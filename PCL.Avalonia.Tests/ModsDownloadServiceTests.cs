using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ModsDownloadServiceTests : IDisposable
{
    private readonly string _folder;

    public ModsDownloadServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaMods", Guid.NewGuid().ToString("N"));
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
    }

    private static ModrinthProjectVersion Version(
        string filename = "jei-15.2.0.27.jar",
        string url = "https://cdn.modrinth.com/data/jei.jar",
        bool primary = true,
        string? sha1 = "abc123")
        => new()
        {
            Id = "v1",
            ProjectId = "abc",
            Name = "JEI 15.2.0.27",
            Files =
            [
                new ModrinthFile
                {
                    Url = url,
                    Filename = filename,
                    Primary = primary,
                    Size = 12345,
                    Sha1 = sha1,
                },
            ],
        };

    [Fact]
    public async Task InstallAsync_DownloadsPrimaryFile_ToModsFolder()
    {
        var client = new FakeDownloadClient();
        var service = new ModsDownloadService(client);
        var version = Version();

        var path = await service.InstallAsync(version, _folder);

        var request = Assert.Single(client.Requests);
        Assert.Equal(Path.Combine(_folder, "jei-15.2.0.27.jar"), request.DestinationPath);
        Assert.Equal(12345, request.ExpectedSize);
        Assert.Equal("abc123", request.ExpectedSha1);
        Assert.Equal("https://cdn.modrinth.com/data/jei.jar", request.Urls.Single());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task InstallAsync_SkipsExistingFile()
    {
        var destination = Path.Combine(_folder, "jei-15.2.0.27.jar");
        File.WriteAllText(destination, "exists");
        var client = new FakeDownloadClient();
        var service = new ModsDownloadService(client);

        var path = await service.InstallAsync(Version(), _folder);

        Assert.Equal(destination, path);
        Assert.Empty(client.Requests);
        Assert.Equal("exists", File.ReadAllText(path));
    }

    [Fact]
    public async Task InstallAsync_FiltersPathTraversalFilename()
    {
        var client = new FakeDownloadClient();
        var service = new ModsDownloadService(client);

        var path = await service.InstallAsync(Version(filename: "../../evil.jar"), _folder);

        Assert.Equal("evil.jar", Path.GetFileName(path));
        Assert.StartsWith(Path.GetFullPath(_folder), Path.GetFullPath(path));
        Assert.Equal(path, client.Requests.Single().DestinationPath);
    }
}
