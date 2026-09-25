using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class CurseForgeModpackServiceTests : IDisposable
{
    private readonly string _folder;

    public CurseForgeModpackServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaPacks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private sealed class FakeApi : ICurseForgeApi
    {
        public List<CurseForgeProject> Projects { get; set; } = [];

        public List<CurseForgeModFile> Files { get; set; } = [];

        public List<int> SearchClassIds { get; } = [];

        public Task<CurseForgeSearchPage> SearchProjectsAsync(
            string query,
            int classId = 6,
            string gameVersion = "",
            string loader = "",
            string categoryId = "",
            int index = 0,
            int pageSize = 40,
            CancellationToken cancellationToken = default)
        {
            SearchClassIds.Add(classId);
            return Task.FromResult(new CurseForgeSearchPage(Projects, Projects.Count));
        }

        public Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
            int projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CurseForgeModFile>> GetModpackFilesAsync(
            int projectId,
            string gameVersion,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(999, projectId);
            Assert.Equal("1.20.1", gameVersion);
            return Task.FromResult<IReadOnlyList<CurseForgeModFile>>(Files);
        }

        public Task<CurseForgeModFile?> GetFileAsync(
            int projectId,
            int fileId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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

    private static CurseForgeProject Project() => new()
    {
        Id = 999,
        Slug = "example-pack",
        Name = "Example Pack",
        Summary = "示例整合包",
        Authors = [new CurseForgeAuthor { Name = "author" }],
        DownloadCount = 12345,
    };

    private static CurseForgeModFile PackFile(
        string filename = "example-pack-1.0.0-modpack.zip",
        string url = "https://edge.forgecdn.net/files/pack.zip")
        => new()
        {
            Id = 101,
            DisplayName = "Example Pack 1.0.0",
            FileName = filename,
            DownloadUrl = url,
            FileLength = 999,
            FileHashes = [new CurseForgeFileHash { Algo = 1, Value = "abc123" }],
            FileDate = DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
        };

    [Fact]
    public async Task SearchAsync_UsesModpackClassId()
    {
        var api = new FakeApi { Projects = [Project()] };
        var service = new CurseForgeModpackService(api, new FakeDownloadClient());

        var results = await service.SearchAsync("pack");

        Assert.Single(results);
        Assert.Equal(4471, Assert.Single(api.SearchClassIds));
    }

    [Fact]
    public async Task InstallAsync_DownloadsModpackZip_ToDownloadsFolder()
    {
        var api = new FakeApi { Files = [PackFile()] };
        var client = new FakeDownloadClient();
        var service = new CurseForgeModpackService(api, client);

        var path = await service.InstallAsync(Project(), "1.20.1", _folder);

        var request = Assert.Single(client.Requests);
        Assert.Equal(Path.Combine(_folder, "downloads", "example-pack-1.0.0-modpack.zip"), request.DestinationPath);
        Assert.Equal(999, request.ExpectedSize);
        Assert.Equal("abc123", request.ExpectedSha1);
        Assert.Equal("https://edge.forgecdn.net/files/pack.zip", request.Urls.Single());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task InstallAsync_SkipsExistingFile()
    {
        var destination = Path.Combine(_folder, "downloads", "example-pack-1.0.0-modpack.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "exists");
        var api = new FakeApi { Files = [PackFile()] };
        var client = new FakeDownloadClient();
        var service = new CurseForgeModpackService(api, client);

        var path = await service.InstallAsync(Project(), "1.20.1", _folder);

        Assert.Equal(destination, path);
        Assert.Empty(client.Requests);
        Assert.Equal("exists", File.ReadAllText(path));
    }

    [Fact]
    public async Task InstallAsync_FiltersPathTraversalFilename()
    {
        var api = new FakeApi { Files = [PackFile(filename: "../../evil.zip")] };
        var client = new FakeDownloadClient();
        var service = new CurseForgeModpackService(api, client);

        var path = await service.InstallAsync(Project(), "1.20.1", _folder);

        Assert.Equal("evil.zip", Path.GetFileName(path));
        Assert.StartsWith(Path.GetFullPath(_folder), Path.GetFullPath(path));
        Assert.Equal(path, client.Requests.Single().DestinationPath);
    }
}
