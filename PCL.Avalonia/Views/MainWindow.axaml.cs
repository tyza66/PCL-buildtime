using Avalonia.Controls;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;
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
        var catalog = new VersionCatalogService();
        return new MainWindowViewModel(
            settings,
            new AvaloniaThemeService(),
            session,
            catalog,
            new GameLauncher(catalog),
            new JavaService(),
            platform);
    }
}
