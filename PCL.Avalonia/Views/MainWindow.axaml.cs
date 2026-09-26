using Avalonia.Controls;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.Services.Link;
using PCL.Avalonia.Services.Platform;
using PCL.Avalonia.ViewModels;

namespace PCL.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(CreateViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var platform = new PlatformService();
        var settings = new JsonSettingsService(Path.Combine(platform.GetConfigDirectory(), "settings.json"));
        // 启动页的 Java 状态行和设置页共用一份扫描结果，两边看到的版本信息才对得上。
        var javaListService = new JavaListService();
        var session = new SessionState();
        var dispatcher = new AvaloniaUiDispatcher();
        var catalog = new VersionCatalogService();
        var downloadClient = new HttpDownloadClient();
        var accountService = new JsonAccountService(Path.Combine(platform.GetConfigDirectory(), "accounts.json"));
        var microsoftAuthentication = new MicrosoftAuthenticationService(new DefaultBrowserLauncher());
        var modrinthApi = new ModrinthApi(downloadClient);
        var modsDownloadService = new ModsDownloadService(downloadClient);
        var curseForgeApi = new CurseForgeApi(downloadClient);
        var curseForgeDownloadService = new CurseForgeDownloadService(downloadClient);
        var resourceDownloadService = new ResourceDownloadService(downloadClient);
        var versionInstaller = new VersionInstaller(downloadClient, catalog);
        var modsService = new ModsService();
        var fabricLoaderService = new FabricLoaderService(downloadClient, versionInstaller);
        var forgelikeInstallRunner = new JavaForgelikeInstallRunner(settings, new JavaService());
        var forgelikeLoaderService = new ForgelikeLoaderService(downloadClient, versionInstaller, forgelikeInstallRunner);
        var curseForgeModpackService = new CurseForgeModpackService(curseForgeApi, downloadClient);
        var modpackInstaller = new ModpackInstallerService(curseForgeApi, downloadClient, versionInstaller);
        var linkService = LinkService.Create(
            Path.Combine(platform.GetConfigDirectory(), "EasyTier"),
            downloadClient);
        return new MainWindowViewModel(
            settings,
            new AvaloniaThemeService(),
            session,
            dispatcher,
            catalog,
            new GameLauncher(catalog, new MemoryOptimizer()),
            new JavaService(),
            platform,
            new VersionManifestService(downloadClient),
            versionInstaller,
            modsService,
            new InstanceClassifier(),
            new InstancePackExporter(),
            accountService,
            microsoftAuthentication,
            modrinthApi,
            modsDownloadService,
            curseForgeApi,
            curseForgeDownloadService,
            curseForgeModpackService,
            modpackInstaller,
            new ResourceSearcherService(modrinthApi, curseForgeApi),
            resourceDownloadService,
            fabricLoaderService,
            forgelikeLoaderService,
            new VersionManagerService(),
            new DefaultFolderOpener(),
            new LaunchScriptExporter(),
            new OtherToolsService(),
            linkService,
            javaListService);
    }
}
