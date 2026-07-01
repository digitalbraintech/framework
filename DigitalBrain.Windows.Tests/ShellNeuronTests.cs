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
