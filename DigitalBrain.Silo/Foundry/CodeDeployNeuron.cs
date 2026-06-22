using DigitalBrain.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Silo.Foundry;

[GrainType("digitalbrain.codedeploy.v1")]
public class CodeDeployNeuron : Neuron, ICodeDeployNeuron
{
    public CodeDeployNeuron(ILogger<CodeDeployNeuron> logger) : base(logger) { }

    public async Task HandleAsync(DeployGeneratedCode cmd)
    {
        if (RestartPending())
        {
            await FireAsync(new FoundryRolledBack(cmd.ModuleName, "deploy-in-progress", LastCheckpointId()));
            return;
        }

        var buildRunner = ServiceProvider.GetRequiredService<IBuildRunner>();
        var outcome = await buildRunner.VerifyBuildAsync(cmd.ModuleName, cmd.Source);
        await FireAsync(new CodeBuilt(cmd.ModuleName, outcome.Success, outcome.Log));

        if (!outcome.Success)
        {
            await FireAsync(new FoundryRolledBack(cmd.ModuleName, "build", LastCheckpointId()));
            return;
        }

        CommitSource(cmd.ModuleName, cmd.Source);

        var resourceController = ServiceProvider.GetRequiredService<IResourceController>();
        await resourceController.RestartSiloAsync("apply-" + cmd.ModuleName);
        await FireAsync(new SiloRestartRequested("apply-" + cmd.ModuleName, cmd.ModuleName));
    }

    private bool RestartPending()
    {
        var lastRestart = OutgoingJournal.OfType<SiloRestartRequested>().LastOrDefault();
        if (lastRestart is null) return false;
        // A NeuronActivated fired after the restart request means the silo already came back.
        var activatedAfter = OutgoingJournal
            .SkipWhile(s => !ReferenceEquals(s, lastRestart))
            .OfType<NeuronActivated>()
            .Any();
        return !activatedAfter;
    }

    private string LastCheckpointId() =>
        OutgoingJournal.OfType<FoundryCheckpointed>().LastOrDefault()?.CheckpointId ?? "none";

    private static void CommitSource(string moduleName, string source)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Generated");
        // Resolve to the source tree Generated folder when running from the silo project.
        var projectGenerated = Path.Combine(Directory.GetCurrentDirectory(), "Generated");
        var target = Directory.Exists(projectGenerated) ? projectGenerated : dir;
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, moduleName + ".cs"), source);
    }
}
