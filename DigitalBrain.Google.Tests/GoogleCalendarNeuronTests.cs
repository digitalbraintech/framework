using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleCalendarNeuronTests : NeuronTestBase
{
    private readonly FakeGoogleCalendarApiClient _fake = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IGoogleCalendarApiClient>(_fake));

    [Fact]
    public async Task CreateEventAsync_Then_ListEventsAsync_Returns_The_Created_Event()
    {
        var calendar = Grain<IGoogleCalendarNeuron>("calendar-test");
        var eventId = await calendar.CreateEventAsync(
            "Standup", "2026-07-02T09:00:00Z", "2026-07-02T09:30:00Z", "Daily standup");
        var events = await calendar.ListEventsAsync("2026-07-01T00:00:00Z", "2026-07-03T00:00:00Z");
        Assert.Contains(events, e => e.StartsWith(eventId));
    }

    [Fact]
    public async Task DeleteEventAsync_Removes_Event_From_Fake()
    {
        var calendar = Grain<IGoogleCalendarNeuron>("calendar-delete-test");
        var eventId = await calendar.CreateEventAsync(
            "Cancel me", "2026-07-02T09:00:00Z", "2026-07-02T09:30:00Z", "");
        await calendar.DeleteEventAsync(eventId);
        var events = await calendar.ListEventsAsync("2026-07-01T00:00:00Z", "2026-07-03T00:00:00Z");
        Assert.DoesNotContain(events, e => e.StartsWith(eventId));
    }
}
