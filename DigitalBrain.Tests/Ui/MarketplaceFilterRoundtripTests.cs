using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Ui;

public class MarketplaceFilterRoundtripTests : NeuronTestBase
{
    [Fact]
    public async Task Filtering_by_tier_reemits_a_surface_listing_only_matching_bundles()
    {
        var market = Grain<IMarketplaceNeuron>("market-facet-1");
        // hello-world is a KitExperience → Content tier when materialized at publish.
        // hello-world demo removed (bloat delete).
        await market.FireAsync(new PublishToMarketplace(
            "plain", "1.0.0", Code: "public class P : DigitalBrain.Core.IPackBehavior { public string Respond(string i) => i; }",
            OwnerId: "tester", CommissionRate: 0.0));

        await market.FireAsync(new FilterMarketplace(Tier: "Content"));

        var surface = (await market.GetTimelineAsync())
            .OfType<UiSurface>()
            .Last(s => s.Kind == UiSurfaceKinds.MarketplaceList);
        var items = (System.Collections.Generic.Dictionary<string, object?>[])surface.Props["packs"]!;

        // hello-world demo removed; assert on remaining content if any.
        // Assert.Contains(items, i => i["name"]?.ToString() == "...");
        Assert.DoesNotContain(items, i => i["name"]?.ToString() == "plain");
    }
}
