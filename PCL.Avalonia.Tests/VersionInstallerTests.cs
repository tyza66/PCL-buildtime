using System.Text.Json;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class VersionInstallerTests : IDisposable
{
    private const string AssetHash = "a9993e364706816aba3e25717850c26c9cd0d89d";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _minecraftFolder;
    private readonly VersionCatalogService _catalog = new();
    private readonly FakeDownloadClient _client = new();
    private readonly VersionInstaller _installer;

    public VersionInstallerTests()
    {
        _minecraftFolder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaInstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_minecraftFolder);
        _client.ContentProvider = DefaultContentProvider;
        _installer = new VersionInstaller(_client, _catalog);
    }

    public void Dispose()
    {
        if (Directory.Exists(_minecraftFolder))
        {
            Directory.Delete(_minecraftFolder, recursive: true);
        }
    }

    private static string OsName => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS()
            ? "osx"
            : "linux";

    private static string ArchSuffix => Environment.Is64BitProcess ? "64" : "32";

    private static string CoreLibraryJson => """
        {
          "name": "com.example:core:1.0",
          "downloads": {
            "artifact": {
              "path": "com/example/core/1.0/core-1.0.jar",
              "url": "https://example.com/core.jar"
            }
          }
        }
        """;

    private static string NativeLibraryJson => $$"""
        {
          "name": "org.example:natives:1.0",
          "natives": { "windows": "natives-${arch}", "osx": "natives-${arch}", "linux": "natives-${arch}" },
          "rules": [ { "action": "allow", "os": { "name": "{{OsName}}" } } ],
          "downloads": {
            "classifiers": {
              "natives-{{ArchSuffix}}": {
                "path": "org/example/natives/1.0/natives-1.0-natives-{{ArchSuffix}}.jar",
                "url": "https://example.com/native.jar"
              }
            }
          }
        }
        """;

    private static string ForgeLibraryJson => """
        {
          "name": "net.minecraftforge:forge:1.20.1",
          "downloads": {
            "artifact": {
              "path": "net/minecraftforge/forge/1.20.1/forge-1.20.1.jar",
              "url": "https://example.com/forge.jar"
            }
          }
        }
        """;

    private static string AssetIndexJson => $$"""
        {
          "objects": {
            "icons/icon.png": {
              "hash": "{{AssetHash}}",
              "size": 3
            }
          }
        }
        """;

    private string BuildVersionJson(string id, string? inheritsFrom, string librariesJson, string assetsId = "1.20")
    {
        return JsonSerializer.Serialize(new
        {
            id,
            type = "release",
            releaseTime = "2024-01-01T00:00:00Z",
            mainClass = "net.minecraft.client.main.Main",
            inheritsFrom,
            assets = assetsId,
            assetIndex = new { id = assetsId, url = $"https://example.com/assets/{assetsId}.json" },
            downloads = new
            {
                client = new
                {
                    path = $"{id}/{id}.jar",
                    url = $"https://example.com/client-{id}.jar",
                },
            },
            arguments = new
            {
                game = new[] { "--assetIndex ${assets_index_name}" },
            },
            libraries = JsonDocument.Parse(librariesJson).RootElement,
        }, JsonOptions);
    }

    private string DefaultContentProvider(DownloadRequest request)
    {
        var normalized = request.DestinationPath.Replace('\\', '/');
        if (normalized.Contains("/versions/") && request.Name == "1.20.1.json")
        {
            return BuildVersionJson(
                "1.20.1",
                null,
                $"[{CoreLibraryJson},{NativeLibraryJson}]");
        }

        if (normalized.Contains("/versions/") && request.Name == "1.19.4.json")
        {
            return BuildVersionJson("1.19.4", null, $"[{CoreLibraryJson}]", "1.19");
        }

        if (normalized.Contains("/versions/") && request.Name == "1.20.1-forge.json")
        {
            return BuildVersionJson("1.20.1-forge", "1.20.1", $"[{ForgeLibraryJson}]");
        }

        if (normalized.Contains("/assets/objects/"))
        {
            return "abc";
        }

        if (normalized.Contains("/libraries/"))
        {
            return "lib";
        }

        if (normalized.Contains("/assets/indexes/"))
        {
            return AssetIndexJson;
        }

        return "content";
    }

    [Fact]
    public async Task InstallAsync_DownloadsVersionJarLibrariesAndAssets()
    {
        var entry = new VersionManifestEntry { Id = "1.20.1", Url = "https://example.com/1.20.1.json" };
        var progress = new ListProgress<InstallProgress>();

        var result = await _installer.InstallAsync(
            "1.20.1",
            entry,
            DownloadSource.Bmclapi,
            _minecraftFolder,
            progress);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "versions", "1.20.1", "1.20.1.json")));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "versions", "1.20.1", "1.20.1.jar")));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "libraries", "com", "example", "core", "1.0", "core-1.0.jar")));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "libraries",
            "org",
            "example",
            "natives",
            "1.0",
            $"natives-1.0-natives-{ArchSuffix}.jar")));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "assets", "indexes", "1.20.json")));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "assets", "objects", AssetHash[..2], AssetHash)));

        Assert.Contains(
            _client.Requests,
            request => request.Name == "1.20.1.json"
                && request.Urls[0] == "https://bmclapi2.bangbang93.com/version/1.20.1/json");
        Assert.Contains(
            _client.Requests,
            request => request.Name == "1.20.1.jar"
                && request.Urls[0] == "https://bmclapi2.bangbang93.com/version/1.20.1/jar");
        Assert.Contains(
            _client.Requests,
            request => request.DestinationPath.Contains("core-1.0.jar", StringComparison.Ordinal)
                && request.Urls[0] == "https://bmclapi2.bangbang93.com/libraries/com/example/core/1.0/core-1.0.jar");
        Assert.Contains(
            _client.Requests,
            request => request.DestinationPath.Contains("natives-1.0-natives-", StringComparison.Ordinal)
                && request.Urls[0] == "https://bmclapi2.bangbang93.com/libraries/org/example/natives/1.0/natives-1.0-natives-" + ArchSuffix + ".jar");
        Assert.Contains(
            _client.Requests,
            request => request.Name == "1.20.json"
                && request.Urls.Single() == "https://example.com/assets/1.20.json");
        Assert.Contains(
            _client.Requests,
            request => request.DestinationPath.EndsWith(AssetHash, StringComparison.Ordinal)
                && request.Urls[0] == $"https://bmclapi2.bangbang93.com/assets/{AssetHash[..2]}/{AssetHash}");

        Assert.Contains(progress.Values, value => value.Stage == InstallStage.Libraries && value.TotalItems == 2);
        Assert.Contains(progress.Values, value => value.Stage == InstallStage.Assets && value.TotalItems == 1);
        Assert.Contains(progress.Values, value => value.Stage == InstallStage.Complete);
    }

    [Fact]
    public async Task InstallAsync_FollowsInheritanceChain()
    {
        var entry = new VersionManifestEntry
        {
            Id = "1.20.1-forge",
            Url = "https://example.com/1.20.1-forge.json",
        };

        var result = await _installer.InstallAsync(
            "1.20.1-forge",
            entry,
            DownloadSource.Bmclapi,
            _minecraftFolder);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "versions", "1.20.1-forge", "1.20.1-forge.json")));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "versions", "1.20.1", "1.20.1.json")));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "versions", "1.20.1", "1.20.1.jar")));
        Assert.False(File.Exists(Path.Combine(_minecraftFolder, "versions", "1.20.1-forge", "1.20.1-forge.jar")));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "libraries",
            "net",
            "minecraftforge",
            "forge",
            "1.20.1",
            "forge-1.20.1.jar")));
        Assert.Contains(_client.Requests, request => request.Name == "1.19.4.json"
            || request.Name == "1.20.1.json");
    }

    [Fact]
    public async Task InstallAsync_SkipsExistingFiles()
    {
        var versionFolder = Path.Combine(_minecraftFolder, "versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);
        File.WriteAllText(
            Path.Combine(versionFolder, "1.20.1.json"),
            BuildVersionJson("1.20.1", null, $"[{CoreLibraryJson}]"));
        File.WriteAllText(Path.Combine(versionFolder, "1.20.1.jar"), "jar");

        var libraryPath = Path.Combine(_minecraftFolder, "libraries", "com", "example", "core", "1.0", "core-1.0.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.WriteAllText(libraryPath, "lib");

        var indexPath = Path.Combine(_minecraftFolder, "assets", "indexes", "1.20.json");
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        File.WriteAllText(indexPath, AssetIndexJson);

        var objectPath = Path.Combine(_minecraftFolder, "assets", "objects", AssetHash[..2], AssetHash);
        Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
        File.WriteAllText(objectPath, "abc");
        _client.Requests.Clear();

        var result = await _installer.InstallAsync(
            "1.20.1",
            new VersionManifestEntry { Id = "1.20.1" },
            DownloadSource.Bmclapi,
            _minecraftFolder);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.Empty(_client.Requests);
    }

    [Fact]
    public async Task InstallAsync_CollectsLibraryFailures()
    {
        _client.ExceptionProvider = request =>
            request.DestinationPath.Contains("core-1.0.jar", StringComparison.Ordinal)
                ? new InvalidOperationException("mirror failed")
                : null;

        var result = await _installer.InstallAsync(
            "1.20.1",
            new VersionManifestEntry { Id = "1.20.1" },
            DownloadSource.Bmclapi,
            _minecraftFolder);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("core", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "versions", "1.20.1", "1.20.1.jar")));
        Assert.True(File.Exists(Path.Combine(_minecraftFolder, "assets", "objects", AssetHash[..2], AssetHash)));
    }

    [Fact]
    public async Task InstallAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _installer.InstallAsync(
            "1.20.1",
            new VersionManifestEntry { Id = "1.20.1" },
            DownloadSource.Bmclapi,
            _minecraftFolder,
            cancellationToken: cancellation.Token));
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public List<DownloadRequest> Requests { get; } = [];

        public Func<DownloadRequest, string> ContentProvider { get; set; } = _ => "content";

        public Func<DownloadRequest, Exception?> ExceptionProvider { get; set; } = _ => null;

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            cancellationToken.ThrowIfCancellationRequested();
            var exception = ExceptionProvider(request);
            if (exception is not null)
            {
                return Task.FromException(exception);
            }

            var directory = Path.GetDirectoryName(request.DestinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var content = ContentProvider(request);
            File.WriteAllText(request.DestinationPath, content);
            progress?.Report(new DownloadProgress(content.Length, content.Length));
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
