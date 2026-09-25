using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;

    public MainWindowViewModel(ISettingsService settingsService, IThemeService themeService)
    {
        _settingsService = settingsService;
        _themeService = themeService;

        var settings = settingsService.Load();
        UseDarkTheme = settings.UseDarkTheme;
        _themeService.Apply(UseDarkTheme);

        Items =
        [
            new NavItemViewModel("启动", new LaunchPageViewModel()),
            new NavItemViewModel("下载", new DownloadPageViewModel()),
            new NavItemViewModel("版本", new VersionPageViewModel()),
            new NavItemViewModel("设置", new SettingsPageViewModel()),
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
        _settingsService.Save(new AppSettings { UseDarkTheme = UseDarkTheme });
    }
}
