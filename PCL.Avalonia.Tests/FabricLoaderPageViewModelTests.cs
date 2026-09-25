using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class FabricLoaderPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class FakeFabricLoaderService : IFabricLoaderService
    {
        public IReadOnlyList<FabricLoaderVersion> Versions { get; set; } = [];

        public FabricInstallResult Result { get; set; } = new("fabric-loader-0.16.9-1.20.1", "1.20.1", "0.16.9", []);

        public string? LastGameVersion { get; private set; }

        public string? LastLoaderVersion { get; private set; }

        public Task<IReadOnlyList<FabricLoaderVersion>> GetVersionsAsync(
            string gameVersion,
            CancellationToken cancellationToken = default)
        {
            LastGameVersion = gameVersion;
            return Task.FromResult(Versions);
        }

        public Task<FabricInstallResult> InstallAsync(
            string gameVersion,
            string loaderVersion,
            string minecraftFolder,
            DownloadSource source,
            IProgress<FabricInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastGameVersion = gameVersion;
            LastLoaderVersion = loaderVersion;
            return Task.FromResult(Result);
        }
    }

    private static (FakeSettingsService Settings, FakeFabricLoaderService Service, SessionState Session, FabricLoaderPageViewModel ViewModel)
        CreateViewModel()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { MinecraftFolder = "/games/mc", DownloadSource = DownloadSource.Mojang },
        };
        var service = new FakeFabricLoaderService();
        var session = new SessionState();
        var viewModel = new FabricLoaderPageViewModel(
            settings,
            service,
            new FakePlatformService(),
            session);
        return (settings, service, session, viewModel);
    }

    [Fact]
    public async Task RefreshAsync_LoadsFabricVersions()
    {
        var (_, service, _, viewModel) = CreateViewModel();
        service.Versions =
        [
            new FabricLoaderVersion("0.16.9", "1.20.1", true, 17),
            new FabricLoaderVersion("0.15.0", "1.20.1", false, 17),
        ];
        viewModel.GameVersionText = "1.20.1";

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Versions.Count);
        Assert.Equal("1.20.1", service.LastGameVersion);
        Assert.Contains("2", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallAsync_InstallsSelectedLoaderAndNotifiesSession()
    {
        var (settings, service, session, viewModel) = CreateViewModel();
        service.Versions = [new FabricLoaderVersion("0.16.9", "1.20.1", true, 17)];
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var selected = viewModel.Versions[0];
        viewModel.SelectedLoader = selected;
        string? installedVersionId = null;
        session.VersionInstalled += (_, versionId) => installedVersionId = versionId;

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal(("1.20.1", "0.16.9"), (service.LastGameVersion, service.LastLoaderVersion));
        Assert.Equal("/games/mc", settings.Settings.MinecraftFolder);
        Assert.Equal("已安装 fabric-loader-0.16.9-1.20.1", viewModel.StatusMessage);
        Assert.Equal("fabric-loader-0.16.9-1.20.1", installedVersionId);
    }

    [Fact]
    public async Task InstallAsync_FailureShowsSummary()
    {
        var (_, service, _, viewModel) = CreateViewModel();
        service.Versions = [new FabricLoaderVersion("0.16.9", "1.20.1", true, 17)];
        service.Result = new FabricInstallResult(
            "fabric-loader-0.16.9-1.20.1",
            "1.20.1",
            "0.16.9",
            ["支持库 asm 下载失败"]);
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedLoader = viewModel.Versions[0];

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.StartsWith("安装未完成", viewModel.StatusMessage);
        Assert.Contains("asm 下载失败", viewModel.StatusMessage);
    }
}
