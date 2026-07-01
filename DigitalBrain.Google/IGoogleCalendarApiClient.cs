namespace DigitalBrain.Google;

public interface IGoogleCalendarApiClient
{
    Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct);
    Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct);
    Task DeleteEventAsync(string eventId, CancellationToken ct);
}
