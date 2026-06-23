using DigitalBrain.Protocol;

namespace DigitalBrain.Tests;

public class UiSurfaceContractTests
{
    public static TheoryData<UiSurface, string[]> PlannedSurfaces => new()
    {
        { UiSurfaceSamples.KernelTasks(), new[] { "tasks", UiSurfaceKeys.Actions } },
        { UiSurfaceSamples.ActivityGraph(), new[] { "nodes", "edges", "events" } },
        { UiSurfaceSamples.TaskWindow(), new[] { "taskId", "state", "body", UiSurfaceKeys.Actions } },
        { UiSurfaceSamples.UserInput(), new[] { "prompt", "schema", "submitAction", "cancelAction" } },
        { UiSurfaceSamples.MarketplaceList(), new[] { "packs", "installAction", "updateAction" } },
        { UiSurfaceSamples.Timeline(), new[] { "events", "filters" } }
    };

    [Theory]
    [MemberData(nameof(PlannedSurfaces))]
    public void Planned_Surface_Samples_Carry_Common_Metadata(UiSurface surface, string[] requiredProps)
    {
        Assert.NotEmpty(surface.Kind);
        AssertCommonProp(surface, UiSurfaceKeys.SurfaceId);
        AssertCommonProp(surface, UiSurfaceKeys.Emitter);
        AssertCommonProp(surface, UiSurfaceKeys.Title);
        AssertCommonProp(surface, UiSurfaceKeys.Priority);
        AssertCommonProp(surface, UiSurfaceKeys.RequiresInput);
        AssertCommonProp(surface, UiSurfaceKeys.Layout);

        foreach (var prop in requiredProps)
        {
            Assert.True(surface.Props.ContainsKey(prop), $"{surface.Kind} is missing required prop '{prop}'.");
        }
    }

    [Fact]
    public void Planned_Surface_Kinds_Use_Stable_Kebab_Case_Names()
    {
        Assert.Equal("kernel-tasks", UiSurfaceKinds.KernelTasks);
        Assert.Equal("activity-graph", UiSurfaceKinds.ActivityGraph);
        Assert.Equal("task-window", UiSurfaceKinds.TaskWindow);
        Assert.Equal("user-input", UiSurfaceKinds.UserInput);
        Assert.Equal("marketplace-list", UiSurfaceKinds.MarketplaceList);
        Assert.Equal("timeline", UiSurfaceKinds.Timeline);
    }

    [Fact]
    public void Action_Descriptors_Point_To_Existing_Synapse_Types()
    {
        var kernelActions = Assert.IsAssignableFrom<IEnumerable<IReadOnlyDictionary<string, object?>>>(
            UiSurfaceSamples.KernelTasks().Props[UiSurfaceKeys.Actions]);
        Assert.Contains(kernelActions, a => Equals(a[UiSurfaceKeys.SynapseType], nameof(RunKernelTask)));
        Assert.Contains(kernelActions, a => Equals(a[UiSurfaceKeys.SynapseType], nameof(CancelKernelTask)));

        var userInput = UiSurfaceSamples.UserInput();
        AssertSynapseAction(userInput.Props["submitAction"], nameof(InoRequest));
        AssertSynapseAction(userInput.Props["cancelAction"], nameof(CancelKernelTask));

        var marketplace = UiSurfaceSamples.MarketplaceList();
        AssertSynapseAction(marketplace.Props["installAction"], nameof(InstallFromMarketplace));
        AssertSynapseAction(marketplace.Props["updateAction"], nameof(InstallFromMarketplace));
    }

    private static void AssertCommonProp(UiSurface surface, string key) =>
        Assert.True(surface.Props.ContainsKey(key), $"{surface.Kind} is missing common prop '{key}'.");

    private static void AssertSynapseAction(object? value, string expectedSynapseType)
    {
        var action = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(value);
        Assert.NotEmpty((string)action[UiSurfaceKeys.ActionId]!);
        Assert.NotEmpty((string)action[UiSurfaceKeys.Label]!);
        Assert.Equal(expectedSynapseType, action[UiSurfaceKeys.SynapseType]);
        Assert.True(action.ContainsKey(UiSurfaceKeys.Props));
    }
}
