# NeuronTestBase Extraction and Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract a reusable `NeuronTestBase` class into `DigitalBrain.TestKit` and migrate all 14 existing
grain-test files across the 8 per-integration ino `.Tests` projects to inherit it, eliminating the
hand-rolled `IAsyncLifetime` + `new TestDigitalBrain(...)` boilerplate duplicated in every one of them.

**Architecture:** `NeuronTestBase` is an abstract class implementing `IAsyncLifetime`, internally owning a
`TestDigitalBrain` instance (the existing facade over a real in-memory Orleans `TestCluster`). It exposes the
same three operations `TestDigitalBrain` already has (`Grain<T>`, `FireAsync`, `DeliverAsync`) as protected
members, plus a `protected virtual void ConfigureSilo(ISiloBuilder builder)` hook for the one project
(`Google.Tests`) that needs to inject test doubles into the silo's DI container.

**Tech Stack:** .NET (net11.0), Orleans 10.2 (`Microsoft.Orleans.TestingHost`), xUnit (`IAsyncLifetime`).

## Global Constraints

- Every migration in this plan is a mechanical, behavior-preserving lift-and-shift — no assertions, no
  scenario logic, and no test names change. If a test passed before migration, it must pass after, unchanged.
- No new NuGet packages are introduced. `NeuronTestBase` uses only what `DigitalBrain.TestKit` already
  references (`Microsoft.Orleans.TestingHost`, `xunit`) and types already globally available solution-wide
  (`ISiloBuilder` from `Orleans.Hosting`, confirmed present in every affected project's generated global
  usings).
- No `.csproj` changes are needed — every affected test project already references
  `DigitalBrain.TestKit`.
- Self-explanatory naming; no vacuous `/// <summary>` comments. Existing inline comments that explain
  non-obvious history (e.g. "closes a pre-existing gap...") are preserved verbatim during migration, not
  rewritten.
- Verification per task: `dotnet build` (implicit in `dotnet test`) → `dotnet test <project>.csproj` for the
  one project touched — targeted, not a blanket full-suite run, matching this repo's own stated convention
  (`docs/SYSTEM_DESIGN.md` §2.1/§2.5).
- Relative paths only; never reference `C:\Users\` paths.
- Run commands from the `brain/` directory (the repo root for this work).

---

### Task 1: Add `NeuronTestBase` (TDD: failing test first)

**Files:**
- Create: `DigitalBrain.TestKit.Tests/NeuronTestBaseTests.cs`
- Create: `DigitalBrain.TestKit/NeuronTestBase.cs`
- Test: `DigitalBrain.TestKit.Tests/NeuronTestBaseTests.cs`

**Interfaces:**
- Produces: `DigitalBrain.TestKit.NeuronTestBase`, an `abstract class : IAsyncLifetime` with:
  - `protected virtual void ConfigureSilo(ISiloBuilder builder)` — override point for per-test DI overrides.
  - `protected TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey`
  - `protected Task FireAsync<T>(T synapse) where T : Synapse`
  - `protected Task DeliverAsync<T>(T synapse) where T : Synapse`
  - `public Task InitializeAsync()` / `public Task DisposeAsync()` (from `IAsyncLifetime`)
- Consumes: existing `DigitalBrain.TestKit.TestDigitalBrain` (constructor `TestDigitalBrain(Action<ISiloBuilder>? extend = null)`) and `DigitalBrain.Core.Synapse`, `DigitalBrain.TestKit.IDemoNeuron` (already present in the codebase).

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.TestKit.Tests/NeuronTestBaseTests.cs`:

```csharp
using Xunit;

namespace DigitalBrain.TestKit.Tests;

public class NeuronTestBaseTests : NeuronTestBase
{
    [Fact]
    public async Task Grain_Resolves_And_Returns_A_Live_Timeline()
    {
        var target = Grain<IDemoNeuron>("neuron-test-base-smoke");
        var timeline = await target.GetTimelineAsync();
        Assert.NotNull(timeline);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.TestKit.Tests/DigitalBrain.TestKit.Tests.csproj`
Expected: Build FAILS with `error CS0246: The type or namespace name 'NeuronTestBase' could not be found`.

- [ ] **Step 3: Implement `NeuronTestBase`**

Create `DigitalBrain.TestKit/NeuronTestBase.cs`:

```csharp
using DigitalBrain.Core;

namespace DigitalBrain.TestKit;

public abstract class NeuronTestBase : IAsyncLifetime
{
    private TestDigitalBrain _brain = null!;

    protected virtual void ConfigureSilo(ISiloBuilder builder) { }

    protected TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => _brain.Grain<TGrain>(key);
    protected Task FireAsync<T>(T synapse) where T : Synapse => _brain.FireAsync(synapse);
    protected Task DeliverAsync<T>(T synapse) where T : Synapse => _brain.DeliverAsync(synapse);

    public Task InitializeAsync()
    {
        _brain = new TestDigitalBrain(ConfigureSilo);
        return _brain.InitializeAsync();
    }

    public Task DisposeAsync() => _brain.DisposeAsync();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.TestKit.Tests/DigitalBrain.TestKit.Tests.csproj`
Expected: PASS — 2 tests succeed (the existing `TestDigitalBrainTests.FireAsync_Delivers_To_Self_Addressed_Grain` plus the new `NeuronTestBaseTests.Grain_Resolves_And_Returns_A_Live_Timeline`).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.TestKit/NeuronTestBase.cs DigitalBrain.TestKit.Tests/NeuronTestBaseTests.cs
git commit -m "test(kit): add NeuronTestBase to remove per-test TestDigitalBrain boilerplate"
```

---

### Task 2: Migrate `DigitalBrain.Context.Tests`

**Files:**
- Modify: `DigitalBrain.Context.Tests/ContextNeuronTests.cs`
- Test: same file (2 existing `[Fact]`s, unchanged assertions)

**Interfaces:**
- Consumes: `DigitalBrain.TestKit.NeuronTestBase` (Task 1).

- [ ] **Step 1: Replace the class declaration and grain-resolution calls**

Replace the full contents of `DigitalBrain.Context.Tests/ContextNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Context.Tests;

public class ContextNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Remember_Then_Recall_Finds_The_Text()
    {
        var context = Grain<IContextNeuron>("context-recall-test");
        await context.RememberAsync("the sky is blue today");
        var results = await context.RecallAsync("sky color");
        Assert.Contains("the sky is blue today", results);
    }

    [Fact]
    public async Task Signal_RecallRequested_Replies_With_Signal_RecallCompleted()
    {
        var context = Grain<IContextNeuron>("context-signal-test");
        await context.RememberAsync("the launch date is March 5th");

        await context.DeliverAsync(new Signal(
            ContextSignals.RecallRequested,
            new Dictionary<string, object?> { ["query"] = "launch date", ["chatId"] = 123L })
        { Receiver = new NeuronId("context-signal-test") });

        var outgoing = await context.GetTimelineAsync();
        Assert.Contains(outgoing, s => s is Signal reply
            && reply.Name == ContextSignals.RecallCompleted
            && reply.Props.TryGetValue("results", out var r)
            && r is string[] results && results.Contains("the launch date is March 5th")
            && reply.Props.TryGetValue("chatId", out var c) && Equals(c, 123L)
            && !reply.Props.ContainsKey("query"));
    }
}
```

- [ ] **Step 2: Run tests to verify they still pass**

Run: `dotnet test DigitalBrain.Context.Tests/DigitalBrain.Context.Tests.csproj`
Expected: PASS — 2 tests, same assertions as before migration.

- [ ] **Step 3: Commit**

```bash
git add DigitalBrain.Context.Tests/ContextNeuronTests.cs
git commit -m "test(context): migrate ContextNeuronTests to NeuronTestBase"
```

---

### Task 3: Migrate `DigitalBrain.Telegram.Channel.Tests`

**Files:**
- Modify: `DigitalBrain.Telegram.Channel.Tests/TelegramChatNeuronTests.cs`
- Test: same file (9 existing `[Fact]`s, unchanged assertions)

**Interfaces:**
- Consumes: `DigitalBrain.TestKit.NeuronTestBase` (Task 1).

- [ ] **Step 1: Replace the class declaration and grain-resolution calls**

Replace the full contents of `DigitalBrain.Telegram.Channel.Tests/TelegramChatNeuronTests.cs` with:

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.UiKit;
using Xunit;

namespace DigitalBrain.Telegram.Channel.Tests;

public class TelegramChatNeuronTests : NeuronTestBase
{
    private static Signal Inbound(long chatId, string text) =>
        new("TelegramMessageReceived", new Dictionary<string, object?>
        {
            ["chatId"] = chatId, ["fromUserId"] = 1L, ["text"] = text, ["updateId"] = 1L
        });

    [Fact]
    public async Task Start_command_binds_the_chat_and_confirms()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-100");
        await chat.DeliverAsync(Inbound(100, "/start hello-world"));

        Assert.Equal("hello-world", await chat.GetBoundBundleAsync());

        var reply = (await chat.GetOutgoingTimelineAsync())
            .OfType<Signal>().Single(s => s.Name == "TelegramReplyRequested");
        Assert.Equal(100L, System.Convert.ToInt64(reply.Props["chatId"]));
        Assert.Contains("hello-world", reply.Props["text"]?.ToString());
    }

    [Fact]
    public async Task Latest_start_wins_as_the_binding()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-101");
        await chat.DeliverAsync(Inbound(101, "/start alpha"));
        await chat.DeliverAsync(Inbound(101, "/start beta"));

        Assert.Equal("beta", await chat.GetBoundBundleAsync());
    }

    [Fact]
    public async Task Bound_chat_routes_a_normal_message_to_the_bound_bundle()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-102");
        await chat.DeliverAsync(Inbound(102, "/start hello-world"));
        await chat.DeliverAsync(Inbound(102, "hi there"));

        var forwarded = (await chat.GetOutgoingTimelineAsync())
            .OfType<Signal>()
            .Where(s => s.Name == "TelegramMessageReceived" && s.Receiver is not null)
            .ToList();
        Assert.Contains(forwarded, s =>
            s.Receiver!.Value == "generated-hello-world" && s.Props["text"]?.ToString() == "hi there");
    }

    [Fact]
    public async Task Unbound_chat_broadcasts_so_the_default_responder_handles_it()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-103");
        await chat.DeliverAsync(Inbound(103, "just a question"));

        var broadcast = (await chat.GetOutgoingTimelineAsync())
            .OfType<Signal>()
            .Where(s => s.Name == "TelegramMessageReceived" && s.IsBroadcast)
            .ToList();
        Assert.Contains(broadcast, s => s.Props["text"]?.ToString() == "just a question");
    }

    [Fact]
    public async Task Start_without_space_does_not_bind_and_broadcasts()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-104");
        await chat.DeliverAsync(Inbound(104, "/startfoo"));

        Assert.Null(await chat.GetBoundBundleAsync());

        var broadcast = (await chat.GetOutgoingTimelineAsync())
            .OfType<Signal>()
            .Where(s => s.Name == "TelegramMessageReceived" && s.IsBroadcast)
            .ToList();
        Assert.Contains(broadcast, s => s.Props["text"]?.ToString() == "/startfoo");
    }

    [Fact]
    public async Task Forwarded_message_preserves_causation_from_the_inbound()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-105");
        await chat.DeliverAsync(Inbound(105, "/start hello-world"));
        await chat.DeliverAsync(Inbound(105, "hi there"));

        var inbound = (await chat.GetIncomingTimelineAsync())
            .OfType<Signal>().Last(s => s.Name == "TelegramMessageReceived" && s.Props["text"]?.ToString() == "hi there");
        var forwarded = (await chat.GetOutgoingTimelineAsync())
            .OfType<Signal>().Single(s => s.Name == "TelegramMessageReceived" && s.Receiver is not null && s.Props["text"]?.ToString() == "hi there");

        Assert.Equal(inbound.SynapseId, forwarded.CausationId);
    }

    [Fact]
    public async Task Ignores_broadcast_echoes_to_avoid_self_loop()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-106");
        await chat.DeliverAsync(Inbound(106, "hello") with { IsBroadcast = true });

        var reactions = (await chat.GetOutgoingTimelineAsync())
            .OfType<Signal>()
            .Where(s => s.Name == "TelegramMessageReceived" || s.Name == "TelegramReplyRequested")
            .ToList();
        Assert.Empty(reactions);
    }

    [Fact]
    public async Task Telegram_viz_signal_produces_UiSurface_handled_by_FlutterUiNeuron()
    {
        var chat = Grain<ITelegramChatNeuron>("tg-chat-viz1");
        await chat.DeliverAsync(Inbound(300, "chart my excel sales data"));

        var chart = Grain<IDataVisualizationNeuron>("viz-default");
        var chartOut = await chart.GetOutgoingTimelineAsync();
        Assert.Contains(chartOut, s => s is DataChartGenerated || s is UiSurface);

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var flIncoming = await flutter.GetIncomingTimelineAsync();
        Assert.Contains(flIncoming, s => s is UiSurface u && (u.Kind == UiSurfaceKinds.DataChart || u.Props.ContainsKey("chartSpec") || u.Props.ContainsKey("tree")));
        Assert.Contains(flIncoming, s => s is UiSurface u && u.Props.TryGetValue("originChannel", out var oc) && oc?.ToString() == "telegram");
        Assert.Contains(flIncoming, s => s is UiSurface u && u.Props.TryGetValue("title", out var t) && (t?.ToString()?.Contains("(from Telegram)") ?? false));
        Assert.Contains(flIncoming, s => s is UiSurface u && u.Props.TryGetValue("channelContext", out var cc) && (cc?.ToString()?.Contains("tg") ?? false));
    }
}
```

- [ ] **Step 2: Run tests to verify they still pass**

Run: `dotnet test DigitalBrain.Telegram.Channel.Tests/DigitalBrain.Telegram.Channel.Tests.csproj`
Expected: PASS — 9 tests, same assertions as before migration.

- [ ] **Step 3: Commit**

```bash
git add DigitalBrain.Telegram.Channel.Tests/TelegramChatNeuronTests.cs
git commit -m "test(telegram-channel): migrate TelegramChatNeuronTests to NeuronTestBase"
```

---

### Task 4: Migrate `DigitalBrain.UiKit.Tests`

**Files:**
- Modify: `DigitalBrain.UiKit.Tests/FlutterUiNeuronTests.cs`
- Test: same file (1 existing `[Fact]`, unchanged assertions)

**Interfaces:**
- Consumes: `DigitalBrain.TestKit.NeuronTestBase` (Task 1).

- [ ] **Step 1: Replace the class declaration and grain-resolution calls**

Replace the full contents of `DigitalBrain.UiKit.Tests/FlutterUiNeuronTests.cs` with:

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.UiKit.Tests;

// Closes a pre-existing gap: IFlutterUiNeuron had no direct test before this plan — it was
// only exercised indirectly inside a Telegram test.
public class FlutterUiNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task HandleAsync_Records_The_Delivered_UiSurface_In_The_Incoming_Journal()
    {
        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surface = new UiSurface("test-kind", new Dictionary<string, object?> { ["title"] = "smoke" });

        // DeliverAsync (not HandleAsync directly) is the real entry point every production caller uses
        // (see DataVisualizationNeuron) — it's what records the incoming journal before dispatching to
        // FlutterUiNeuron.HandleAsync via the declared IHandle<UiSurface> interface.
        await flutter.DeliverAsync(surface);

        var incoming = await flutter.GetIncomingTimelineAsync();
        Assert.Contains(incoming, s => s is UiSurface delivered && delivered.Kind == "test-kind");
    }
}
```

- [ ] **Step 2: Run tests to verify they still pass**

Run: `dotnet test DigitalBrain.UiKit.Tests/DigitalBrain.UiKit.Tests.csproj`
Expected: PASS — 1 test, same assertion as before migration.

- [ ] **Step 3: Commit**

```bash
git add DigitalBrain.UiKit.Tests/FlutterUiNeuronTests.cs
git commit -m "test(uikit): migrate FlutterUiNeuronTests to NeuronTestBase"
```

---

### Task 5: Migrate `DigitalBrain.Developer.Tests` (4 files)

**Files:**
- Modify: `DigitalBrain.Developer.Tests/GitNeuronTests.cs`
- Modify: `DigitalBrain.Developer.Tests/RoslynNeuronTests.cs`
- Modify: `DigitalBrain.Developer.Tests/NuGetNeuronTests.cs`
- Modify: `DigitalBrain.Developer.Tests/DotNetNeuronTests.cs`
- Not touched: `DigitalBrain.Developer.Tests/RoslynAnalysisServiceTests.cs` (pure C#, no Orleans, no `TestDigitalBrain` usage — out of scope)
- Test: all four modified files, unchanged assertions

**Interfaces:**
- Consumes: `DigitalBrain.TestKit.NeuronTestBase` (Task 1).

- [ ] **Step 1: Migrate `GitNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Developer.Tests/GitNeuronTests.cs` with:

```csharp
using System.Diagnostics;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class GitNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Status_Works()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbgit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var init = new ProcessStartInfo("git", "init -b main")
            { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(init)!) process.WaitForExit();

            var git = Grain<IGitNeuron>("git-smoke");
            var status = await git.StatusAsync(dir);
            Assert.Contains("branch", status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
```

- [ ] **Step 2: Migrate `RoslynNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Developer.Tests/RoslynNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Developer.Tests;

// Closes a pre-existing zero-coverage gap: RoslynNeuron had no test before this plan.
// In-proc (MSBuildWorkspace), safe to run for real against the actual solution.
public class RoslynNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Analyzes_Real_Solution()
    {
        var roslyn = Grain<IRoslynNeuron>("roslyn-test");
        var solutionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Brain.slnx"));
        var result = await roslyn.AnalyzeSolutionAsync(solutionPath);
        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
```

- [ ] **Step 3: Migrate `NuGetNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Developer.Tests/NuGetNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Developer.Tests;

// Closes a pre-existing zero-coverage gap: NuGetNeuron had no test before this plan.
// INuGetNeuron has no SearchAsync member (ListPackages/ListOutdated/Restore/AddPackage only) —
// deviates from the brief's literal snapshot, which named a non-existent method; ListPackagesAsync
// against this project's own csproj is the equivalent zero-network smoke check.
public class NuGetNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task ListPackages_Returns_Zero_Exit_Code()
    {
        var nuget = Grain<INuGetNeuron>("nuget-test");
        var csprojPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "DigitalBrain.Developer", "DigitalBrain.Developer.csproj"));
        var result = await nuget.ListPackagesAsync(csprojPath);
        Assert.Equal(0, result.ExitCode);
    }
}
```

- [ ] **Step 4: Migrate `DotNetNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Developer.Tests/DotNetNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Developer.Tests;

public class DotNetNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Reports_Sdk_Version()
    {
        var dotnet = Grain<IDotNetNeuron>("dotnet-test");
        var version = await dotnet.VersionAsync();
        Assert.Matches(@"\d+\.\d+", version);
    }
}
```

- [ ] **Step 5: Run tests to verify they still pass**

Run: `dotnet test DigitalBrain.Developer.Tests/DigitalBrain.Developer.Tests.csproj`
Expected: PASS — all tests in the project (Git, Roslyn, NuGet, DotNet, plus untouched `RoslynAnalysisServiceTests`), same assertions as before migration.

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Developer.Tests/GitNeuronTests.cs DigitalBrain.Developer.Tests/RoslynNeuronTests.cs DigitalBrain.Developer.Tests/NuGetNeuronTests.cs DigitalBrain.Developer.Tests/DotNetNeuronTests.cs
git commit -m "test(developer): migrate Git/Roslyn/NuGet/DotNet neuron tests to NeuronTestBase"
```

---

### Task 6: Migrate `DigitalBrain.Google.Tests` (4 files)

**Files:**
- Modify: `DigitalBrain.Google.Tests/GmailNeuronTests.cs`
- Modify: `DigitalBrain.Google.Tests/GoogleCalendarNeuronTests.cs`
- Modify: `DigitalBrain.Google.Tests/GoogleDriveNeuronTests.cs`
- Modify: `DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs`
- Not touched: `DigitalBrain.Google.Tests/FakeGoogleApiClients.cs` (fakes, no lifecycle code)
- Test: all four modified files, unchanged assertions

**Interfaces:**
- Consumes: `DigitalBrain.TestKit.NeuronTestBase` (Task 1), specifically the `protected virtual void ConfigureSilo(ISiloBuilder builder)` override point — three of these four files inject a fake API client into the silo's DI container and need it.

- [ ] **Step 1: Migrate `GmailNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Google.Tests/GmailNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GmailNeuronTests : NeuronTestBase
{
    private readonly FakeGmailApiClient _fake = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IGmailApiClient>(_fake));

    [Fact]
    public async Task SendMessageAsync_Records_The_Send_On_The_Fake()
    {
        var gmail = Grain<IGmailNeuron>("gmail-test");
        await gmail.SendMessageAsync("someone@example.com", "hi", "hello there");
        Assert.Single(_fake.SentMessages, m => m.To == "someone@example.com" && m.Subject == "hi");
    }

    [Fact]
    public async Task ListMessagesAsync_Returns_Fake_Results()
    {
        var gmail = Grain<IGmailNeuron>("gmail-list-test");
        var messages = await gmail.ListMessagesAsync("is:unread", 10);
        Assert.NotEmpty(messages);
    }
}
```

- [ ] **Step 2: Migrate `GoogleCalendarNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Google.Tests/GoogleCalendarNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleCalendarNeuronTests : NeuronTestBase
{
    private readonly FakeGoogleCalendarApiClient _fake = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IGoogleCalendarApiClient>(_fake));

    [Fact]
    public async Task CreateEventAsync_Then_ListEventsAsync_Returns_The_Created_Event()
    {
        var calendar = Grain<IGoogleCalendarNeuron>("calendar-test");
        var eventId = await calendar.CreateEventAsync(
            "Standup", "2026-07-02T09:00:00Z", "2026-07-02T09:30:00Z", "Daily standup");
        var events = await calendar.ListEventsAsync("2026-07-01T00:00:00Z", "2026-07-03T00:00:00Z");
        Assert.Contains(events, e => e.StartsWith(eventId));
    }

    [Fact]
    public async Task DeleteEventAsync_Removes_Event_From_Fake()
    {
        var calendar = Grain<IGoogleCalendarNeuron>("calendar-delete-test");
        var eventId = await calendar.CreateEventAsync(
            "Cancel me", "2026-07-02T09:00:00Z", "2026-07-02T09:30:00Z", "");
        await calendar.DeleteEventAsync(eventId);
        var events = await calendar.ListEventsAsync("2026-07-01T00:00:00Z", "2026-07-03T00:00:00Z");
        Assert.DoesNotContain(events, e => e.StartsWith(eventId));
    }
}
```

- [ ] **Step 3: Migrate `GoogleDriveNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Google.Tests/GoogleDriveNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleDriveNeuronTests : NeuronTestBase
{
    private readonly FakeGoogleDriveApiClient _fake = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IGoogleDriveApiClient>(_fake));

    [Fact]
    public async Task UploadFileAsync_Then_DownloadFileAsync_Round_Trips_Content()
    {
        var drive = Grain<IGoogleDriveNeuron>("drive-test");
        var fileId = await drive.UploadFileAsync("notes.txt", "hello drive", "text/plain");
        var content = await drive.DownloadFileAsync(fileId);
        Assert.Equal("hello drive", content);
    }

    [Fact]
    public async Task DeleteFileAsync_Removes_File_From_Fake()
    {
        var drive = Grain<IGoogleDriveNeuron>("drive-delete-test");
        var fileId = await drive.UploadFileAsync("temp.txt", "temp", "text/plain");
        await drive.DeleteFileAsync(fileId);
        var files = await drive.ListFilesAsync("");
        Assert.DoesNotContain(fileId, files);
    }
}
```

- [ ] **Step 4: Migrate `GoogleAuthNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs` with:

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleAuthNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task AuthRequested_Fires_AuthCompleted()
    {
        var auth = Grain<IGoogleAuthNeuron>("google-auth-test");
        await auth.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("google-auth-test") });

        var outgoing = await auth.GetTimelineAsync();
        Assert.Contains(outgoing, s => s is Signal reply && reply.Name == GoogleSignals.AuthCompleted);
    }
}
```

- [ ] **Step 5: Run tests to verify they still pass**

Run: `dotnet test DigitalBrain.Google.Tests/DigitalBrain.Google.Tests.csproj`
Expected: PASS — 7 tests (2 Gmail + 2 Calendar + 2 Drive + 1 Auth), same assertions as before migration.

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Google.Tests/GmailNeuronTests.cs DigitalBrain.Google.Tests/GoogleCalendarNeuronTests.cs DigitalBrain.Google.Tests/GoogleDriveNeuronTests.cs DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs
git commit -m "test(google): migrate Gmail/Calendar/Drive/Auth neuron tests to NeuronTestBase"
```

---

### Task 7: Migrate `DigitalBrain.Windows.Tests` (3 files)

**Files:**
- Modify: `DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs`
- Modify: `DigitalBrain.Windows.Tests/ShellNeuronTests.cs`
- Modify: `DigitalBrain.Windows.Tests/WingetNeuronTests.cs`
- Not touched: `DigitalBrain.Windows.Tests/FileSystemOperationsTests.cs` (pure C#, no Orleans, no `TestDigitalBrain` usage — out of scope)
- Test: all three modified files, unchanged assertions

**Interfaces:**
- Consumes: `DigitalBrain.TestKit.NeuronTestBase` (Task 1).

- [ ] **Step 1: Migrate `FileSystemNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Windows.Tests;

public class FileSystemNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Write_Read_List_Delete_RoundTrip()
    {
        var fs = Grain<IFileSystemNeuron>("fs-test");
        var dir = Path.Combine(Path.GetTempPath(), "dbfs-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "note.txt");
        try
        {
            await fs.WriteFileAsync(file, "hello fs");
            Assert.True(await fs.ExistsAsync(file));
            Assert.Equal("hello fs", await fs.ReadFileAsync(file));
            Assert.Contains(file, await fs.ListFilesAsync(dir, "*.txt"));
            await fs.DeleteAsync(file);
            Assert.False(await fs.ExistsAsync(file));
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
```

- [ ] **Step 2: Migrate `ShellNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Windows.Tests/ShellNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Windows.Tests;

public class ShellNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task Executes_Echo()
    {
        var shell = Grain<IShellNeuron>("shell-test");
        var result = await shell.ExecuteAsync("echo digitalbrain");
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("digitalbrain", result.Output);
    }

    [Fact]
    public async Task Blocks_Dangerous_Command()
    {
        var shell = Grain<IShellNeuron>("shell-block");
        var result = await shell.ExecuteAsync("format c:");
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Migrate `WingetNeuronTests.cs`**

Replace the full contents of `DigitalBrain.Windows.Tests/WingetNeuronTests.cs` with:

```csharp
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Windows.Tests;

// Closes a pre-existing zero-coverage gap: WingetNeuron had no test before this plan.
// Only read-only operations (List/Search) run for real — Install/UpgradeAll mutate the host
// and are intentionally not exercised here.
public class WingetNeuronTests : NeuronTestBase
{
    [Fact]
    public async Task List_Returns_Zero_Exit_Code()
    {
        var winget = Grain<IWingetNeuron>("winget-test");
        var result = await winget.ListAsync();
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Search_Returns_Zero_Exit_Code()
    {
        var winget = Grain<IWingetNeuron>("winget-search-test");
        var result = await winget.SearchAsync("git");
        Assert.Equal(0, result.ExitCode);
    }
}
```

- [ ] **Step 4: Run tests to verify they still pass**

Run: `dotnet test DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj`
Expected: PASS — all tests in the project (FileSystem, Shell, Winget, plus untouched `FileSystemOperationsTests`), same assertions as before migration.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Windows.Tests/FileSystemNeuronTests.cs DigitalBrain.Windows.Tests/ShellNeuronTests.cs DigitalBrain.Windows.Tests/WingetNeuronTests.cs
git commit -m "test(windows): migrate FileSystem/Shell/Winget neuron tests to NeuronTestBase"
```

---

### Task 8: Final verification pass

**Files:** none (verification only)

**Interfaces:** none

- [ ] **Step 1: Full solution build**

Run: `dotnet build Brain.slnx`
Expected: Build succeeds, 0 errors.

- [ ] **Step 2: Re-run every touched project's tests together**

Run:
```bash
dotnet test DigitalBrain.TestKit.Tests/DigitalBrain.TestKit.Tests.csproj
dotnet test DigitalBrain.Context.Tests/DigitalBrain.Context.Tests.csproj
dotnet test DigitalBrain.Telegram.Channel.Tests/DigitalBrain.Telegram.Channel.Tests.csproj
dotnet test DigitalBrain.UiKit.Tests/DigitalBrain.UiKit.Tests.csproj
dotnet test DigitalBrain.Developer.Tests/DigitalBrain.Developer.Tests.csproj
dotnet test DigitalBrain.Google.Tests/DigitalBrain.Google.Tests.csproj
dotnet test DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj
```
Expected: All PASS, 0 failures across all seven projects.

- [ ] **Step 3: Confirm no other file in the repo still references the old per-test boilerplate pattern**

Run: `grep -rl "IAsyncLifetime" --include=*.cs DigitalBrain.Context.Tests DigitalBrain.Telegram.Channel.Tests DigitalBrain.UiKit.Tests DigitalBrain.Developer.Tests DigitalBrain.Google.Tests DigitalBrain.Windows.Tests`
Expected: No output (every migrated file now inherits `NeuronTestBase` instead of implementing `IAsyncLifetime` directly).

- [ ] **Step 4: Commit** (only if Step 3 found and fixed a straggler; otherwise no-op)

---

## Self-Review Notes

- **Spec coverage:** This plan implements design section "1. `NeuronTestBase`" and "2. Per-ino migration table"
  from `docs/specs/2026-07-02-neuron-test-harness-consolidation-design.md` in full (all 14 files + the
  `TestKit.Tests` companion test). Design sections 3 (`UnitTest1.cs` split) and 4 (quality audit) are
  explicitly out of scope for this plan — each gets its own plan once this one has landed, per the spec's
  "Suggested sequencing."
- **Placeholder scan:** No TBD/TODO; every step shows the complete file content, not a diff description.
- **Type consistency:** `Grain<T>`, `FireAsync<T>`, `DeliverAsync<T>`, and `ConfigureSilo` are defined once in
  Task 1 and used identically (same names, same signatures) in every subsequent task.
