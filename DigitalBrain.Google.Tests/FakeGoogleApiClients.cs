namespace DigitalBrain.Google.Tests;

public sealed class FakeGmailApiClient : IGmailApiClient
{
    public List<(string To, string Subject, string Body)> SentMessages { get; } = [];

    public Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct) =>
        Task.FromResult(new[] { "fake-message-1", "fake-message-2" });

    public Task<string> ReadMessageAsync(string messageId, CancellationToken ct) =>
        Task.FromResult($"fake body for {messageId}");

    public Task SendMessageAsync(string to, string subject, string body, CancellationToken ct)
    {
        SentMessages.Add((to, subject, body));
        return Task.CompletedTask;
    }
}

public sealed class FakeGoogleDriveApiClient : IGoogleDriveApiClient
{
    private readonly Dictionary<string, string> _files = new();

    public Task<string[]> ListFilesAsync(string query, CancellationToken ct) =>
        Task.FromResult(_files.Keys.ToArray());

    public Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct)
    {
        var id = "fake-" + name;
        _files[id] = content;
        return Task.FromResult(id);
    }

    public Task<string> DownloadFileAsync(string fileId, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(fileId, out var content) ? content : "");

    public Task DeleteFileAsync(string fileId, CancellationToken ct)
    {
        _files.Remove(fileId);
        return Task.CompletedTask;
    }
}

public sealed class FakeGoogleCalendarApiClient : IGoogleCalendarApiClient
{
    private readonly List<string> _events = [];

    public Task<string[]> ListEventsAsync(string timeMinIso, string timeMaxIso, CancellationToken ct) =>
        Task.FromResult(_events.ToArray());

    public Task<string> CreateEventAsync(string summary, string startIso, string endIso, string description, CancellationToken ct)
    {
        var id = "fake-event-" + _events.Count;
        _events.Add($"{id}:{summary}");
        return Task.FromResult(id);
    }

    public Task DeleteEventAsync(string eventId, CancellationToken ct)
    {
        _events.RemoveAll(e => e.StartsWith(eventId));
        return Task.CompletedTask;
    }
}
