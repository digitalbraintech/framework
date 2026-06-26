namespace DigitalBrain.Tests.E2E;

// Reusable pack C# source strings for E2E tests. Each method returns compilable
// IPackBehavior source that can be passed directly to PublishPackAsync.
public static class TestPacks
{
    // Returns pack source whose Handle emits a UiSurface that carries the given
    // surfaceId in its Props. The DefaultSource RFW template is omitted here
    // because UiSurfaceRfwBridge.FromUiSurface adds one automatically when the
    // surface carries no "source" prop — and that default template satisfies
    // _isRenderableSurface in the Flutter host. The panel.id that the Flutter
    // host assigns equals the RfwCard.CorrelationId, which flows from the
    // SurfaceDemoRequested trigger's correlationId (preserved through the
    // observability-surface path via ObservabilityNeuron → HomeFeedBus).
    // The trigger correlationId is set to surfaceId so panel.id == surfaceId
    // and flt-semantics-identifier == surfaceId.
    public static string RenderableSurfacePack(string surfaceId) => $$"""
        public sealed class RenderableSurfacePack : DigitalBrain.Core.IPackBehavior
        {
            public string Respond(string input) => "surface:" + input;

            public DigitalBrain.Core.PackManifest GetManifest() =>
                new(new[] { new DigitalBrain.Core.SynapseType(nameof(DigitalBrain.Core.DemoMessageSynapse)) });

            public bool CanHandle(DigitalBrain.Core.Synapse synapse) =>
                synapse is DigitalBrain.Core.DemoMessageSynapse;

            public System.Collections.Generic.IReadOnlyList<DigitalBrain.Core.Synapse> Handle(DigitalBrain.Core.Synapse synapse)
            {
                var props = new System.Collections.Generic.Dictionary<string, object?>
                {
                    [DigitalBrain.Core.UiSurfaceKeys.SurfaceId] = "{{surfaceId}}",
                    [DigitalBrain.Core.UiSurfaceKeys.Emitter] = "RenderableSurfacePack",
                    [DigitalBrain.Core.UiSurfaceKeys.Title] = "E2E pack surface",
                    [DigitalBrain.Core.UiSurfaceKeys.Priority] = 100,
                    [DigitalBrain.Core.UiSurfaceKeys.RequiresInput] = false,
                    [DigitalBrain.Core.UiSurfaceKeys.Layout] = DigitalBrain.Core.UiSurfaceLayouts.Panel,
                    ["body"] = "E2E surface emitted by RenderableSurfacePack",
                    ["status"] = "live",
                    ["tone"] = "teal"
                };
                return new DigitalBrain.Core.Synapse[]
                {
                    new DigitalBrain.Core.UiSurface(DigitalBrain.Core.UiSurfaceKinds.TaskWindow, props)
                };
            }
        }
        """;
}
