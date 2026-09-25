using System.IO.Compression;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Link;

namespace PCL.Avalonia.Tests;

public sealed class LinkBinaryInstallerTests
{
    private sealed class FakeDownloadClient : IDownloadClient
    {
        private readonly Func<DownloadRequest, CancellationToken, Task> _onDownload;

        public FakeDownloadClient(Func<DownloadRequest, CancellationToken, Task> onDownload)
        {
            _onDownload = onDownload;
        }

        public IReadOnlyList<DownloadRequest> Requests { get; private set; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests = [.. Requests, request];
            return _onDownload(request, cancellationToken);
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

    [Fact]
    public async Task EnsureInstalledAsync_WhenUpToDate_SkipsDownload()
    {
        var directory = CreateDirectoryWithBinaries(isWindows: true);
        var client = new FakeDownloadClient((_, _) => Task.CompletedTask);
        var installer = new LinkBinaryInstaller(
            directory,
            client,
            () => ("windows", "x86_64"),
            "v2.6.4",
            includePacketDll: true);

        var persistedVersion = -1;
        await installer.EnsureInstalledAsync(
            LinkBinaryInstaller.CurrentEasyTierVersion,
            version => persistedVersion = version);

        Assert.Empty(client.Requests);
        Assert.Equal(-1, persistedVersion);
        Assert.Equal(Path.Combine(directory, "联机模块.exe"), installer.CorePath);
        Assert.Equal(Path.Combine(directory, "联机模块 CLI.exe"), installer.CliPath);
    }

    [Fact]
    public async Task EnsureInstalledAsync_Windows_DownloadsAndRenamesBinaries()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        var client = new FakeDownloadClient((request, cancellationToken) =>
        {
            CreateEasyTierZip(
                request.DestinationPath,
                ("easytier-core.exe", "core"),
                ("easytier-cli.exe", "cli"),
                ("Packet.dll", "packet"));
            return Task.CompletedTask;
        });
        var installer = new LinkBinaryInstaller(
            directory,
            client,
            () => ("windows", "x86_64"),
            "v2.6.4",
            includePacketDll: true);

        var persistedVersion = -1;
        await installer.EnsureInstalledAsync(-1, version => persistedVersion = version);

        var request = Assert.Single(client.Requests);
        Assert.Equal(
            "https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-x86_64-v2.6.4.zip",
            request.Urls.Single());
        Assert.Equal(LinkBinaryInstaller.CurrentEasyTierVersion, persistedVersion);
        Assert.True(File.Exists(Path.Combine(directory, "联机模块.exe")));
        Assert.True(File.Exists(Path.Combine(directory, "联机模块 CLI.exe")));
        Assert.True(File.Exists(Path.Combine(directory, "Packet.dll")));
        Assert.False(File.Exists(Path.Combine(directory, "EasyTier.zip")));
    }

    [Fact]
    public async Task EnsureInstalledAsync_MacArm_UsesAarch64WithoutExtension()
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        var client = new FakeDownloadClient((request, cancellationToken) =>
        {
            CreateEasyTierZip(
                request.DestinationPath,
                ("easytier-core", "core"),
                ("easytier-cli", "cli"));
            return Task.CompletedTask;
        });
        var installer = new LinkBinaryInstaller(
            directory,
            client,
            () => ("macos", "aarch64"),
            "v2.6.4",
            includePacketDll: false);

        await installer.EnsureInstalledAsync(-1, _ => { });

        var request = Assert.Single(client.Requests);
        Assert.Equal(
            "https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-macos-aarch64-v2.6.4.zip",
            request.Urls.Single());
        Assert.True(File.Exists(Path.Combine(directory, "联机模块")));
        Assert.True(File.Exists(Path.Combine(directory, "联机模块 CLI")));
    }

    private static string CreateDirectoryWithBinaries(bool isWindows)
    {
        var directory = Directory.CreateTempSubdirectory().FullName;
        var suffix = isWindows ? ".exe" : "";
        File.WriteAllText(Path.Combine(directory, "联机模块" + suffix), "core");
        File.WriteAllText(Path.Combine(directory, "联机模块 CLI" + suffix), "cli");
        if (isWindows)
        {
            File.WriteAllText(Path.Combine(directory, "Packet.dll"), "packet");
        }

        return directory;
    }

    private static void CreateEasyTierZip(string path, params (string Name, string Content)[] entries)
    {
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }
}
