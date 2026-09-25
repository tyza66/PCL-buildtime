using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class DownloadPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeManifestService : IVersionManifestService
    {
        public VersionManifest Manifest { get; set; } = new();

        public DownloadSource? LastSource { get; private set; }

        public Exception? Exception { get; set; }

        public Task<VersionManifest> GetManifestAsync(
            DownloadSource source,
            CancellationToken cancellationToken = default)
        {
            LastSource = source;
            return Exception is null
                ? Task.FromResult(Manifest)
                : Task.FromException<VersionManifest>(Exception);
        }
    }

    private sealed class FakeInstaller : IVersionInstaller
    {
        public VersionInstallResult Result { get; set; } = new("", []);

        public List<(string VersionId, DownloadSource Source, string Folder)> Calls { get; } = [];

        public TaskCompletionSource? Gate { get; set; }

        public async Task<VersionInstallResult> InstallAsync(
            string versionId,
            VersionManifestEntry? entry,
            DownloadSource source,
            string minecraftFolder,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((versionId, source, minecraftFolder));
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Result;
        }
    }

    private sealed class FakeCatalog : IVersionCatalogService
    {
        public IReadOnlyList<MinecraftVersion> Installed { get; set; } = [];

        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder) => Installed;

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id) => null;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private static DownloadPageViewModel CreateViewModel(
        FakeSettingsService settings,
        FakeManifestService manifest,
        FakeInstaller installer,
        FakeCatalog catalog)
        => new(settings, manifest, installer, catalog, new FakePlatformService());

    [Fact]
    public async Task RefreshAsync_LoadsManifestAndMarksInstalled()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings
            {
                MinecraftFolder = "/games/mc",
                DownloadSource = DownloadSource.Mojang,
            },
        };
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions =
                [
                    new VersionManifestEntry
                    {
                        Id = "1.20.1",
                        Type = "release",
                        ReleaseTime = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    },
                    new VersionManifestEntry
                    {
                        Id = "24w10a",
                        Type = "snapshot",
                        ReleaseTime = DateTimeOffset.Parse("2024-03-06T12:00:00Z"),
                    },
                ],
            },
        };
        var catalog = new FakeCatalog
        {
            Installed =
            [
                new MinecraftVersion { Id = "1.20.1", Folder = "/x", JsonPath = "/x/1.20.1.json" },
            ],
        };
        var viewModel = CreateViewModel(settings, manifest, new FakeInstaller(), catalog);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Versions.Count);
        Assert.Equal("24w10a", viewModel.Versions[0].Id);
        Assert.True(viewModel.Versions[1].IsInstalled);
        Assert.Equal(DownloadSource.Mojang, manifest.LastSource);
        Assert.Contains("2", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SearchText_FiltersVersions()
    {
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions =
                [
                    new VersionManifestEntry { Id = "1.20.1", ReleaseTime = DateTimeOffset.Parse("2024-01-01T00:00:00Z") },
                    new VersionManifestEntry { Id = "24w10a", ReleaseTime = DateTimeOffset.Parse("2024-03-06T12:00:00Z") },
                ],
            },
        };
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            manifest,
            new FakeInstaller(),
            new FakeCatalog());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SearchText = "1.20";

        var item = Assert.Single(viewModel.Versions);
        Assert.Equal("1.20.1", item.Id);
    }

    [Fact]
    public async Task InstallAsync_CallsInstaller_AndMarksInstalled()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings
            {
                MinecraftFolder = "/games/mc",
                DownloadSource = DownloadSource.Bmclapi,
            },
        };
        var installer = new FakeInstaller
        {
            Result = new VersionInstallResult("1.20.1", []),
        };
        var viewModel = CreateViewModel(settings, new FakeManifestService(), installer, new FakeCatalog());
        var item = new DownloadVersionItemViewModel(
            new VersionManifestEntry { Id = "1.20.1" },
            isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;

        await viewModel.InstallCommand.ExecuteAsync(null);

        var call = Assert.Single(installer.Calls);
        Assert.Equal(("1.20.1", DownloadSource.Bmclapi, "/games/mc"), (call.VersionId, call.Source, call.Folder));
        Assert.True(item.IsInstalled);
        Assert.Equal("已安装 1.20.1", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallAsync_FailureShowsSummary()
    {
        var installer = new FakeInstaller
        {
            Result = new VersionInstallResult("1.20.1", ["支持库 core 下载失败", "资源 icon.png 下载失败"]),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), new FakeManifestService(), installer, new FakeCatalog());
        var item = new DownloadVersionItemViewModel(new VersionManifestEntry { Id = "1.20.1" }, isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.StartsWith("安装未完成", viewModel.StatusMessage);
        Assert.Contains("支持库 core 下载失败", viewModel.StatusMessage);
        Assert.False(item.IsInstalled);
    }

    [Fact]
    public async Task InstallAsync_CancelStopsInstallation()
    {
        var installer = new FakeInstaller
        {
            Gate = new TaskCompletionSource(),
            Result = new VersionInstallResult("1.20.1", []),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), new FakeManifestService(), installer, new FakeCatalog());
        var item = new DownloadVersionItemViewModel(new VersionManifestEntry { Id = "1.20.1" }, isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;

        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        viewModel.CancelCommand.Execute(null);

        await installTask;

        Assert.Equal("安装已取消", viewModel.StatusMessage);
        Assert.False(viewModel.IsInstalling);
        Assert.False(item.IsInstalled);
    }

    [Fact]
    public async Task RefreshAsync_FailureShowsMessage()
    {
        var manifest = new FakeManifestService
        {
            Exception = new InvalidOperationException("network down"),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), manifest, new FakeInstaller(), new FakeCatalog());

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.StartsWith("获取版本清单失败", viewModel.StatusMessage);
        Assert.Empty(viewModel.Versions);
    }
}
