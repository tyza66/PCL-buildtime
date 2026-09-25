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
            },
        };

        var viewModel = new SettingsPageViewModel(settings, new FakePlatformService());

        Assert.Equal("/games/mc", viewModel.MinecraftFolder);
        Assert.Equal("/opt/java/bin/java", viewModel.JavaPath);
        Assert.Equal("Alex", viewModel.UserName);
        Assert.Equal(6144, viewModel.MaxMemoryMb);
        Assert.Equal(DownloadSource.Mojang, viewModel.DownloadSource.Source);
    }

    [Fact]
    public void Constructor_EmptyFolder_UsesPlatformDefault()
    {
        var viewModel = new SettingsPageViewModel(new FakeSettingsService(), new FakePlatformService());

        Assert.Equal("/default/.minecraft", viewModel.MinecraftFolder);
        Assert.Equal("Player", viewModel.UserName);
        Assert.Equal(4096, viewModel.MaxMemoryMb);
    }

    [Fact]
    public void Save_PersistsFields_AndPreservesOtherSettings()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { UseDarkTheme = false, MinecraftFolder = "/old/mc" },
        };
        var viewModel = new SettingsPageViewModel(settings, new FakePlatformService());
        viewModel.MinecraftFolder = "/new/mc";
        viewModel.JavaPath = "/new/java";
        viewModel.UserName = "Steve";
        viewModel.MaxMemoryMb = 8192;
        viewModel.DownloadSource = viewModel.DownloadSources.Single(
            option => option.Source == DownloadSource.Mojang);

        viewModel.SaveCommand.Execute(null);

        Assert.Equal("/new/mc", settings.Settings.MinecraftFolder);
        Assert.Equal("/new/java", settings.Settings.JavaPath);
        Assert.Equal("Steve", settings.Settings.UserName);
        Assert.Equal(8192, settings.Settings.MaxMemoryMb);
        Assert.Equal(DownloadSource.Mojang, settings.Settings.DownloadSource);
        Assert.False(settings.Settings.UseDarkTheme);
        Assert.Equal("设置已保存", viewModel.StatusMessage);
    }
}
