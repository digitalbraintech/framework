namespace DigitalBrain.Google;

public interface IGmailApiClient
{
    Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct);
    Task<string> ReadMessageAsync(string messageId, CancellationToken ct);
    Task SendMessageAsync(string to, string subject, string body, CancellationToken ct);
}
