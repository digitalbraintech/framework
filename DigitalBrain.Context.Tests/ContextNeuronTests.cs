using DigitalBrain.TestKit;
using DigitalBrain.Context;
using Xunit;

namespace DigitalBrain.Context.Tests;

public class ContextNeuronTests : IAsyncLifetime
{
    private readonly TestDigitalBrain _brain = new();
    public Task InitializeAsync() => _brain.InitializeAsync();
    public Task DisposeAsync() => _brain.DisposeAsync();

    [Fact]
    public async Task Remember_Then_Recall_Finds_The_Text()
    {
        var context = _brain.Grain<IContextNeuron>("context-recall-test");
        await context.RememberAsync("the sky is blue today");
        var results = await context.RecallAsync("sky color");
        Assert.Contains("the sky is blue today", results);
    }
}
