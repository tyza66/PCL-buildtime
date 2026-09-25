using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.Services.Platform;
using PCL.Avalonia.ViewModels;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class MainWindowViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();
        public int SaveCount { get; private set; }

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings)
        {
            Settings = settings;
            SaveCount++;
        }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public bool? LastAppliedTheme { get; private set; }

        public void Apply(bool useDarkTheme) => LastAppliedTheme = useDarkTheme;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => Path.Combine(Path.GetTempPath(), ".minecraft");
    }

    private sealed class FakeVersionCatalogService : IVersionCatalogService
    {
        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder) => [];

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id) => null;
    }

    private sealed class FakeGameLauncher : IGameLauncher
    {
        public LaunchPlan BuildLaunchPlan(
            MinecraftVersion version,
            AppSettings settings,
            string javaExecutable,
            Account? account = null)
            => throw new NotSupportedException();

        public IGameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null)
            => throw new NotSupportedException();
    }

    private sealed class FakeJavaService : IJavaService
    {
        public string? ResolveJavaExecutable(AppSettings settings) => settings.JavaPath;
    }

    private sealed class FakeManifestService : IVersionManifestService
    {
        public Task<VersionManifest> GetManifestAsync(
            DownloadSource source,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new VersionManifest());
    }

    private sealed class FakeInstaller : IVersionInstaller
    {
        public Task<VersionInstallResult> InstallAsync(
            string versionId,
            VersionManifestEntry? entry,
            DownloadSource source,
            string minecraftFolder,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new VersionInstallResult(versionId, []));
    }

    private sealed class FakeModsService : IModsService
    {
        public IReadOnlyList<ModInfo> Scan(string minecraftFolder) => [];

        public ModInfo SetEnabled(ModInfo mod, bool enabled) => mod;

        public void Delete(ModInfo mod)
        {
        }
    }

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class FakeAccountService : IAccountService
    {
        public IReadOnlyList<Account> Load() => [];

        public Account AddOfflineAccount(string name) => new() { Id = Guid.NewGuid(), Name = name };

        public Account AddMicrosoftAccount(MicrosoftAccountSession session)
            => new()
            {
                Id = Guid.NewGuid(),
                Name = session.Name,
                Type = "microsoft",
                Uuid = session.Uuid,
            };

        public void RemoveAccount(Guid id)
        {
        }

        public void SetDefaultAccount(Guid id)
        {
        }

        public Account? GetDefaultAccount() => null;
    }

    private sealed class FakeMicrosoftAuthenticationService : IMicrosoftAuthenticationService
    {
        public Task<MicrosoftAccountSession> LoginAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MicrosoftAccountSession
            {
                Name = "Alex",
                Uuid = "11111111-2222-3333-4444-555555555555",
                AccessToken = "token",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            });

        public Task<MicrosoftAccountSession?> RefreshAsync(
            Account account,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<MicrosoftAccountSession?>(null);
    }

    private sealed class FakeModrinthApi : IModrinthApi
    {
        public Task<IReadOnlyList<ModrinthProject>> SearchProjectsAsync(
            string query,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModrinthProject>>([]);

        public Task<IReadOnlyList<ModrinthProjectVersion>> GetVersionsAsync(
            string projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModrinthProjectVersion>>([]);
    }

    private sealed class FakeModsDownloadService : IModsDownloadService
    {
        public Task<string> InstallAsync(
            ModrinthProjectVersion version,
            string modsFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult("");
    }

    private sealed class FakeCurseForgeApi : ICurseForgeApi
    {
        public Task<IReadOnlyList<CurseForgeProject>> SearchProjectsAsync(
            string query,
            int classId = 6,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeProject>>([]);

        public Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
            int projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeModFile>>([]);

        public Task<IReadOnlyList<CurseForgeModFile>> GetModpackFilesAsync(
            int projectId,
            string gameVersion,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeModFile>>([]);

        public Task<CurseForgeModFile?> GetFileAsync(
            int projectId,
            int fileId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<CurseForgeModFile?>(null);
    }

    private sealed class FakeCurseForgeDownloadService : ICurseForgeDownloadService
    {
        public Task<string> InstallAsync(
            CurseForgeModFile file,
            string modsFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult("");
    }

    private sealed class FakeCurseForgeModpackService : ICurseForgeModpackService
    {
        public Task<IReadOnlyList<CurseForgeProject>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeProject>>([]);

        public Task<string> InstallAsync(
            CurseForgeProject project,
            string gameVersion,
            string minecraftFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult("");
    }

    private sealed class FakeModpackInstaller : IModpackInstallerService
    {
        public Task<ModpackInstallResult> InstallAsync(
            string modpackZipPath,
            string minecraftFolder,
            DownloadSource source,
            IProgress<ModpackInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModpackInstallResult("pack", "1.20.1", [], []));
    }

    private sealed class FakeFabricLoaderService : IFabricLoaderService
    {
        public Task<IReadOnlyList<FabricLoaderVersion>> GetVersionsAsync(
            string gameVersion,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FabricLoaderVersion>>([]);

        public Task<FabricInstallResult> InstallAsync(
            string gameVersion,
            string loaderVersion,
            string minecraftFolder,
            DownloadSource source,
            IProgress<FabricInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new FabricInstallResult("fabric-loader", gameVersion, loaderVersion, []));
    }

    private sealed class FakeForgelikeLoaderService : IForgelikeLoaderService
    {
        public Task<IReadOnlyList<ForgelikeLoaderVersion>> GetForgeVersionsAsync(
            string gameVersion,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ForgelikeLoaderVersion>>([]);

        public Task<IReadOnlyList<ForgelikeLoaderVersion>> GetNeoForgeVersionsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ForgelikeLoaderVersion>>([]);

        public Task<ForgelikeInstallResult> InstallAsync(
            ForgelikeLoaderVersion version,
            string minecraftFolder,
            DownloadSource source,
            IProgress<ForgelikeInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ForgelikeInstallResult(
                $"{version.Kind.ToString().ToLowerInvariant()}-{version.VersionName}",
                version.GameVersion,
                version.VersionName,
            []));
    }

    private sealed class FakeVersionManager : IVersionManagerService
    {
        public VersionSettings LoadSettings(string minecraftFolder, string versionId)
            => new();

        public void SetFavorite(string minecraftFolder, string versionId, bool isFavorite)
        {
        }

        public void SetHidden(string minecraftFolder, string versionId, bool isHidden)
        {
        }

        public void SetDescription(string minecraftFolder, string versionId, string description)
        {
        }

        public string Rename(string minecraftFolder, string versionId, string newName) => newName;

        public void Delete(string minecraftFolder, string versionId)
        {
        }
    }

    private sealed class FakeFolderOpener : IFolderOpener
    {
        public void Open(string path)
        {
        }
    }

    private sealed class FakeScriptExporter : ILaunchScriptExporter
    {
        public string Export(LaunchPlan plan, string filePath) => filePath;
    }

    private sealed class FakeOtherToolsService : IOtherToolsService
    {
        public OtherEnvironmentInfo GetEnvironmentInfo(string minecraftFolder, string configDirectory)
            => new("1.0.0", ".NET 8.0", "TestOS", minecraftFolder, configDirectory);

        public GarbageReport ScanGarbage(IReadOnlyList<string> roots) => new(0, 0);

        public GarbageReport CleanGarbage(IReadOnlyList<string> roots) => new(0, 0);
    }

    private static (FakeSettingsService Settings, FakeThemeService Theme, MainWindowViewModel ViewModel) CreateViewModel(
        bool useDarkTheme)
    {
        var settings = new FakeSettingsService { Settings = new AppSettings { UseDarkTheme = useDarkTheme } };
        var theme = new FakeThemeService();
        var viewModel = new MainWindowViewModel(
            settings,
            theme,
            new SessionState(),
            new FakeDispatcher(),
            new FakeVersionCatalogService(),
            new FakeGameLauncher(),
            new FakeJavaService(),
            new FakePlatformService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeModsService(),
            new FakeAccountService(),
            new FakeMicrosoftAuthenticationService(),
            new FakeModrinthApi(),
            new FakeModsDownloadService(),
            new FakeCurseForgeApi(),
            new FakeCurseForgeDownloadService(),
            new FakeCurseForgeModpackService(),
            new FakeModpackInstaller(),
            new FakeFabricLoaderService(),
            new FakeForgelikeLoaderService(),
            new FakeVersionManager(),
            new FakeFolderOpener(),
            new FakeScriptExporter(),
            new FakeOtherToolsService());
        return (settings, theme, viewModel);
    }

    [Fact]
    public void Constructor_AppliesSavedTheme_AndSelectsFirstPage()
    {
        var (_, theme, viewModel) = CreateViewModel(useDarkTheme: true);

        Assert.True(viewModel.UseDarkTheme);
        Assert.Equal("启动", viewModel.SelectedItem?.Title);
        Assert.IsType<LaunchPageViewModel>(viewModel.CurrentPage);
        Assert.True(theme.LastAppliedTheme);
    }

    [Fact]
    public void SelectingNavItem_SwitchesCurrentPage()
    {
        var (_, _, viewModel) = CreateViewModel(useDarkTheme: true);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "版本");

        Assert.IsType<VersionPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "设置");

        Assert.IsType<SettingsPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "下载");

        Assert.IsType<DownloadPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "Fabric");

        Assert.IsType<FabricLoaderPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "Forge");

        Assert.IsType<ForgelikeLoaderPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "Mod管理");

        Assert.IsType<ModsPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "账号");

        Assert.IsType<AccountsPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "Mod下载");

        Assert.IsType<ModsDownloadPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "整合包");

        Assert.IsType<IntegrationPacksPageViewModel>(viewModel.CurrentPage);

        viewModel.SelectedItem = viewModel.Items.Single(item => item.Title == "其他");

        Assert.IsType<OtherPageViewModel>(viewModel.CurrentPage);
    }

    [Fact]
    public void ToggleThemeCommand_FlipsTheme_AppliesAndPersists()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { UseDarkTheme = true, MinecraftFolder = "/games/mc" },
        };
        var theme = new FakeThemeService();
        var viewModel = new MainWindowViewModel(
            settings,
            theme,
            new SessionState(),
            new FakeDispatcher(),
            new FakeVersionCatalogService(),
            new FakeGameLauncher(),
            new FakeJavaService(),
            new FakePlatformService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeModsService(),
            new FakeAccountService(),
            new FakeMicrosoftAuthenticationService(),
            new FakeModrinthApi(),
            new FakeModsDownloadService(),
            new FakeCurseForgeApi(),
            new FakeCurseForgeDownloadService(),
            new FakeCurseForgeModpackService(),
            new FakeModpackInstaller(),
            new FakeFabricLoaderService(),
            new FakeForgelikeLoaderService(),
            new FakeVersionManager(),
            new FakeFolderOpener(),
            new FakeScriptExporter(),
            new FakeOtherToolsService());

        viewModel.ToggleThemeCommand.Execute(null);

        Assert.False(viewModel.UseDarkTheme);
        Assert.False(theme.LastAppliedTheme);
        Assert.False(settings.Settings.UseDarkTheme);
        Assert.Equal("/games/mc", settings.Settings.MinecraftFolder);
        Assert.Equal(1, settings.SaveCount);
    }
}
