using DigitalBrain.Core;
using DigitalBrain.Tests.Ui;
using Xunit;

namespace DigitalBrain.Tests.Ui;

/// <summary>
/// Fast, in-memory, type-safe tests using the new ExperienceTestHarness + UiTreeAssertions.
///
/// These are the primary mechanism for fast iteration on UI experiences defined with
/// neurons, synapses, and the ui: / neuron: kit.
///
/// No browser, no full stack. Millisecond feedback.
/// </summary>
public class UiTestingFrameworkExamples
{
    [Fact]
    public void Can_drive_hello_world_experience_and_assert_tree()
    {
        // Using the real compiled behavior from the seeded hello-world example.
        var harness = new ExperienceTestHarness<HelloWorldExperience>();

        var ask = harness.Trigger("ask");
        var tree = (UiWidgetTree)ask.Props["tree"];

        tree.ShouldHaveNodeOfType(DigitalBrain.Core.Ui.TextField);
        tree.ShouldHaveButtonWithLabel("Greet");

        // Example of other kit nodes (for experiences using Select etc.)
        // tree.ShouldHaveSelect("some-field"); 
    }

    [Fact]
    public void Can_use_golden_snapshot_and_matchers()
    {
        var harness = new ExperienceTestHarness<HelloWorldExperience>();

        var ask = harness.Trigger("ask");
        var tree = (UiWidgetTree)ask.Props["tree"];

        // Golden snapshot
        var snapshot = tree.ToGoldenSnapshot();
        Assert.Contains("ui:TextField", snapshot);

        tree.ShouldHaveButtonWithLabel("Greet");
    }

    // Real type from the hello-world seed code (defined in MarketplaceSeeds).
    // In production packs you reference the concrete KitExperience type directly.
    private sealed class HelloWorldExperience : KitExperience
    {
        protected override UiExperience Define() =>
            Experience("hello-world", "Hello World")
                .Hop("ask", s => s
                    .Text("What's your name?")
                    .TextField("name", "Your name")
                    .Button("Greet", "greeting"))
                .Hop("greeting", s => s
                    .Panel(p => p.Text(state =>
                        "Hello " + (state.GetValueOrDefault("name") is { Length: > 0 } n ? n : "World") + "!")));
    }
}