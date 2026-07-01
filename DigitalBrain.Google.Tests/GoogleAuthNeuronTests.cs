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
