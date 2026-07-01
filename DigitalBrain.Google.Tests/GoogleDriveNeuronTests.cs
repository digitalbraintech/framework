using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleDriveNeuronTests : NeuronTestBase
{
    private readonly FakeGoogleDriveApiClient _fake = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IGoogleDriveApiClient>(_fake));

    [Fact]
    public async Task UploadFileAsync_Then_DownloadFileAsync_Round_Trips_Content()
    {
        var drive = Grain<IGoogleDriveNeuron>("drive-test");
        var fileId = await drive.UploadFileAsync("notes.txt", "hello drive", "text/plain");
        var content = await drive.DownloadFileAsync(fileId);
        Assert.Equal("hello drive", content);
    }

    [Fact]
    public async Task DeleteFileAsync_Removes_File_From_Fake()
    {
        var drive = Grain<IGoogleDriveNeuron>("drive-delete-test");
        var fileId = await drive.UploadFileAsync("temp.txt", "temp", "text/plain");
        await drive.DeleteFileAsync(fileId);
        var files = await drive.ListFilesAsync("");
        Assert.DoesNotContain(fileId, files);
    }
}
