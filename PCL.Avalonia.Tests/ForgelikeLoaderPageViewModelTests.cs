using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class ForgelikeLoaderPageViewModelTests
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

    private sealed class FakeForgelikeLoaderService : IForgelikeLoaderService
    {
        public IReadOnlyList<ForgelikeLoaderVersion> ForgeVersions { get; set; } = [];

        public IReadOnlyList<ForgelikeLoaderVersion> NeoForgeVersions { get; set; } = [];

        public ForgelikeInstallResult Result { get; set; } =
            new("forge-47.1.82-1.20.1", "1.20.1", "47.1.82", []);

        public string? LastGameVersion { get; private set; }

        public ForgelikeLoaderVersion? LastInstalledVersion { get; private set; }

        public Task<IReadOnlyList<ForgelikeLoaderVersion>> GetForgeVersionsAsync(
            string gameVersion,
            CancellationToken cancellationToken = default)
        {
            LastGameVersion = gameVersion;
            return Task.FromResult(ForgeVersions);
        }

        public Task<IReadOnlyList<ForgelikeLoaderVersion>> GetNeoForgeVersionsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(NeoForgeVersions);

        public Task<ForgelikeInstallResult> InstallAsync(
            ForgelikeLoaderVersion version,
            string minecraftFolder,
            DownloadSource source,
            IProgress<ForgelikeInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastInstalledVersion = version;
            return Task.FromResult(Result);
        }
    }

    private static (FakeSettingsService Settings, FakeForgelikeLoaderService Service, SessionState Session, ForgelikeLoaderPageViewModel ViewModel)
        CreateViewModel(bool neoForgeMode = false)
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { MinecraftFolder = "/games/mc", DownloadSource = DownloadSource.Mojang },
        };
        var service = new FakeForgelikeLoaderService();
        var session = new SessionState();
        var viewModel = new ForgelikeLoaderPageViewModel(settings, service, new FakePlatformService(), session);
        viewModel.IsNeoForgeMode = neoForgeMode;
        return (settings, service, session, viewModel);
    }

    [Fact]
    public async Task RefreshAsync_ForgeMode_LoadsForgeVersions()
    {
        var (_, service, _, viewModel) = CreateViewModel();
        service.ForgeVersions =
        [
            new ForgelikeLoaderVersion(
                ForgelikeKind.Forge,
                "47.1.82",
                "1.20.1",
                false,
                new Version(47, 1, 82)),
        ];
        viewModel.GameVersionText = "1.20.1";

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("1.20.1", service.LastGameVersion);
        Assert.Single(viewModel.Versions);
        Assert.Contains("1", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshAsync_NeoForgeMode_LoadsNeoForgeVersions()
    {
        var (_, service, _, viewModel) = CreateViewModel(neoForgeMode: true);
        service.NeoForgeVersions =
        [
            new ForgelikeLoaderVersion(
                ForgelikeKind.NeoForge,
                "21.1.120",
                "1.21.1",
                false,
                new Version(21, 1, 120)),
        ];

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(service.LastGameVersion);
        Assert.Single(viewModel.Versions);
        Assert.Contains("NeoForge", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshAsync_ForgeModeWithoutGameVersion_ShowsPrompt()
    {
        var (_, service, _, viewModel) = CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("请先填写游戏版本", viewModel.StatusMessage);
        Assert.Null(service.LastGameVersion);
        Assert.Empty(viewModel.Versions);
    }

    [Fact]
    public async Task InstallAsync_InstallsSelectedVersionAndNotifiesSession()
    {
        var (settings, service, session, viewModel) = CreateViewModel(neoForgeMode: true);
        service.Result = new ForgelikeInstallResult(
            "neoforge-21.1.120",
            "1.21.1",
            "21.1.120",
            []);
        service.NeoForgeVersions =
        [
            new ForgelikeLoaderVersion(
                ForgelikeKind.NeoForge,
                "21.1.120",
                "1.21.1",
                false,
                new Version(21, 1, 120)),
        ];
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedLoader = viewModel.Versions[0];
        string? installedVersionId = null;
        session.VersionInstalled += (_, versionId) => installedVersionId = versionId;

        await viewModel.InstallCommand.ExecuteAsync(null);

        var installed = service.LastInstalledVersion;
        Assert.NotNull(installed);
        Assert.Equal("21.1.120", installed.VersionName);
        Assert.Equal("/games/mc", settings.Settings.MinecraftFolder);
        Assert.Equal("已安装 neoforge-21.1.120", viewModel.StatusMessage);
        Assert.Equal("neoforge-21.1.120", installedVersionId);
    }

    [Fact]
    public async Task InstallAsync_FailureShowsSummary()
    {
        var (_, service, _, viewModel) = CreateViewModel();
        service.ForgeVersions =
        [
            new ForgelikeLoaderVersion(
                ForgelikeKind.Forge,
                "47.1.82",
                "1.20.1",
                false,
                new Version(47, 1, 82)),
        ];
        service.Result = new ForgelikeInstallResult(
            "forge-47.1.82-1.20.1",
            "1.20.1",
            "47.1.82",
            ["支持库 asm 下载失败"]);
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedLoader = viewModel.Versions[0];

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.StartsWith("安装未完成", viewModel.StatusMessage);
        Assert.Contains("asm 下载失败", viewModel.StatusMessage);
    }
}
