using System.Runtime.InteropServices;
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

    private sealed class FakeJavaListService : IJavaListService
    {
        private readonly List<JavaInfo> _infos;

        public FakeJavaListService(params JavaInfo[] infos) => _infos = infos.ToList();

        public IReadOnlyList<JavaInfo> Scan() => _infos;

        public JavaInfo? GetJava(string path) => null;

        public void Refresh()
        {
        }
    }

    private sealed class NoopDispatcher : IUiDispatcher
    {
        public void Post(Action action)
        {
        }

        public void Debounce(string key, TimeSpan delay, Action action)
        {
        }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public void Debounce(string key, TimeSpan delay, Action action) => action();
    }

    private static SettingsPageViewModel CreateViewModel(
        FakeSettingsService settings,
        FakeThemeService? theme = null,
        params JavaInfo[] javas)
    {
        return new SettingsPageViewModel(
            settings,
            new FakePlatformService(),
            theme ?? new FakeThemeService(),
            new FakeJavaListService(javas),
            new NoopDispatcher());
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

    [Fact]
    public void RefreshJavaList_WarnsWhenSelectedJavaArchitectureMismatchesSystem()
    {
        // 反过来构造成与本机芯片不一致的架构：x64 机器上放 arm64 的 Java，arm64 机器上放 x64 的。
        var foreign = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "x64" : "arm64";
        // JavaPath 要指向这一条，扫描后它才会是 SelectedJava，警告才有落脚点。
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { JavaPath = "/opt/java/bin/java" },
        };
        var viewModel = CreateViewModel(settings, javas:
            new JavaInfo("/opt/java/bin/java", "21.0.2", foreign, 21, true));

        viewModel.RefreshJavaListCommand.Execute(null);

        Assert.Contains("架构", viewModel.StatusMessage);
        Assert.Contains(foreign, viewModel.StatusMessage);
        Assert.Contains("brew install openjdk", viewModel.StatusMessage);
    }

    [Fact]
    public void RefreshJavaList_KeepsQuietWhenSelectedJavaArchitectureMatchesSystem()
    {
        var native = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { JavaPath = "/opt/java/bin/java" },
        };
        var viewModel = CreateViewModel(settings, javas:
            new JavaInfo("/opt/java/bin/java", "21.0.2", native, 21, true));

        viewModel.RefreshJavaListCommand.Execute(null);

        Assert.DoesNotContain("架构", viewModel.StatusMessage);
    }

    [Fact]
    public void RefreshJavaList_OrdersByMajorVersion_NotStringOrder()
    {
        // 按 Version 字符串排会把 "9.0.4" 排到 "21.0.1" 前面，选 Java 的用户看得别扭。
        var viewModel = CreateViewModel(new FakeSettingsService(), javas:
        [
            new JavaInfo("/jdk9/bin/java", "9.0.4", "x64", 9, true),
            new JavaInfo("/jdk21/bin/java", "21.0.1", "aarch64", 21, true),
            new JavaInfo("/jdk8/bin/java", "1.8.0_392", "x64", 8, true),
        ]);

        viewModel.RefreshJavaListCommand.Execute(null);

        Assert.Equal(21, viewModel.JavaEntries[0].MajorVersion);
        Assert.Equal(9, viewModel.JavaEntries[1].MajorVersion);
        Assert.Equal(8, viewModel.JavaEntries[2].MajorVersion);
    }

    [Fact]
    public void ChangingSetting_Autosaves_AndReportsTimestamp()
    {
        var settings = new FakeSettingsService();
        var viewModel = new SettingsPageViewModel(
            settings,
            new FakePlatformService(),
            new FakeThemeService(),
            new FakeJavaListService(),
            new ImmediateDispatcher());

        viewModel.MaxMemoryMb = 8192;

        // 防抖回调立即执行：改动应当已经落盘，状态行还要告诉用户保存过了。
        Assert.Equal(8192, settings.Settings.MaxMemoryMb);
        Assert.Contains("已自动保存", viewModel.StatusMessage);
    }
}
