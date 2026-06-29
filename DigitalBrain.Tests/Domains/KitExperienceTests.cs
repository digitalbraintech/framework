using System.Collections.Generic;
using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests.Domains;

public class KitExperienceTests
{
    [Fact]
    public void ForExperienceHopTree_carries_tree_and_markers()
    {
        var tree = new UiWidgetTree(DigitalBrain.Core.Ui.Screen, new Dictionary<string, object?>());
        var surface = UiSurface.ForExperienceHopTree("hello-world", "hello-world", "ask", tree, title: "Hello World");

        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Same(tree, surface.Props["tree"]);
        Assert.Equal("hello-world/hello-world", surface.Props["activeExperience"]);
        Assert.Equal("hello-world", surface.Props["experienceId"]);
        Assert.Equal("ask", surface.Props[UiSurfaceKeys.SurfaceId]);
    }
}
