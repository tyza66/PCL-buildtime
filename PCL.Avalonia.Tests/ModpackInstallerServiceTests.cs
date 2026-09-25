using System.IO.Compression;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ModpackInstallerServiceTests : IDisposable
{
    private readonly string _folder;
    private readonly string _zipPath;

    public ModpackInstallerServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaInstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _zipPath = Path.Combine(_folder, "pack.zip");
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
        public Dictionary<(int ProjectId, int FileId), CurseForgeModFile> Files { get; set; } = [];

        public Task<IReadOnlyList<CurseForgeProject>> SearchProjectsAsync(
            string query,
            int classId = 6,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
            => throw new NotSupportedException();

        public Task<CurseForgeModFile?> GetFileAsync(
            int projectId,
            int fileId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Files.GetValueOrDefault((projectId, fileId)));
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

    private static CurseForgeModFile ModFile(
        string filename,
        string url = "https://edge.forgecdn.net/files/" + "mod.jar")
        => new()
        {
            Id = 200,
            DisplayName = filename,
            FileName = filename,
            DownloadUrl = url,
            FileLength = 999,
            FileHashes = [new CurseForgeFileHash { Algo = 1, Value = "abc123" }],
        };

    private void WriteZip(Action<ZipArchive> writer)
    {
        using var stream = File.Create(_zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        writer(archive);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string ManifestJson(string minecraftVersion = "1.20.1")
        => $$"""
            {
              "name": "Example Pack",
              "author": "author",
              "version": "1.0.0",
              "minecraft": {
                "version": "{{minecraftVersion}}",
                "modLoaders": [
                  { "id": "fabric-0.15.11", "primary": true }
                ]
              },
              "files": [
                { "projectID": 100, "fileID": 200, "required": true },
                { "projectID": 101, "fileID": 201, "required": false }
              ],
              "overrides": "overrides"
            }
            """;

    [Fact]
    public async Task InstallAsync_InstallsMissingBaseVersion()
    {
        WriteZip(archive => WriteEntry(archive, "manifest.json", ManifestJson()));
        var versionInstaller = new FakeVersionInstaller();
        var service = new ModpackInstallerService(
            new FakeApi(),
            new FakeDownloadClient(),
            versionInstaller);

        var result = await service.InstallAsync(_zipPath, Path.Combine(_folder, "mc"), DownloadSource.Bmclapi);

        var call = Assert.Single(versionInstaller.Calls);
        Assert.Equal(("1.20.1", DownloadSource.Bmclapi, Path.Combine(_folder, "mc")), call);
        Assert.Equal("1.20.1", result.MinecraftVersion);
    }

    [Fact]
    public async Task InstallAsync_SkipsBaseVersion_WhenAlreadyInstalled()
    {
        WriteZip(archive => WriteEntry(archive, "manifest.json", ManifestJson()));
        var mcFolder = Path.Combine(_folder, "mc");
        var versionFolder = Path.Combine(mcFolder, "versions", "1.20.1");
        Directory.CreateDirectory(versionFolder);
        File.WriteAllText(Path.Combine(versionFolder, "1.20.1.json"), "{}");
        var versionInstaller = new FakeVersionInstaller();
        var service = new ModpackInstallerService(
            new FakeApi(),
            new FakeDownloadClient(),
            versionInstaller);

        await service.InstallAsync(_zipPath, mcFolder, DownloadSource.Bmclapi);

        Assert.Empty(versionInstaller.Calls);
    }

    [Fact]
    public async Task InstallAsync_DownloadsRequiredMods_AndSkipsOptional()
    {
        WriteZip(archive => WriteEntry(archive, "manifest.json", ManifestJson()));
        var mcFolder = Path.Combine(_folder, "mc");
        var api = new FakeApi
        {
            Files =
            {
                [(100, 200)] = ModFile("required-mod.jar"),
            },
        };
        var client = new FakeDownloadClient();
        var service = new ModpackInstallerService(api, client, new FakeVersionInstaller());

        var result = await service.InstallAsync(_zipPath, mcFolder, DownloadSource.Bmclapi);

        var request = Assert.Single(client.Requests);
        Assert.Equal(Path.Combine(mcFolder, "mods", "required-mod.jar"), request.DestinationPath);
        Assert.Equal("abc123", request.ExpectedSha1);
        Assert.Equal(["required-mod.jar"], result.InstalledMods);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task InstallAsync_ExtractsOverridesIntoGameFolder()
    {
        WriteZip(archive =>
        {
            WriteEntry(archive, "manifest.json", ManifestJson());
            WriteEntry(archive, "overrides/config/example.properties", "a=1");
            WriteEntry(archive, "overrides/mods/extra.jar", "jar");
            WriteEntry(archive, "overrides/", "");
        });
        var mcFolder = Path.Combine(_folder, "mc");
        var service = new ModpackInstallerService(
            new FakeApi(),
            new FakeDownloadClient(),
            new FakeVersionInstaller());

        await service.InstallAsync(_zipPath, mcFolder, DownloadSource.Bmclapi);

        Assert.Equal("a=1", File.ReadAllText(Path.Combine(mcFolder, "config", "example.properties")));
        Assert.Equal("jar", File.ReadAllText(Path.Combine(mcFolder, "mods", "extra.jar")));
    }

    [Fact]
    public async Task InstallAsync_BlocksPathTraversalEntries()
    {
        WriteZip(archive =>
        {
            WriteEntry(archive, "manifest.json", ManifestJson());
            WriteEntry(archive, "overrides/../../escape.txt", "evil");
        });
        var mcFolder = Path.Combine(_folder, "mc");
        var service = new ModpackInstallerService(
            new FakeApi(),
            new FakeDownloadClient(),
            new FakeVersionInstaller());

        var result = await service.InstallAsync(_zipPath, mcFolder, DownloadSource.Bmclapi);

        Assert.False(File.Exists(Path.Combine(_folder, "escape.txt")));
        Assert.False(File.Exists(Path.Combine(mcFolder, "escape.txt")));
        Assert.Contains(result.Errors, item => item.Contains("不安全路径", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InstallAsync_MissingManifest_Throws()
    {
        WriteZip(archive => WriteEntry(archive, "readme.txt", "hello"));
        var service = new ModpackInstallerService(
            new FakeApi(),
            new FakeDownloadClient(),
            new FakeVersionInstaller());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InstallAsync(_zipPath, Path.Combine(_folder, "mc"), DownloadSource.Bmclapi));
    }
}
