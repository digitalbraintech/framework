using DigitalBrain.Tests.E2E.Packs;
using Xunit;

namespace DigitalBrain.Tests.E2E;

[Trait("Category", "E2E")]
[Collection(nameof(DigitalBrainE2ECollection))]
public sealed class TravelPlanTripRendersE2ETests(DigitalBrainBrowserFixture fixture)
{
    readonly DigitalBrainBrowserFixture _fx = fixture;

    [SkippableFact]
    public async Task PlanTrip_walks_hops_and_each_renders_full_screen_in_flutter()
    {
        E2EPrerequisites.RequireRenderE2E();

        var driver = new ExperienceFlowDriver(_fx, pack: "travel", experienceId: "plan-trip");
        await driver.PublishAndInstallAsync(TravelPackSource.Read(),
            description: "Travel domain — Plan a trip experience");
        await driver.OpenAsync();

        await driver.TriggerExperienceAsync(("prompt", "plan a trip to Bali next month"));
        await driver.AssertHopRendersAsync("travel-intro");

        await driver.TapAsync("flight.selected", ("flightId", "FL-001"));
        await driver.AssertHopRendersAsync("travel-hotels");

        await driver.TapAsync("hotel.selected", ("hotelId", "H-001"));
        await driver.AssertHopRendersAsync("travel-events");

        await driver.TapAsync("event.selected", ("eventId", "EV-001"));
        await driver.AssertHopRendersAsync("travel-activities");

        await driver.TapAsync("activity.selected", ("activityId", "AC-001"));
        await driver.AssertHopRendersAsync("travel-summary");
    }
}
