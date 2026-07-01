namespace DigitalBrain.Google;

public interface IGoogleDriveApiClient
{
    Task<string[]> ListFilesAsync(string query, CancellationToken ct);
    Task<string> UploadFileAsync(string name, string content, string mimeType, CancellationToken ct);
    Task<string> DownloadFileAsync(string fileId, CancellationToken ct);
    Task DeleteFileAsync(string fileId, CancellationToken ct);
}
