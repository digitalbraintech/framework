using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests.Trust;

public class TrustedSeedInstallTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) => builder.ConfigureServices(services =>
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Marketplace:RejectUnsignedPacks"] = "true"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
    });

    [Fact]
    public async Task Under_Strict_Default_Signed_Seed_Installs_But_Unsigned_Is_Rejected()
    {
        var market = Grain<IMarketplaceNeuron>("market-trusted");

        var seed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
        await market.FireAsync(seed);
        await market.FireAsync(new InstallFromMarketplace(seed.PackName, seed.Version, "buyer"));

        await market.FireAsync(new PublishToMarketplace("UnsignedPack", "1.0", Code: "public class U {}", OwnerId: "stranger"));
        await market.FireAsync(new InstallFromMarketplace("UnsignedPack", "1.0", "buyer"));

        var installed = (await market.GetTimelineAsync()).OfType<NeuroPackInstalled>().Select(i => i.Pack.Name).ToArray();
        Assert.Contains(seed.PackName, installed);
        Assert.DoesNotContain("UnsignedPack", installed);
    }

    // Pure unit test — no TestCluster today, and none needed: only exercises MarketplaceSeeds/
    // PackSignatureVerifier statics. A plain nested class (no NeuronTestBase) keeps it at that cost.
    public sealed class SignatureVerificationTests
    {
        [Fact]
        public void Trusted_Publisher_Signs_Seeds_So_They_Verify()
        {
            var signed = MarketplaceSeeds.ToPublishCommand(MarketplaceSeeds.LocalUiPacks[0]);
            var pack = new NeuroPack(signed.PackName, signed.Version, signed.OwnerId, signed.IsPrivate,
                signed.CommissionRate, signed.Code, signed.Description, signed.AuthorPublicKeyBase64, signed.SignatureBase64, signed.Price);
            Assert.True(PackSignatureVerifier.VerifyPack(pack));
        }
    }
}
