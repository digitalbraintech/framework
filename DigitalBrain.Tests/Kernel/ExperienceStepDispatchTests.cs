using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.Tests.E2E;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Kernel;

[Collection("silo-host")]
public class ExperienceStepDispatchTests : NeuronTestBase
{
    private readonly HomeFeedBus _homeFeedBus = new();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false"
                    })
                    .Build());
            services.AddSingleton(_homeFeedBus);
        });

    [Fact]
    public async Task ExperienceStep_start_emits_intro_surface_to_home_feed()
    {
        var pack = new NeuroPack(
            Name: "travel",
            Version: "1.0",
            Code: TravelPackSource.Read(),
            OwnerId: "test",
            IsPrivate: false,
            CommissionRate: 0,
            Description: "travel domain");

        var generated = Grain<IGeneratedNeuron>("generated-travel");
        await generated.DeliverAsync(new NeuroPackInstalled(pack));

        using var sub = _homeFeedBus.Subscribe();

        await generated.FireAsync(new ExperienceStep(
            Pack: "travel",
            ExperienceId: "plan-trip",
            EventName: "start",
            Args: new Dictionary<string, string> { ["prompt"] = "plan a trip to Bali next month" }));

        var card = await ReadUntilAsync(sub.Reader, TimeSpan.FromSeconds(10),
            c => c.CorrelationId == "travel-intro");
        Assert.Equal("travel-intro", card.CorrelationId);
        Assert.Contains("WEATHER", card.DataJson);
    }

    static async Task<RfwCard> ReadUntilAsync(
        System.Threading.Channels.ChannelReader<RfwCard> reader,
        TimeSpan timeout,
        Func<RfwCard, bool> predicate)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var card in reader.ReadAllAsync(cts.Token))
        {
            if (predicate(card)) return card;
        }
        throw new TimeoutException("No matching RfwCard arrived within the timeout.");
    }
}
