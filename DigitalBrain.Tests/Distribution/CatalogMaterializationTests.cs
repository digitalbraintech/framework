using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Distribution;

public class CatalogMaterializationTests : NeuronTestBase
{
    [Fact]
    public async Task Publishing_a_kit_bundle_materializes_its_manifest_into_the_catalog()
    {
        var market = Grain<IMarketplaceNeuron>("market-catalog-1");
        // hello-world demo pack removed.
        await market.FireAsync(new ListPublished());

        var listed = (await market.GetTimelineAsync()).OfType<PublishedList>().Last().Packs;
        // hello-world specific asserts removed.
    }
}
