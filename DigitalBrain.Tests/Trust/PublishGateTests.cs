using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Trust;

public class PublishGateTests : NeuronTestBase
{
    private const string PackCode =
        "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }";

    protected override void ConfigureSilo(ISiloBuilder builder) => builder.ConfigureServices(services =>
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "false",
                ["DigitalBrain:Marketplace:GatePublishing"] = "true"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
    });

    private static async Task<IReadOnlyList<NeuroPack>> ListedAsync(IMarketplaceNeuron market)
    {
        await market.FireAsync(new ListPublished());
        return (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
    }

    [Fact]
    public async Task Gate_on_admits_a_trusted_publisher()
    {
        var market = Grain<IMarketplaceNeuron>("market-gate-trusted");
        var signed = TrustedPublisher.SignPublishCommand(
            new PublishToMarketplace("trusted-pack", "1.0.0", Code: PackCode));
        await market.FireAsync(signed);

        Assert.Contains(await ListedAsync(market), p => p.Name == "trusted-pack");
    }

    [Fact]
    public async Task Gate_on_rejects_a_stranger()
    {
        var market = Grain<IMarketplaceNeuron>("market-gate-stranger");
        var (priv, pub) = PackSignatureVerifier.GenerateKeyPair();
        var stranger = PackSignatureVerifier.SignPack(
            new NeuroPack("stranger-pack", "1.0.0", Code: PackCode), priv, pub);
        await market.FireAsync(new PublishToMarketplace(
            stranger.Name, stranger.Version, Code: stranger.Code,
            AuthorPublicKeyBase64: stranger.AuthorPublicKeyBase64, SignatureBase64: stranger.SignatureBase64));

        Assert.DoesNotContain(await ListedAsync(market), p => p.Name == "stranger-pack");
    }

    // Gating is opt-in per-cluster (the outer class's ConfigureSilo enables it) — this nested class extends
    // NeuronTestBase directly, not PublishGateTests, so it gets the plain default silo config instead of
    // inheriting the gated one.
    public sealed class DefaultUngatedTests : NeuronTestBase
    {
        [Fact]
        public async Task Gate_off_by_default_admits_unsigned()
        {
            var market = Grain<IMarketplaceNeuron>("market-gate-off");
            await market.FireAsync(new PublishToMarketplace("unsigned-pack", "1.0.0", Code: PackCode));

            Assert.Contains(await ListedAsync(market), p => p.Name == "unsigned-pack");
        }
    }
}
