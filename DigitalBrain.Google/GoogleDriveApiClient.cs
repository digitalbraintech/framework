using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace DigitalBrain.Google;

public sealed class GoogleDriveApiClient(UserCredential credential) : IGoogleDriveApiClient
{
    private readonly DriveService _service = new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "DigitalBrain"
    });

    public async Task<string[]> ListFilesAsync(string query, CancellationToken ct)
    {
        var request = _service.Files.List();
        request.Q = query;
        request.Fields = "files(id, name)";
        var response = await request.ExecuteAsync(ct);
        return response.Files?.Select(f => $"{f.Id}:{f.Name}").ToArray() ?? [];
    }

    public async Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct)
    {
        var metadata = new DriveFile { Name = name };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var request = _service.Files.Create(metadata, stream, mimeType);
        await request.UploadAsync(ct);
        return request.ResponseBody?.Id ?? throw new InvalidOperationException("Drive upload returned no file id.");
    }

    public async Task<string> DownloadFileAsync(string fileId, CancellationToken ct)
    {
        using var stream = new MemoryStream();
        await _service.Files.Get(fileId).DownloadAsync(stream, ct);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task DeleteFileAsync(string fileId, CancellationToken ct) =>
        await _service.Files.Delete(fileId).ExecuteAsync(ct);
}
