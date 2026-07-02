using DigitalBrain.Context;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Context;

public class ContextRecallTests : NeuronTestBase
{
    [Fact]
    public async Task Remembers_And_Recalls_By_Relevance()
    {
        var context = Grain<IContextNeuron>("ctx-recall");
        await context.RememberAsync("set an alarm clock for 7am");
        await context.RememberAsync("buy milk and eggs from the store");
        await context.RememberAsync("git commit and push the feature branch");

        // With the NoOp embedder, recall degrades to keyword scoring via the hybrid scorer.
        var hits = await context.RecallAsync("alarm", top: 2);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Contains("alarm", System.StringComparison.OrdinalIgnoreCase));
    }
}

