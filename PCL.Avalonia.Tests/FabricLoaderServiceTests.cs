using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class FabricLoaderServiceTests : IDisposable
{
    private const string ProfileJson = """
        {
          "id": "fabric-loader-0.16.9-1.20.1",
          "inheritsFrom": "1.20.1",
          "type": "release",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [
            {
              "name": "net.fabricmc:fabric-loader:0.16.9",
              "url": "https://maven.fabricmc.net/",
              "sha1": "abc",
              "size": 123
            },
            {
              "name": "org.ow2.asm:asm:9.7.1",
              "url": "https://maven.fabricmc.net/",
              "sha1": "def",
              "size": 456
            }
          ]
        }
        """;

    private readonly string _minecraftFolder;
    private readonly FakeDownloadClient _client = new();
    private readonly FakeVersionInstaller _versionInstaller = new();
    private readonly FabricLoaderService _service;

    public FabricLoaderServiceTests()
    {
        _minecraftFolder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaFabric", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_minecraftFolder);
        _client.GetStringContentProvider = urls => urls.Any(url => url.Contains("/profile/json", StringComparison.Ordinal))
            ? ProfileJson
            : MetaJson;
        _client.DownloadContentProvider = _ => "lib";
        _service = new FabricLoaderService(_client, _versionInstaller);
    }

    public void Dispose()
    {
        if (Directory.Exists(_minecraftFolder))
        {
            Directory.Delete(_minecraftFolder, recursive: true);
        }
    }

    private static string MetaJson => """
        [
          {
            "loader": { "version": "0.15.0", "stable": false },
            "intermediary": { "version": "1.20.1", "stable": true },
            "launcherMeta": { "min_java_version": 17 }
          },
          {
            "loader": { "version": "0.16.9", "stable": true },
            "intermediary": { "version": "1.20.1", "stable": true },
            "launcherMeta": { "min_java_version": 17 }
          }
        ]
        """;

    [Fact]
    public async Task GetVersionsAsync_ParsesMetaAndSortsStableFirst()
    {
        var versions = await _service.GetVersionsAsync("1.20.1");

        Assert.Equal(2, versions.Count);
        Assert.Equal("0.16.9", versions[0].Version);
        Assert.True(versions[0].IsStable);
        Assert.Equal("0.15.0", versions[1].Version);
        Assert.False(versions[1].IsStable);
        Assert.Equal("1.20.1", versions[0].MinecraftVersion);
        Assert.Equal(17, versions[0].MinJavaVersion);
        var request = Assert.Single(_client.GetStringRequests);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/1.20.1",
            request.Urls[0]);
        Assert.Equal(
            "https://meta.fabricmc.net/v2/versions/loader/1.20.1",
            request.Urls[1]);
    }

    [Fact]
    public async Task InstallAsync_InstallsBaseProfileAndDownloadsLibraries()
    {
        var progress = new ListProgress<FabricInstallProgress>();

        var result = await _service.InstallAsync(
            "1.20.1",
            "0.16.9",
            _minecraftFolder,
            DownloadSource.Bmclapi,
            progress);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.Equal("fabric-loader-0.16.9-1.20.1", result.VersionId);
        var installCall = Assert.Single(_versionInstaller.Calls);
        Assert.Equal(("1.20.1", DownloadSource.Bmclapi, _minecraftFolder), installCall);
        var profilePath = Path.Combine(_minecraftFolder, "versions", result.VersionId, result.VersionId + ".json");
        Assert.True(File.Exists(profilePath));
        Assert.Contains("\"id\": \"fabric-loader-0.16.9-1.20.1\"", File.ReadAllText(profilePath));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "libraries",
            "net",
            "fabricmc",
            "fabric-loader",
            "0.16.9",
            "fabric-loader-0.16.9.jar")));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "libraries",
            "org",
            "ow2",
            "asm",
            "asm",
            "9.7.1",
            "asm-9.7.1.jar")));

        var fabricRequest = Assert.Single(_client.DownloadRequests,
            request => request.DestinationPath.Contains("fabric-loader-0.16.9.jar", StringComparison.Ordinal));
        Assert.Equal("abc", fabricRequest.ExpectedSha1);
        Assert.Equal(123, fabricRequest.ExpectedSize);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/net/fabricmc/fabric-loader/0.16.9/fabric-loader-0.16.9.jar",
            fabricRequest.Urls[0]);
        Assert.Equal(
            "https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.16.9/fabric-loader-0.16.9.jar",
            fabricRequest.Urls[1]);

        Assert.Contains(progress.Values, value => value.Stage == FabricInstallStage.BaseVersion);
        Assert.Contains(progress.Values, value => value.Stage == FabricInstallStage.ProfileJson);
        Assert.Contains(progress.Values, value => value.Stage == FabricInstallStage.Libraries && value.TotalItems == 2);
        Assert.Contains(progress.Values, value => value.Stage == FabricInstallStage.Complete);
    }

    [Fact]
    public async Task InstallAsync_SkipsBaseVersionWhenAlreadyInstalled()
    {
        var versionFolder = Path.Combine(_minecraftFolder, "versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);
        File.WriteAllText(Path.Combine(versionFolder, "1.20.1.json"), "{}");

        var result = await _service.InstallAsync(
            "1.20.1",
            "0.16.9",
            _minecraftFolder,
            DownloadSource.Bmclapi);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.Empty(_versionInstaller.Calls);
    }

    [Fact]
    public async Task InstallAsync_CollectsLibraryFailures()
    {
        _client.DownloadExceptionProvider = request =>
            request.DestinationPath.Contains("asm-9.7.1.jar", StringComparison.Ordinal)
                ? new InvalidOperationException("mirror failed")
                : null;

        var result = await _service.InstallAsync(
            "1.20.1",
            "0.16.9",
            _minecraftFolder,
            DownloadSource.Bmclapi);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("asm-9.7.1.jar", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "versions",
            result.VersionId,
            result.VersionId + ".json")));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "libraries",
            "net",
            "fabricmc",
            "fabric-loader",
            "0.16.9",
            "fabric-loader-0.16.9.jar")));
    }

    [Fact]
    public async Task InstallAsync_RejectsUnsafeLibraryNames()
    {
        _client.GetStringContentProvider = _ => """
            {
              "id": "fabric-loader-0.16.9-1.20.1",
              "inheritsFrom": "1.20.1",
              "type": "release",
              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
              "libraries": [
                {
                  "name": "com.example:../../evil:1.0",
                  "url": "https://maven.fabricmc.net/"
                }
              ]
            }
            """;

        var result = await _service.InstallAsync(
            "1.20.1",
            "0.16.9",
            _minecraftFolder,
            DownloadSource.Bmclapi);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("支持库路径无效", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_minecraftFolder, "..", "evil")));
    }

    private sealed class FakeVersionInstaller : IVersionInstaller
    {
        public List<(string VersionId, DownloadSource Source, string Folder)> Calls { get; } = [];

        public Task<VersionInstallResult> InstallAsync(
            string versionId,
            VersionManifestEntry? entry,
            DownloadSource source,
            string minecraftFolder,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((versionId, source, minecraftFolder));
            return Task.FromResult(new VersionInstallResult(versionId, []));
        }
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public List<DownloadRequest> GetStringRequests { get; } = [];

        public List<DownloadRequest> DownloadRequests { get; } = [];

        public Func<IReadOnlyList<string>, string> GetStringContentProvider { get; set; } = _ => "{}";

        public Func<DownloadRequest, string> DownloadContentProvider { get; set; } = _ => "content";

        public Func<DownloadRequest, Exception?> DownloadExceptionProvider { get; set; } = _ => null;

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadRequests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            var exception = DownloadExceptionProvider(request);
            if (exception is not null)
            {
                return Task.FromException(exception);
            }

            var directory = Path.GetDirectoryName(request.DestinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = DownloadContentProvider(request);
            File.WriteAllText(request.DestinationPath, content);
            progress?.Report(new DownloadProgress(content.Length, content.Length));
            return Task.CompletedTask;
        }

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            GetStringRequests.Add(new DownloadRequest(urls, Path.Combine(Path.GetTempPath(), "meta.json")));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(GetStringContentProvider(urls));
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ListProgress<T> : IProgress<T>
    {
        private readonly object _lock = new();
        private readonly List<T> _values = [];

        public IReadOnlyList<T> Values
        {
            get
            {
                lock (_lock)
                {
                    return _values.ToArray();
                }
            }
        }

        public void Report(T value)
        {
            lock (_lock)
            {
                _values.Add(value);
            }
        }
    }
}
