using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Sdk;

// Functional tests for the typed SDK integration neurons against the in-process TestCluster.
public class SdkNeuronsTests : IAsyncLifetime
{
    private TestCluster? _cluster;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<NeuronTestSiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
            await _cluster.StopAllSilosAsync();
    }

    [Fact]
    public async Task DotNet_Reports_Sdk_Version()
    {
        var dotnet = _cluster!.GrainFactory.GetGrain<IDotNetNeuron>("dotnet-test");
        var version = await dotnet.VersionAsync();
        Assert.Matches(@"\d+\.\d+", version);
    }

    [Fact]
    public async Task Git_Status_Works_After_ProcessRunner_Refactor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dbgit2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var init = new System.Diagnostics.ProcessStartInfo("git", "init -b main")
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = System.Diagnostics.Process.Start(init)!)
                process.WaitForExit();

            var git = _cluster!.GrainFactory.GetGrain<IGitNeuron>("git-smoke");
            var status = await git.StatusAsync(dir);
            Assert.Contains("branch", status, System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }
}

