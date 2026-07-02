using DigitalBrain.Kernel;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Kernel;

// Verifies that the DigitalBrainTimeline stream provider is registered and the Timeline() extension resolves the correct stream.
public class TimelineStreamTests : NeuronTestBase
{
    protected override void ConfigureClient(IClientBuilder builder) =>
        builder.AddMemoryStreams(SynapseStream.ProviderName);

    [Fact]
    public void ProviderName_Is_DigitalBrainTimeline()
    {
        Assert.Equal("DigitalBrainTimeline", SynapseStream.ProviderName);
    }

    [Fact]
    public void Timeline_Extension_Returns_Stream_For_Global_Namespace()
    {
        var provider = Cluster.Client.GetStreamProvider(SynapseStream.ProviderName);
        var stream = provider.Timeline();

        Assert.NotNull(stream);
        Assert.Equal("global", stream.StreamId.GetKeyAsString());
    }
}
