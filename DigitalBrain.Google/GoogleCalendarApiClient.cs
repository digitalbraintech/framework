using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace DigitalBrain.Google;

public sealed class GoogleCalendarApiClient(UserCredential credential) : IGoogleCalendarApiClient
{
    private readonly CalendarService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DigitalBrain"
    });

    public async Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct)
    {
        var request = _service.Events.List("primary");
        request.TimeMinDateTimeOffset = DateTimeOffset.Parse(timeMinIso);
        request.TimeMaxDateTimeOffset = DateTimeOffset.Parse(timeMaxIso);
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        var response = await request.ExecuteAsync(ct);
        return response.Items?.Select(e => $"{e.Id}:{e.Summary}").ToArray() ?? [];
    }

    public async Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct)
    {
        var newEvent = new Event
        {
            Summary = summary,
            Description = description,
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse(startIso) },
            End = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse(endIso) }
        };
        var created = await _service.Events.Insert(newEvent, "primary").ExecuteAsync(ct);
        return created.Id;
    }

    public async Task DeleteEventAsync(string eventId, CancellationToken ct) =>
        await _service.Events.Delete("primary", eventId).ExecuteAsync(ct);
}
