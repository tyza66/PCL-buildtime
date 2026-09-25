using PCL.Avalonia.Services;
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

    private static (FakeSettingsService Settings, FakeThemeService Theme, MainWindowViewModel ViewModel) CreateViewModel(
        bool useDarkTheme)
    {
        var settings = new FakeSettingsService { Settings = new AppSettings { UseDarkTheme = useDarkTheme } };
        var theme = new FakeThemeService();
        var viewModel = new MainWindowViewModel(settings, theme);
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
    }

    [Fact]
    public void ToggleThemeCommand_FlipsTheme_AppliesAndPersists()
    {
        var (settings, theme, viewModel) = CreateViewModel(useDarkTheme: true);

        viewModel.ToggleThemeCommand.Execute(null);

        Assert.False(viewModel.UseDarkTheme);
        Assert.False(theme.LastAppliedTheme);
        Assert.False(settings.Settings.UseDarkTheme);
        Assert.Equal(1, settings.SaveCount);
    }
}
