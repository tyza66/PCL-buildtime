using PCL.Avalonia.Services;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class SettingsPageViewModelTests
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

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class FakeThemeService : IThemeService
    {
        public bool? LastAppliedTheme { get; private set; }

        public void Apply(bool useDarkTheme) => LastAppliedTheme = useDarkTheme;
    }

    private static SettingsPageViewModel CreateViewModel(
        FakeSettingsService settings,
        FakeThemeService? theme = null)
    {
        return new SettingsPageViewModel(settings, new FakePlatformService(), theme ?? new FakeThemeService());
    }

    [Fact]
    public void Constructor_LoadsSettingsIntoFields()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings
            {
                MinecraftFolder = "/games/mc",
                JavaPath = "/opt/java/bin/java",
                UserName = "Alex",
                MaxMemoryMb = 6144,
                DownloadSource = DownloadSource.Mojang,
                JvmArguments = "-Dcustom=1",
                GameArguments = "--demo",
                DownloadThreads = 128,
                DownloadSpeedLimitKbps = 1024,
                OptimizeMemoryBeforeLaunch = false,
                LinkLatencyMode = LinkLatencyMode.PreferredLowLatency,
                LinkCustomPeer = "tcp://peer.example:11010",
                UseDarkTheme = false,
            },
        };

        var viewModel = CreateViewModel(settings);

        Assert.Equal("/games/mc", viewModel.MinecraftFolder);
        Assert.Equal("/opt/java/bin/java", viewModel.JavaPath);
        Assert.Equal("Alex", viewModel.UserName);
        Assert.Equal(6144, viewModel.MaxMemoryMb);
        Assert.Equal(DownloadSource.Mojang, viewModel.DownloadSource.Source);
        Assert.Equal("-Dcustom=1", viewModel.JvmArguments);
        Assert.Equal("--demo", viewModel.GameArguments);
        Assert.Equal(128, viewModel.DownloadThreads);
        Assert.Equal(1024, viewModel.DownloadSpeedLimitKbps);
        Assert.False(viewModel.OptimizeMemoryBeforeLaunch);
        Assert.Equal(LinkLatencyMode.PreferredLowLatency, viewModel.LinkLatencyMode.Mode);
        Assert.Equal("tcp://peer.example:11010", viewModel.LinkCustomPeer);
        Assert.False(viewModel.UseDarkTheme);
    }

    [Fact]
    public void Constructor_EmptyFolder_UsesPlatformDefault()
    {
        var viewModel = CreateViewModel(new FakeSettingsService());

        Assert.Equal("/default/.minecraft", viewModel.MinecraftFolder);
        Assert.Equal("Player", viewModel.UserName);
        Assert.Equal(4096, viewModel.MaxMemoryMb);
        Assert.Equal(64, viewModel.DownloadThreads);
        Assert.Equal(0, viewModel.DownloadSpeedLimitKbps);
        Assert.True(viewModel.OptimizeMemoryBeforeLaunch);
        Assert.Equal(LinkLatencyMode.PreferredDirect, viewModel.LinkLatencyMode.Mode);
        Assert.Equal("", viewModel.LinkCustomPeer);
        Assert.True(viewModel.UseDarkTheme);
        Assert.Equal("启动", viewModel.SelectedSection.Title);
    }

    [Fact]
    public void Save_PersistsFields_AndPreservesOtherSettings()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { UseDarkTheme = false, MinecraftFolder = "/old/mc" },
        };
        var viewModel = CreateViewModel(settings);
        viewModel.MinecraftFolder = "/new/mc";
        viewModel.JavaPath = "/new/java";
        viewModel.UserName = "Steve";
        viewModel.MaxMemoryMb = 8192;
        viewModel.DownloadSource = viewModel.DownloadSources.Single(
            option => option.Source == DownloadSource.Mojang);
        viewModel.JvmArguments = "-Dcustom=2";
        viewModel.GameArguments = "--config \"hello world\"";
        viewModel.DownloadThreads = 200;
        viewModel.DownloadSpeedLimitKbps = 2048;
        viewModel.OptimizeMemoryBeforeLaunch = false;
        viewModel.LinkLatencyMode = viewModel.LinkLatencyModes.Single(
            option => option.Mode == LinkLatencyMode.PreferredLowLatency);
        viewModel.LinkCustomPeer = "tcp://peer2.example:11010";
        viewModel.UseDarkTheme = true;

        viewModel.SaveCommand.Execute(null);

        Assert.Equal("/new/mc", settings.Settings.MinecraftFolder);
        Assert.Equal("/new/java", settings.Settings.JavaPath);
        Assert.Equal("Steve", settings.Settings.UserName);
        Assert.Equal(8192, settings.Settings.MaxMemoryMb);
        Assert.Equal(DownloadSource.Mojang, settings.Settings.DownloadSource);
        Assert.Equal("-Dcustom=2", settings.Settings.JvmArguments);
        Assert.Equal("--config \"hello world\"", settings.Settings.GameArguments);
        Assert.Equal(200, settings.Settings.DownloadThreads);
        Assert.Equal(2048, settings.Settings.DownloadSpeedLimitKbps);
        Assert.False(settings.Settings.OptimizeMemoryBeforeLaunch);
        Assert.Equal(LinkLatencyMode.PreferredLowLatency, settings.Settings.LinkLatencyMode);
        Assert.Equal("tcp://peer2.example:11010", settings.Settings.LinkCustomPeer);
        Assert.True(settings.Settings.UseDarkTheme);
        Assert.Equal(1, settings.SaveCount);
        Assert.Equal("设置已保存", viewModel.StatusMessage);
    }

    [Fact]
    public void Save_ClampsNumericSettings()
    {
        var settings = new FakeSettingsService();
        var viewModel = CreateViewModel(settings);
        viewModel.MaxMemoryMb = 64;
        viewModel.DownloadThreads = 4096;
        viewModel.DownloadSpeedLimitKbps = -5;

        viewModel.SaveCommand.Execute(null);

        Assert.Equal(256, settings.Settings.MaxMemoryMb);
        Assert.Equal(255, settings.Settings.DownloadThreads);
        Assert.Equal(0, settings.Settings.DownloadSpeedLimitKbps);
    }

    [Fact]
    public void Save_AppliesSelectedTheme()
    {
        var theme = new FakeThemeService();
        var viewModel = CreateViewModel(new FakeSettingsService(), theme);
        viewModel.UseDarkTheme = false;

        viewModel.SaveCommand.Execute(null);

        Assert.False(theme.LastAppliedTheme);
        Assert.Equal("设置已保存", viewModel.StatusMessage);
    }

    [Fact]
    public void SelectedSection_ChangesCurrentSection()
    {
        var viewModel = CreateViewModel(new FakeSettingsService());

        viewModel.SelectedSection = viewModel.Sections.Single(section => section.Id == "Link");

        Assert.Equal("联机", viewModel.SelectedSection.Title);
    }
}
