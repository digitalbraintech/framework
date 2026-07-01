using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;

namespace DigitalBrain.Google;

public sealed class GoogleGmailApiClient(UserCredential credential) : IGmailApiClient
{
    private readonly GmailService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DigitalBrain"
    });

    public async Task<string[]> ListMessagesAsync(string query, int maxResults, CancellationToken ct)
    {
        var request = _service.Users.Messages.List("me");
        request.Q = query;
        request.MaxResults = maxResults;
        var response = await request.ExecuteAsync(ct);
        return response.Messages?.Select(m => m.Id).ToArray() ?? [];
    }

    public async Task<string> ReadMessageAsync(string messageId, CancellationToken ct)
    {
        var message = await _service.Users.Messages.Get("me", messageId).ExecuteAsync(ct);
        return message.Snippet ?? "";
    }

    public async Task SendMessageAsync(string to, string subject, string body, CancellationToken ct)
    {
        var raw = $"To: {to}\r\nSubject: {subject}\r\n\r\n{body}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await _service.Users.Messages.Send(new Message { Raw = encoded }, "me").ExecuteAsync(ct);
    }
}
