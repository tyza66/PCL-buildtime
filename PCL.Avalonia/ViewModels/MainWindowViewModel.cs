using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;

    public MainWindowViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        SessionState session,
        IUiDispatcher dispatcher,
        IVersionCatalogService versionCatalogService,
        IGameLauncher gameLauncher,
        IJavaService javaService,
        IPlatformService platformService,
        IVersionManifestService versionManifestService,
        IVersionInstaller versionInstaller,
        IModsService modsService,
        IAccountService accountService,
        IModrinthApi modrinthApi,
        IModsDownloadService modsDownloadService,
        ICurseForgeApi curseForgeApi,
        ICurseForgeDownloadService curseForgeDownloadService,
        ICurseForgeModpackService curseForgeModpackService,
        IModpackInstallerService modpackInstaller)
    {
        _settingsService = settingsService;
        _themeService = themeService;

        var settings = settingsService.Load();
        UseDarkTheme = settings.UseDarkTheme;
        _themeService.Apply(UseDarkTheme);

        Items =
        [
            new NavItemViewModel("启动", new LaunchPageViewModel(settingsService, javaService, gameLauncher, session, dispatcher)),
            new NavItemViewModel("账号", new AccountsPageViewModel(accountService, session)),
            new NavItemViewModel("下载", new DownloadPageViewModel(settingsService, versionManifestService, versionInstaller, versionCatalogService, platformService, session)),
            new NavItemViewModel("Mod下载", new ModsDownloadPageViewModel(settingsService, modrinthApi, modsDownloadService, platformService, curseForgeApi, curseForgeDownloadService)),
            new NavItemViewModel("整合包", new IntegrationPacksPageViewModel(settingsService, curseForgeModpackService, modpackInstaller, platformService)),
            new NavItemViewModel("版本", new VersionPageViewModel(settingsService, versionCatalogService, session, platformService)),
            new NavItemViewModel("Mod管理", new ModsPageViewModel(settingsService, modsService, platformService)),
            new NavItemViewModel("设置", new SettingsPageViewModel(settingsService, platformService)),
            new NavItemViewModel("其他", new OtherPageViewModel()),
        ];

        SelectedItem = Items[0];
    }

    public ObservableCollection<NavItemViewModel> Items { get; }

    [ObservableProperty]
    private NavItemViewModel? _selectedItem;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeButtonText))]
    private bool _useDarkTheme;

    public string ThemeButtonText => UseDarkTheme ? "切换到浅色" : "切换到深色";

    partial void OnSelectedItemChanged(NavItemViewModel? value)
    {
        CurrentPage = value?.Page;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        UseDarkTheme = !UseDarkTheme;
        _themeService.Apply(UseDarkTheme);
        var current = _settingsService.Load();
        _settingsService.Save(current with { UseDarkTheme = UseDarkTheme });
    }
}
