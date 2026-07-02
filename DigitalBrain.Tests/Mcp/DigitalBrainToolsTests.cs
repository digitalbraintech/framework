using DigitalBrain.Mcp.Tools;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Mcp;

// The MCP tools are co-hosted in the silo and resolve grains via an in-process IGrainFactory.
// These tests exercise that exact path (TestCluster grain factory) without an HTTP transport.
public class DigitalBrainToolsTests : NeuronTestBase
{
    [Fact]
    public void Ping_Works_Standalone()
        => Assert.Contains("connected", DigitalBrainReadTools.PingDigitalBrain(), System.StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task Publish_Then_List_Through_InProcess_GrainFactory()
    {
        var factory = new TestGrainFactory(this);
        var mutationTools = new DigitalBrainMutationTools(factory);
        var readTools = new DigitalBrainReadTools(factory);

        await mutationTools.PublishToMarketplace("McpPack", "1.0", "public class P {}", "mcp-user", false, 0.15);
        var listing = await readTools.ListMarketplace();

        Assert.Contains("McpPack@1.0", listing);
    }

    private sealed class TestGrainFactory(DigitalBrainToolsTests owner) : IGrainFactory
    {
        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey => owner.Grain<TGrainInterface>(primaryKey);

        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey => owner.Grain<TGrainInterface>(primaryKey);

        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey => throw new NotSupportedException("Only string-keyed grains for MCP tests");

        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();
        public IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey, string? keyExtension = null) => throw new NotSupportedException();

        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId) => throw new NotSupportedException();
        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string? grainClassNamePrefix = null) => throw new NotSupportedException();
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => throw new NotSupportedException();

        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
        public void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver => throw new NotSupportedException();
    }
}
