using System.IO.Compression;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class ForgelikeLoaderServiceTests : IDisposable
{
    private const string ForgeInstallerProfileJson = """
        {
          "id": "1.20.1-forge-50.1.0",
          "libraries": [
            {
              "name": "only.in.profile:fake:1.0",
              "downloads": {
                "artifact": {
                  "path": "only/in/profile/fake/1.0/fake-1.0.jar",
                  "url": "https://example.com/fake.jar"
                }
              }
            }
          ]
        }
        """;

    private const string NewForgeVersionJson = """
        {
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "inheritsFrom": "1.20.1",
          "libraries": [
            {
              "name": "net.minecraftforge:forge:1.20.1-50.1.0",
              "downloads": {
                "artifact": {
                  "path": "net/minecraftforge/forge/1.20.1-50.1.0/forge-1.20.1-50.1.0.jar",
                  "url": "https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-50.1.0/forge-1.20.1-50.1.0.jar",
                  "sha1": "original",
                  "size": 100
                }
              }
            },
            {
              "name": "org.ow2.asm:asm:9.7.1",
              "downloads": {
                "artifact": {
                  "path": "org/ow2/asm/asm/9.7.1/asm-9.7.1.jar",
                  "url": "https://repo1.maven.org/maven2/org/ow2/asm/asm/9.7.1/asm-9.7.1.jar",
                  "sha1": "asm-hash",
                  "size": 50
                }
              }
            }
          ]
        }
        """;

    private const string InjectorJson = """
        {
          "id": "1.20.1-forge-50.1.0",
          "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
          "inheritsFrom": "1.20.1"
        }
        """;

    private readonly string _minecraftFolder;
    private readonly FakeDownloadClient _client = new();
    private readonly FakeVersionInstaller _versionInstaller = new();
    private readonly FakeInstallRunner _runner = new();
    private readonly ForgelikeLoaderService _service;

    public ForgelikeLoaderServiceTests()
    {
        _minecraftFolder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaForgelike", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_minecraftFolder);
        _client.DownloadContentProvider = _ => "lib";
        _service = new ForgelikeLoaderService(_client, _versionInstaller, _runner);
    }

    public void Dispose()
    {
        if (Directory.Exists(_minecraftFolder))
        {
            Directory.Delete(_minecraftFolder, recursive: true);
        }
    }

    private static string ForgeBmclJson => """
        [
          {
            "version": "50.1.0",
            "branch": null,
            "modified": "2024-01-02T03:04:05",
            "files": [
              { "category": "universal", "format": "zip", "hash": "universal-hash" },
              { "category": "installer", "format": "jar", "hash": "installer-hash" },
              { "category": "client", "format": "zip", "hash": "client-hash" }
            ]
          },
          {
            "version": "11.15.1.2318",
            "branch": null,
            "modified": "2016-01-01T00:00:00",
            "files": [
              { "category": "installer", "format": "jar", "hash": "legacy-hash" }
            ]
          },
          {
            "version": "47.2.0",
            "branch": null,
            "modified": "2024-05-06T00:00:00",
            "files": [
              { "category": "client", "format": "zip", "hash": "client-only-hash" }
            ]
          }
        ]
        """;

    private static string LatestJson => """
        {
          "name": "net.neoforged:neoforge",
          "releases": [
            "20.4.30-beta",
            "20.4.29",
            "26.1.0.0-alpha.1+snapshot-3",
            "0.25w14craftmine.3-beta"
          ]
        }
        """;

    private static string LegacyJson => """
        {
          "name": "net.neoforged:forge",
          "releases": [
            "1.20.1-47.1.99",
            "1.20.1-47.1.82",
            "1.20.1-47.1.79"
          ]
        }
        """;

    [Fact]
    public async Task GetForgeVersionsAsync_ParsesBmclJson_AppliesPriorityAndBranchWorkarounds()
    {
        _client.GetStringContentProvider = _ => ForgeBmclJson;

        var versions = await _service.GetForgeVersionsAsync("1.20.1");

        Assert.Equal(3, versions.Count);
        Assert.Equal("50.1.0", versions[0].VersionName);
        Assert.Equal("installer", versions[0].Category);
        Assert.Equal("installer-hash", versions[0].Hash);
        Assert.Equal("50.1.0", versions[0].FileVersion);
        Assert.Equal("1.20.1", versions[0].GameVersion);
        Assert.Equal("11.15.1.2318-1.8.9", versions[1].FileVersion);
        Assert.Equal("legacy-hash", versions[1].Hash);
        Assert.Equal("client", versions[2].Category);
        var request = Assert.Single(_client.GetStringRequests);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/forge/minecraft/1.20.1",
            request.Urls[0]);
        Assert.Equal(
            "https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.20.1.html",
            request.Urls[1]);
    }

    [Fact]
    public async Task GetForgeVersionsAsync_AppliesOneSevenTenBranchWhenBuildIsAtLeast1300()
    {
        _client.GetStringContentProvider = _ => """
            [
              {
                "version": "10.13.4.1614",
                "branch": null,
                "modified": "2015-01-01T00:00:00",
                "files": [
                  { "category": "universal", "format": "zip", "hash": "u" }
                ]
              },
              {
                "version": "10.13.4.1299",
                "branch": null,
                "modified": "2014-01-01T00:00:00",
                "files": [
                  { "category": "universal", "format": "zip", "hash": "u2" }
                ]
              }
            ]
            """;

        var versions = await _service.GetForgeVersionsAsync("1.7.10");

        Assert.Equal("10.13.4.1614-1.7.10", versions[0].FileVersion);
        Assert.Equal("10.13.4.1299", versions[1].FileVersion);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/forge/minecraft/1.7.10",
            Assert.Single(_client.GetStringRequests).Urls[0]);
    }

    [Fact]
    public async Task GetForgeVersionsAsync_UnderscoresGameVersionInUrls()
    {
        _client.GetStringContentProvider = _ => ForgeBmclJson;

        await _service.GetForgeVersionsAsync("1.7.10-pre4");

        Assert.Equal(
            "https://bmclapi2.bangbang93.com/forge/minecraft/1.7.10_pre4",
            Assert.Single(_client.GetStringRequests).Urls[0]);
    }

    [Fact]
    public async Task GetNeoForgeVersionsAsync_ParsesBothSources_ExcludesBrokenVersionAndSorts()
    {
        _client.GetStringContentProvider = urls => urls[0].EndsWith("/neoforge", StringComparison.Ordinal)
            ? LatestJson
            : LegacyJson;

        var versions = await _service.GetNeoForgeVersionsAsync();

        Assert.Equal(5, versions.Count);
        Assert.Equal("26.1.0.0-alpha.1+snapshot-3", versions[0].VersionName);
        Assert.Equal("26.1-snapshot-3", versions[0].GameVersion);
        Assert.True(versions[0].IsBeta);
        Assert.Equal("20.4.30-beta", versions[1].VersionName);
        Assert.Equal("1.20.4", versions[1].GameVersion);
        Assert.Equal("1.20.1-47.1.99", versions[2].ApiName);
        Assert.Equal("47.1.99", versions[2].VersionName);
        Assert.Equal("1.20.1", versions[2].GameVersion);
        Assert.Equal("1.20.1-47.1.79", versions[3].ApiName);
        Assert.Equal("0.25w14craftmine.3-beta", versions[4].VersionName);
        Assert.Equal("25w14craftmine", versions[4].GameVersion);
        Assert.DoesNotContain(versions, version => version.ApiName == "1.20.1-47.1.82");
        Assert.Equal(2, _client.GetStringRequests.Count);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge",
            _client.GetStringRequests[0].Urls[0]);
        Assert.Equal(
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
            _client.GetStringRequests[0].Urls[1]);
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge",
            _client.GetStringRequests[1].Urls[0]);
        Assert.Equal(
            "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge",
            _client.GetStringRequests[1].Urls[1]);
    }

    [Fact]
    public async Task InstallAsync_NewForge_InstallsBaseAndLibraries_RunsInjectorAndCopiesJson()
    {
        var installerPath = Path.Combine(_minecraftFolder, "installer", "forge-installer.jar");
        BuildInstallerZip(
            installerPath,
            ForgeInstallerProfileJson,
            NewForgeVersionJson);
        var version = new ForgelikeLoaderVersion(
            ForgelikeKind.Forge,
            "50.1.0",
            "1.20.1",
            false,
            new Version(50, 1, 0, 0),
            FileVersion: "50.1.0",
            Category: "installer",
            Hash: "installer-hash");
        var progress = new ListProgress<ForgelikeInstallProgress>();

        var result = await _service.InstallAsync(
            version,
            _minecraftFolder,
            DownloadSource.Bmclapi,
            progress);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.Equal("forge-50.1.0", result.VersionId);
        var installCall = Assert.Single(_versionInstaller.Calls);
        Assert.Equal(("1.20.1", DownloadSource.Bmclapi, _minecraftFolder), installCall);

        var installerRequest = Assert.Single(
            _client.DownloadRequests,
            request => request.DestinationPath.EndsWith("forge_installer.jar", StringComparison.Ordinal));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/1.20.1-50.1.0/forge-1.20.1-50.1.0-installer.jar",
            installerRequest.Urls[0]);
        Assert.Equal(
            "https://files.minecraftforge.net/maven/net/minecraftforge/forge/1.20.1-50.1.0/forge-1.20.1-50.1.0-installer.jar",
            installerRequest.Urls[1]);

        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "libraries",
            "org",
            "ow2",
            "asm",
            "asm",
            "9.7.1",
            "asm-9.7.1.jar")));
        Assert.DoesNotContain(
            _client.DownloadRequests,
            request => request.DestinationPath.Contains("fake-1.0.jar", StringComparison.Ordinal));
        Assert.DoesNotContain(
            _client.DownloadRequests,
            request => request.DestinationPath.EndsWith("forge-1.20.1-50.1.0.jar", StringComparison.Ordinal));

        var runnerCall = Assert.Single(_runner.Calls);
        Assert.Equal((_minecraftFolder, installerRequest.DestinationPath, ForgelikeKind.Forge), runnerCall);
        var versionJsonPath = Path.Combine(_minecraftFolder, "versions", result.VersionId, result.VersionId + ".json");
        Assert.True(File.Exists(versionJsonPath));
        Assert.Contains("\"mainClass\": \"cpw.mods.bootstraplauncher.BootstrapLauncher\"", File.ReadAllText(versionJsonPath));
        Assert.Contains(progress.Values, value => value.Stage == ForgelikeInstallStage.BaseVersion);
        Assert.Contains(progress.Values, value => value.Stage == ForgelikeInstallStage.Installer);
        Assert.Contains(progress.Values, value => value.Stage == ForgelikeInstallStage.Libraries);
        Assert.Contains(progress.Values, value => value.Stage == ForgelikeInstallStage.Injector);
        Assert.Contains(progress.Values, value => value.Stage == ForgelikeInstallStage.Complete);
    }

    [Fact]
    public async Task InstallAsync_NeoForge_UsesMirroredInstallerUrlAndCopiesJson()
    {
        var installerPath = Path.Combine(_minecraftFolder, "installer", "neoforge-installer.jar");
        BuildInstallerZip(
            installerPath,
            """{"id": "neoforge-47.1.99", "libraries": []}""",
            """
            {
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
              "inheritsFrom": "1.20.1",
              "libraries": [
                {
                  "name": "org.ow2.asm:asm:9.7.1",
                  "downloads": {
                    "artifact": {
                      "path": "org/ow2/asm/asm/9.7.1/asm-9.7.1.jar",
                      "url": "https://repo1.maven.org/maven2/org/ow2/asm/asm/9.7.1/asm-9.7.1.jar"
                    }
                  }
                }
              ]
            }
            """);
        _runner.CreatedVersionId = "1.20.1-neoforge-47.1.99";
        var version = new ForgelikeLoaderVersion(
            ForgelikeKind.NeoForge,
            "47.1.99",
            "1.20.1",
            false,
            new Version(19, 47, 1, 99),
            ApiName: "1.20.1-47.1.99");

        var result = await _service.InstallAsync(
            version,
            _minecraftFolder,
            DownloadSource.Bmclapi);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.Equal("neoforge-47.1.99", result.VersionId);
        var installerRequest = Assert.Single(
            _client.DownloadRequests,
            request => request.DestinationPath.EndsWith("forge_installer.jar", StringComparison.Ordinal));
        Assert.Equal(
            "https://bmclapi2.bangbang93.com/maven/net/neoforged/forge/1.20.1-47.1.99/forge-1.20.1-47.1.99-installer.jar",
            installerRequest.Urls[0]);
        Assert.Equal(
            "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.99/forge-1.20.1-47.1.99-installer.jar",
            installerRequest.Urls[1]);
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "versions",
            result.VersionId,
            result.VersionId + ".json")));
        Assert.Single(_versionInstaller.Calls);
    }

    [Fact]
    public async Task InstallAsync_CollectsLibraryFailuresAndStillCompletes()
    {
        var installerPath = Path.Combine(_minecraftFolder, "installer", "forge-installer.jar");
        BuildInstallerZip(
            installerPath,
            """{"id": "1.20.1-forge-50.1.0", "libraries": []}""",
            """
            {
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
              "inheritsFrom": "1.20.1",
              "libraries": [
                {
                  "name": "com.google.guava:guava:33.0.0-jre",
                  "downloads": {
                    "artifact": {
                      "path": "com/google/guava/guava/33.0.0-jre/guava-33.0.0-jre.jar",
                      "url": "https://repo1.maven.org/maven2/com/google/guava/guava/33.0.0-jre/guava-33.0.0-jre.jar"
                    }
                  }
                },
                {
                  "name": "org.ow2.asm:asm:9.7.1",
                  "downloads": {
                    "artifact": {
                      "path": "org/ow2/asm/asm/9.7.1/asm-9.7.1.jar",
                      "url": "https://repo1.maven.org/maven2/org/ow2/asm/asm/9.7.1/asm-9.7.1.jar"
                    }
                  }
                }
              ]
            }
            """);
        _client.DownloadExceptionProvider = request =>
            request.DestinationPath.Contains("guava-33.0.0-jre.jar", StringComparison.Ordinal)
                ? new InvalidOperationException("mirror failed")
                : null;
        var version = new ForgelikeLoaderVersion(
            ForgelikeKind.Forge,
            "50.1.0",
            "1.20.1",
            false,
            new Version(50, 1, 0, 0),
            FileVersion: "50.1.0",
            Category: "installer");

        var result = await _service.InstallAsync(version, _minecraftFolder, DownloadSource.Bmclapi);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("guava-33.0.0-jre.jar", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "libraries",
            "org",
            "ow2",
            "asm",
            "asm",
            "9.7.1",
            "asm-9.7.1.jar")));
        Assert.True(File.Exists(Path.Combine(
            _minecraftFolder,
            "versions",
            result.VersionId,
            result.VersionId + ".json")));
        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task InstallAsync_RejectsUnsafeLibraryPath()
    {
        var installerPath = Path.Combine(_minecraftFolder, "installer", "forge-installer.jar");
        BuildInstallerZip(
            installerPath,
            """{"id": "1.20.1-forge-50.1.0", "libraries": []}""",
            """
            {
              "mainClass": "cpw.mods.bootstraplauncher.BootstrapLauncher",
              "inheritsFrom": "1.20.1",
              "libraries": [
                {
                  "name": "com.example:evil:1.0",
                  "downloads": {
                    "artifact": {
                      "path": "../evil.jar",
                      "url": "https://example.com/evil.jar"
                    }
                  }
                }
              ]
            }
            """);
        var version = new ForgelikeLoaderVersion(
            ForgelikeKind.Forge,
            "50.1.0",
            "1.20.1",
            false,
            new Version(50, 1, 0, 0),
            FileVersion: "50.1.0",
            Category: "installer");

        var result = await _service.InstallAsync(version, _minecraftFolder, DownloadSource.Bmclapi);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("支持库路径无效", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_minecraftFolder, "..", "evil.jar")));
    }

    [Fact]
    public async Task InstallAsync_LegacyForge_WithoutInstall_ExtractsMavenAndRewritesJson()
    {
        var installerPath = Path.Combine(_minecraftFolder, "installer", "forge-installer.jar");
        BuildInstallerZip(
            installerPath,
            """
            {
              "json": "path/to/forge.json",
              "libraries": []
            }
            """,
            versionJson: null,
            extraEntries: new Dictionary<string, string>
            {
                ["path/to/forge.json"] = """{"id": "1.12.2", "inheritsFrom": "1.12.2", "mainClass": "net.minecraft.launchwrapper.Launch"}""",
                ["maven/org/example/lib/1.0/lib-1.0.jar"] = "lib-content",
                ["README.txt"] = "not a library",
            });
        var version = new ForgelikeLoaderVersion(
            ForgelikeKind.Forge,
            "14.23.5.2847",
            "1.12.2",
            false,
            new Version(14, 23, 5, 2847),
            FileVersion: "14.23.5.2847",
            Category: "installer");

        var result = await _service.InstallAsync(version, _minecraftFolder, DownloadSource.Bmclapi);

        Assert.True(result.Success, string.Join("；", result.Errors));
        var versionJsonPath = Path.Combine(_minecraftFolder, "versions", result.VersionId, result.VersionId + ".json");
        Assert.True(File.Exists(versionJsonPath));
        Assert.Contains("\"id\": \"forge-14.23.5.2847\"", File.ReadAllText(versionJsonPath));
        Assert.Equal(
            "lib-content",
            File.ReadAllText(Path.Combine(
                _minecraftFolder,
                "libraries",
                "org",
                "example",
                "lib",
                "1.0",
                "lib-1.0.jar")));
        Assert.False(File.Exists(Path.Combine(_minecraftFolder, "libraries", "README.txt")));
        Assert.Empty(_runner.Calls);
        Assert.Empty(_versionInstaller.Calls);
    }

    [Fact]
    public async Task InstallAsync_LegacyForge_WithInstall_WritesUniversalJarAndVersionInfo()
    {
        var installerPath = Path.Combine(_minecraftFolder, "installer", "forge-installer.jar");
        BuildInstallerZip(
            installerPath,
            """
            {
              "install": {
                "path": "net/minecraftforge/forge/9.11.1.1345/forge-9.11.1.1345-universal.jar",
                "filePath": "forge-9.11.1.1345-universal.jar",
                "profileName": "Forge 9.11.1.1345",
                "version": "9.11.1.1345"
              },
              "versionInfo": {
                "id": "1.6.4-forge9.11.1.1345",
                "mainClass": "net.minecraft.launchwrapper.Launch",
                "minecraftArguments": "demo"
              },
              "libraries": []
            }
            """,
            versionJson: null,
            extraEntries: new Dictionary<string, string>
            {
                ["forge-9.11.1.1345-universal.jar"] = "universal-content",
            });
        var version = new ForgelikeLoaderVersion(
            ForgelikeKind.Forge,
            "9.11.1.1345",
            "1.6.4",
            false,
            new Version(9, 11, 1, 1345),
            FileVersion: "9.11.1.1345",
            Category: "installer");

        var result = await _service.InstallAsync(version, _minecraftFolder, DownloadSource.Bmclapi);

        Assert.True(result.Success, string.Join("；", result.Errors));
        Assert.Equal(
            "universal-content",
            File.ReadAllText(Path.Combine(
                _minecraftFolder,
                "libraries",
                "net",
                "minecraftforge",
                "forge",
                "9.11.1.1345",
                "forge-9.11.1.1345-universal.jar")));
        var versionJsonPath = Path.Combine(_minecraftFolder, "versions", result.VersionId, result.VersionId + ".json");
        var content = File.ReadAllText(versionJsonPath);
        Assert.Contains("\"id\": \"forge-9.11.1.1345\"", content);
        Assert.Contains("\"inheritsFrom\": \"1.6.4\"", content);
        Assert.Empty(_runner.Calls);
    }

    private static void BuildInstallerZip(
        string path,
        string installProfileJson,
        string? versionJson,
        IReadOnlyDictionary<string, string>? extraEntries = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        WriteEntry(archive, "install_profile.json", installProfileJson);
        if (versionJson is not null)
        {
            WriteEntry(archive, "version.json", versionJson);
        }

        if (extraEntries is not null)
        {
            foreach (var (entryPath, content) in extraEntries)
            {
                WriteEntry(archive, entryPath, content);
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
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

    private sealed class FakeInstallRunner : IForgelikeInstallRunner
    {
        public List<(string MinecraftFolder, string InstallerPath, ForgelikeKind Kind)> Calls { get; } = [];

        public string? CreatedVersionId { get; set; } = "1.20.1-forge-50.1.0";

        public string JsonContent { get; set; } = InjectorJson;

        public Task RunAsync(
            string minecraftFolder,
            string installerPath,
            ForgelikeKind kind,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((minecraftFolder, installerPath, kind));
            if (CreatedVersionId is not null)
            {
                var folder = Path.Combine(minecraftFolder, "versions", CreatedVersionId);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, CreatedVersionId + ".json"), JsonContent);
            }

            return Task.CompletedTask;
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
