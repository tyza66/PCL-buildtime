using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class CurseForgeDownloadServiceTests : IDisposable
{
    private readonly string _folder;

    public CurseForgeDownloadServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaCurseForge", Guid.NewGuid().ToString("N"));
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

    private static CurseForgeModFile CurseFile(
        string filename = "jei-15.2.0.27.jar",
        string url = "https://edge.forgecdn.net/files/jei.jar")
        => new()
        {
            Id = 456,
            DisplayName = "JEI 15.2.0.27",
            FileName = filename,
            DownloadUrl = url,
            FileLength = 12345,
            FileHashes = [new CurseForgeFileHash { Algo = 1, Value = "abc123" }],
        };

    [Fact]
    public async Task InstallAsync_DownloadsFile_ToModsFolder()
    {
        var client = new FakeDownloadClient();
        var service = new CurseForgeDownloadService(client);

        var path = await service.InstallAsync(CurseFile(), _folder);

        var request = Assert.Single(client.Requests);
        Assert.Equal(Path.Combine(_folder, "jei-15.2.0.27.jar"), request.DestinationPath);
        Assert.Equal(12345, request.ExpectedSize);
        Assert.Equal("abc123", request.ExpectedSha1);
        Assert.Equal("https://edge.forgecdn.net/files/jei.jar", request.Urls.Single());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task InstallAsync_SkipsExistingFile()
    {
        var destination = Path.Combine(_folder, "jei-15.2.0.27.jar");
        File.WriteAllText(destination, "exists");
        var client = new FakeDownloadClient();
        var service = new CurseForgeDownloadService(client);

        var path = await service.InstallAsync(CurseFile(), _folder);

        Assert.Equal(destination, path);
        Assert.Empty(client.Requests);
        Assert.Equal("exists", File.ReadAllText(path));
    }

    [Fact]
    public async Task InstallAsync_FiltersPathTraversalFilename()
    {
        var client = new FakeDownloadClient();
        var service = new CurseForgeDownloadService(client);

        var path = await service.InstallAsync(CurseFile(filename: "../../evil.jar"), _folder);

        Assert.Equal("evil.jar", Path.GetFileName(path));
        Assert.StartsWith(Path.GetFullPath(_folder), Path.GetFullPath(path));
        Assert.Equal(path, client.Requests.Single().DestinationPath);
    }
}
