using Avalonia.Controls;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
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
        var session = new SessionState();
        var dispatcher = new AvaloniaUiDispatcher();
        var catalog = new VersionCatalogService();
        var downloadClient = new HttpDownloadClient();
        var accountService = new JsonAccountService(Path.Combine(platform.GetConfigDirectory(), "accounts.json"));
        var modrinthApi = new ModrinthApi(downloadClient);
        var modsDownloadService = new ModsDownloadService(downloadClient);
        return new MainWindowViewModel(
            settings,
            new AvaloniaThemeService(),
            session,
            dispatcher,
            catalog,
            new GameLauncher(catalog),
            new JavaService(),
            platform,
            new VersionManifestService(downloadClient),
            new VersionInstaller(downloadClient, catalog),
            new ModsService(),
            accountService,
            modrinthApi,
            modsDownloadService);
    }
}
