using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ResourceDownloadServiceTests : IDisposable
{
    private readonly string _folder;

    public ResourceDownloadServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaResource", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public List<DownloadRequest> Requests { get; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            File.WriteAllBytes(request.DestinationPath, [1, 2, 3]);
            return Task.CompletedTask;
        }

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static ResourceFileItem ResourceFile(
        string filename = "resource.zip",
        string url = "https://cdn.example.com/resource.zip")
        => new()
        {
            Source = ResourceSource.Modrinth,
            ProjectId = "abc",
            FileId = "v1",
            DisplayName = "Resource 1.0",
            FileName = filename,
            Url = url,
            Size = 12345,
            Sha1 = "abc123",
        };

    [Theory]
    [InlineData(ResourceType.Mod, "mods")]
    [InlineData(ResourceType.ResourcePack, "resourcepacks")]
    [InlineData(ResourceType.Shader, "shaderpacks")]
    [InlineData(ResourceType.DataPack, "datapacks")]
    public async Task InstallAsync_DownloadsFile_ToTypeFolder(ResourceType type, string folderName)
    {
        var client = new FakeDownloadClient();
        var service = new ResourceDownloadService(client);

        var path = await service.InstallAsync(type, ResourceFile(), _folder);

        var request = Assert.Single(client.Requests);
        Assert.Equal(Path.Combine(_folder, folderName, "resource.zip"), request.DestinationPath);
        Assert.Equal(12345, request.ExpectedSize);
        Assert.Equal("abc123", request.ExpectedSha1);
        Assert.Equal("https://cdn.example.com/resource.zip", request.Urls.Single());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task InstallAsync_SkipsExistingFile()
    {
        var destination = Path.Combine(_folder, "resourcepacks", "resource.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "exists");
        var client = new FakeDownloadClient();
        var service = new ResourceDownloadService(client);

        var path = await service.InstallAsync(ResourceType.ResourcePack, ResourceFile(), _folder);

        Assert.Equal(destination, path);
        Assert.Empty(client.Requests);
        Assert.Equal("exists", File.ReadAllText(path));
    }

    [Fact]
    public async Task InstallAsync_FiltersPathTraversalFilename()
    {
        var client = new FakeDownloadClient();
        var service = new ResourceDownloadService(client);

        var path = await service.InstallAsync(
            ResourceType.Shader,
            ResourceFile(filename: "../../evil.zip"),
            _folder);

        Assert.Equal("evil.zip", Path.GetFileName(path));
        Assert.StartsWith(Path.GetFullPath(_folder), Path.GetFullPath(path));
        Assert.Equal(path, client.Requests.Single().DestinationPath);
    }

    [Fact]
    public async Task InstallAsync_MissingDownloadUrl_Throws()
    {
        var service = new ResourceDownloadService(new FakeDownloadClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync(
                ResourceType.Mod,
                ResourceFile(url: ""),
                _folder));

        Assert.Contains("下载地址不完整", exception.Message);
    }
}
