using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;
using OllamaSharp;

namespace DigitalBrain.Silo.Ino;

// INO: ultra-context personal assistant neuron.
// Uses dual journals as primary memory (recent + full history), spawns KernelTasks for actions,
// can drive checkpoints/branches for planning. Context is multi-scale via recency + LLM summary.
[GrainType("ino.personal.v1")]
public class InoNeuron : Neuron, IInoNeuron
{
    private readonly List<string> _activeTasks = new();
    private string _focus = string.Empty;
    private readonly List<MemorySummary> _longTermMemory = new(); // long-term summaries from journals

    public InoNeuron(ILogger<InoNeuron> logger) : base(logger) { }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        // Bootstrap: recent focus + load long-term memory summaries from journal (multi-scale).
        var recent = OutgoingJournal.TakeLast(5).OfType<InoResponse>().LastOrDefault();
        if (recent != null) _focus = recent.Prompt;

        _longTermMemory.Clear();
        _longTermMemory.AddRange(OutgoingJournal.OfType<MemorySummary>().TakeLast(10));
    }

    public async Task HandleAsync(InoRequest req)
    {
        var ctx = await BuildContextAsync(req.Prompt);
        var reply = await ReasonWithLlmAsync(req.Prompt, ctx);

        var taskIds = await OrchestrateActionsIfNeededAsync(req.Prompt, reply);

        // Enrich response with any fresh task results for better context to caller.
        var enriched = reply;
        if (taskIds.Count > 0)
        {
            var infos = new List<string>();
            foreach (var tid in taskIds.Where(t => !t.StartsWith("branch")))
            {
                try
                {
                    var kt = GrainFactory.GetGrain<IKernelTask>(tid);
                    var inf = await kt.GetInfoAsync();
                    if (inf.Result != null) infos.Add($"{tid}={inf.Result}");
                }
                catch { }
            }
            if (infos.Count > 0) enriched += " | " + string.Join("; ", infos);
        }

        await FireAsync(new InoResponse(req.Prompt, enriched, taskIds.ToArray()));
        _focus = req.Prompt;

        // Occasionally compress for long-term memory (multi-scale).
        if (OutgoingJournal.Count % 5 == 0)
        {
            await CreateMemorySummaryAsync();
        }
    }

    public async Task<string> AskAsync(string prompt)
    {
        await FireAsync(new InoRequest(prompt));
        await Task.Delay(120);
        var tl = await GetOutgoingTimelineAsync();
        var last = tl.OfType<InoResponse>().LastOrDefault();
        return last?.Response ?? "processed";
    }

    private async Task<string> BuildContextAsync(string prompt)
    {
        // Multi-scale context:
        // - short-term/working: recent in/out from dual journals
        // - episodic: active tasks + recent branches
        // - long-term: loaded MemorySummaries + cross-neuron
        // - cross-neuron: pull recent from key system neurons (status for awareness, etc.)
        var recentOut = OutgoingJournal.TakeLast(8).Select(s => s.Type + ":" + s.ToString()).ToList();
        var recentIn = IncomingJournal.TakeLast(5).Select(s => "in:" + s.ToString()).ToList();
        var taskCtx = string.Join(";", _activeTasks.Select(tid => "task:" + tid));
        var memCtx = _longTermMemory.Count > 0 ? string.Join(";", _longTermMemory.Select(m => m.Topic + "=" + m.Summary)) : "";

        string cross = "";
        try
        {
            var sys = GrainFactory.GetGrain<ISystemStatus>("status-main");
            var sysTl = await sys.GetOutgoingTimelineAsync();
            cross = "sys:" + string.Join(",", sysTl.TakeLast(3).Select(s => s.Type));
        }
        catch { }

        return $"focus:{_focus}\nprompt:{prompt}\nrecent-out:{string.Join(";", recentOut)}\nrecent-in:{string.Join(";", recentIn)}\ntasks:{taskCtx}\nmem:{memCtx}\ncross:{cross}";
    }

    private async Task<string> ReasonWithLlmAsync(string prompt, string context)
    {
        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm == null) return $"[no-llm] INO would act on: {prompt} (ctx len {context.Length})";

        llm.SelectedModel = "qwen2.5-coder:1.5b";
        var sys = "You are INO, DigitalBrain's personal OS assistant. Use provided context from neuron journals. Be concise, propose kernel tasks or branches when useful. Output action if any as 'TASK: desc' or 'BRANCH: whatif'.";
        var full = sys + "\nCTX:\n" + context + "\nUSER: " + prompt;
        var acc = "";
        await foreach (var ch in llm.GenerateAsync(full))
            if (ch?.Response is string t) acc += t;
        return acc.Trim();
    }

    private async Task<List<string>> OrchestrateActionsIfNeededAsync(string prompt, string reply)
    {
        var created = new List<string>();
        if (reply.Contains("TASK:", StringComparison.OrdinalIgnoreCase))
        {
            var taskDesc = reply.Split("TASK:", 2)[1].Split('\n')[0].Trim();
            var tid = "task-" + Guid.NewGuid().ToString("N")[..8];
            var kt = GrainFactory.GetGrain<IKernelTask>(tid);
            await kt.FireAsync(new RunKernelTask(tid, taskDesc));
            // Capture result for context (poll briefly in prototype)
            await Task.Delay(80);
            var info = await kt.GetInfoAsync();
            if (info.Result != null)
            {
                await FireAsync(new KernelTaskProgress(tid, "result:" + info.Result));
            }
            created.Add(tid);
            _activeTasks.Add(tid);
        }
        if (reply.Contains("BRANCH:", StringComparison.OrdinalIgnoreCase) || prompt.Contains("what if", StringComparison.OrdinalIgnoreCase))
        {
            var cp = await CreateCheckpointAsync();
            var bid = await BranchAsync(cp);
            await FireAsync(new KernelTaskProgress("branch", "created:" + bid.Value));
            created.Add("branch:" + bid.Value);
        }
        return created;
    }

    private async Task CreateMemorySummaryAsync()
    {
        // Long-term: compress recent journal into semantic summary using LLM, store as synapse for persistence.
        var recent = OutgoingJournal.Concat(IncomingJournal).TakeLast(20).ToList();
        if (recent.Count < 5) return;

        var llm = ServiceProvider.GetService<IOllamaApiClient>();
        if (llm == null) return;

        llm.SelectedModel = "qwen2.5-coder:1.5b";
        var ctx = string.Join("\n", recent.Select(s => s.Type + ": " + (s as object)?.ToString()));
        var prompt = "Summarize the following recent activity in DigitalBrain for personal assistant memory. One short topic + 1-sentence summary. Activity:\n" + ctx;
        var acc = "";
        await foreach (var ch in llm.GenerateAsync(prompt))
            if (ch?.Response is string t) acc += t;

        var summaryText = acc.Trim();
        if (summaryText.Length > 10)
        {
            var topic = summaryText.Split('.')[0].Trim();
            var mem = new MemorySummary(topic.Length > 30 ? topic.Substring(0,30) : topic, summaryText, DateTimeOffset.UtcNow);
            await FireAsync(mem);
            _longTermMemory.Add(mem);
            // keep bounded
            if (_longTermMemory.Count > 20) _longTermMemory.RemoveAt(0);
        }
    }
}