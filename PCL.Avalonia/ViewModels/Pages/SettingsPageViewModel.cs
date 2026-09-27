using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Runtime.InteropServices;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;
using System.Collections.ObjectModel;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IJavaListService _javaListService;
    private readonly IUiDispatcher _dispatcher;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IPlatformService platformService,
        IThemeService themeService,
        IJavaListService javaListService,
        IUiDispatcher dispatcher)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _javaListService = javaListService;
        _dispatcher = dispatcher;
        var settings = settingsService.Load();
        MinecraftFolder = string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? platformService.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
        JavaPath = settings.JavaPath;
        UserName = string.IsNullOrWhiteSpace(settings.UserName) ? "Player" : settings.UserName;
        MaxMemoryMb = settings.MaxMemoryMb;
        DownloadSource = DownloadSources.First(option => option.Source == settings.DownloadSource);
        JvmArguments = settings.JvmArguments;
        GameArguments = settings.GameArguments;
        UseDarkTheme = settings.UseDarkTheme;
        DownloadThreads = settings.DownloadThreads;
        DownloadSpeedLimitKbps = settings.DownloadSpeedLimitKbps;
        OptimizeMemoryBeforeLaunch = settings.OptimizeMemoryBeforeLaunch;
        LinkLatencyMode = LinkLatencyModes.First(option => option.Mode == settings.LinkLatencyMode);
        LinkCustomPeer = settings.LinkCustomPeer;
        VersionIsolationMode = VersionIsolationDefaults.First(option => option.Value == settings.VersionIsolationDefault);
        _lastSaved = Snapshot();
        RefreshJavaList();
    }

    /// <summary>
    /// 设置改动必须立刻落盘：以前只有点“保存设置”才写文件，用户改完游戏目录直接去启动，
    /// 读到的还是空目录，于是误报“未设置游戏目录”。
    /// </summary>
    private AppSettings _lastSaved;

    public IReadOnlyList<SettingsSectionOption> Sections { get; } =
    [
        new("Launch", "启动"),
        new("Download", "下载"),
        new("Personalization", "个性化"),
        new("Link", "联机"),
    ];

    public IReadOnlyList<DownloadSourceOption> DownloadSources { get; } =
    [
        new(PCL.Avalonia.Services.DownloadSource.Bmclapi, "BMCLAPI（推荐）"),
        new(PCL.Avalonia.Services.DownloadSource.Mojang, "Mojang 官方"),
    ];

    public IReadOnlyList<LinkLatencyModeOption> LinkLatencyModes { get; } =
    [
        new(PCL.Avalonia.Services.LinkLatencyMode.PreferredDirect, "优先直连"),
        new(PCL.Avalonia.Services.LinkLatencyMode.PreferredLowLatency, "优先低延迟"),
    ];

    [ObservableProperty]
    private SettingsSectionOption _selectedSection = new("Launch", "启动");

    [ObservableProperty]
    private string _minecraftFolder = "";

    [ObservableProperty]
    private string _javaPath = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private int _maxMemoryMb = 4096;

    [ObservableProperty]
    private string _jvmArguments = "";

    [ObservableProperty]
    private string _gameArguments = "";

    [ObservableProperty]
    private DownloadSourceOption _downloadSource = new(PCL.Avalonia.Services.DownloadSource.Bmclapi, "BMCLAPI（推荐）");

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _useDarkTheme = true;

    [ObservableProperty]
    private int _downloadThreads = 64;

    [ObservableProperty]
    private int _downloadSpeedLimitKbps;

    [ObservableProperty]
    private bool _optimizeMemoryBeforeLaunch = true;

    [ObservableProperty]
    private LinkLatencyModeOption _linkLatencyMode =
        new(PCL.Avalonia.Services.LinkLatencyMode.PreferredDirect, "优先直连");

    [ObservableProperty]
    private string _linkCustomPeer = "";

    public ObservableCollection<JavaInfo> JavaEntries { get; } = new();

    [ObservableProperty]
    private JavaInfo? _selectedJava;

    [ObservableProperty]
    private bool _isScanningJava;

    [RelayCommand]
    private void RefreshJavaList()
    {
        IsScanningJava = true;
        try
        {
            var java = _javaListService.Scan();
            JavaEntries.Clear();
            // 按主版本号排，不能按 Version 字符串排：字符串序会把 "9.0.4" 排到 "21.0.1" 前面。
            foreach (var entry in java
                .OrderByDescending(j => j.MajorVersion)
                .ThenByDescending(j => j.Version, StringComparer.OrdinalIgnoreCase))
            {
                JavaEntries.Add(entry);
            }

            SelectedJava = JavaEntries.FirstOrDefault(j =>
                j.Path.Equals(JavaPath, StringComparison.OrdinalIgnoreCase));
            StatusMessage = JavaEntries.Count == 0
                ? "未检测到 Java，请手动指定路径"
                : $"检测到 {JavaEntries.Count} 个 Java";
            WarnIfJavaArchitectureMismatch(SelectedJava);
        }
        finally
        {
            IsScanningJava = false;
        }
    }

    [RelayCommand]
    private void SelectJava()
    {
        var java = SelectedJava;
        if (java is null)
        {
            return;
        }

        JavaPath = java.Path;
        StatusMessage = $"已选择 Java {java.Version} ({java.Architecture})";
        WarnIfJavaArchitectureMismatch(java);
    }

    /// <summary>
    /// 当前 Java 架构和本机不一致时把警告接在状态栏后面：架构不符在启动时只表现成
    /// "卡"或"莫名崩溃"，不在这里说清用户永远想不到是 Java 装错了架构。
    /// </summary>
    private void WarnIfJavaArchitectureMismatch(JavaInfo? java)
    {
        var warning = JavaHints.DescribeArchitectureMismatch(
            java?.Architecture,
            RuntimeInformation.OSArchitecture);
        if (warning is not null)
        {
            StatusMessage += "。" + warning;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _lastSaved = Snapshot();
        Commit(_lastSaved);
        StatusMessage = "设置已保存";
    }

    /// <summary>
    /// 把当前界面值按与“保存设置”一致的收敛规则打成一条 AppSettings，自动落盘和手动保存都从这里出。
    /// </summary>
    private AppSettings Snapshot()
    {
        var current = _settingsService.Load();
        return current with
        {
            MinecraftFolder = MinecraftFolder.Trim(),
            JavaPath = JavaPath.Trim(),
            UserName = string.IsNullOrWhiteSpace(UserName) ? "Player" : UserName.Trim(),
            MaxMemoryMb = Math.Max(256, MaxMemoryMb),
            DownloadSource = DownloadSource.Source,
            JvmArguments = JvmArguments.Trim(),
            GameArguments = GameArguments.Trim(),
            UseDarkTheme = UseDarkTheme,
            DownloadThreads = Math.Clamp(DownloadThreads, 1, 255),
            DownloadSpeedLimitKbps = Math.Max(0, DownloadSpeedLimitKbps),
            OptimizeMemoryBeforeLaunch = OptimizeMemoryBeforeLaunch,
            LinkLatencyMode = LinkLatencyMode.Mode,
            LinkCustomPeer = LinkCustomPeer.Trim(),
            VersionIsolationDefault = VersionIsolationMode.Value,
        };
    }

    private void Commit(AppSettings settings)
    {
        _settingsService.Save(settings);
        _themeService.Apply(settings.UseDarkTheme);
    }

    // 每个要持久化的字段改动都走防抖自动落盘：用户改完即生效，不再依赖点“保存设置”。
    partial void OnMinecraftFolderChanged(string value) => ScheduleAutoSave();
    partial void OnJavaPathChanged(string value) => ScheduleAutoSave();
    partial void OnUserNameChanged(string value) => ScheduleAutoSave();
    partial void OnMaxMemoryMbChanged(int value) => ScheduleAutoSave();
    partial void OnDownloadSourceChanged(DownloadSourceOption value) => ScheduleAutoSave();
    partial void OnJvmArgumentsChanged(string value) => ScheduleAutoSave();
    partial void OnGameArgumentsChanged(string value) => ScheduleAutoSave();
    partial void OnUseDarkThemeChanged(bool value) => ScheduleAutoSave();
    partial void OnDownloadThreadsChanged(int value) => ScheduleAutoSave();
    partial void OnDownloadSpeedLimitKbpsChanged(int value) => ScheduleAutoSave();
    partial void OnOptimizeMemoryBeforeLaunchChanged(bool value) => ScheduleAutoSave();
    public IReadOnlyList<VersionIsolationOption> VersionIsolationDefaults { get; } =
    [
        new(VersionIsolationDefault.Off, "关闭",
            "所有版本共用同一份存档、Mod、资源包。多个装了 Mod 的版本共存时可能互相冲突。"),
        new(VersionIsolationDefault.ModdableOnly, "隔离可安装 Mod 的版本",
            "Forge、Fabric 等可安装 Mod 的版本互相独立，避免 Mod 冲突；原版等其他版本不被隔离。"),
        new(VersionIsolationDefault.SnapshotOnly, "隔离非正式版",
            "把快照、预发布版、远古版本、愚人节版本与其他版本隔离开。"),
        new(VersionIsolationDefault.SnapshotAndModdable, "隔离可安装 Mod 的版本与非正式版",
            "可安装 Mod 的版本与快照、预发布版、远古版本、愚人节版本都会被隔离。"),
        new(VersionIsolationDefault.All, "隔离所有版本",
            "每个版本的存档、Mod、资源包都独立。不同原版版本间的存档将不能共用。"),
    ];

    /// <summary>新安装版本默认的版本隔离策略；已有版本可在版本页单独调整。</summary>
    [ObservableProperty]
    private VersionIsolationOption _versionIsolationMode = new(VersionIsolationDefault.All, "隔离所有版本", "");

    partial void OnLinkLatencyModeChanged(LinkLatencyModeOption value) => ScheduleAutoSave();
    partial void OnLinkCustomPeerChanged(string value) => ScheduleAutoSave();
    partial void OnVersionIsolationModeChanged(VersionIsolationOption value) => ScheduleAutoSave();

    private void ScheduleAutoSave()
    {
        // 记一份已落盘快照，值没实质变化就不重复写盘；防抖避免每敲一个字都保存一次。
        _dispatcher.Debounce("settings-autosave", TimeSpan.FromMilliseconds(400), () =>
        {
            var snapshot = Snapshot();
            if (snapshot == _lastSaved)
            {
                return;
            }

            _lastSaved = snapshot;
            Commit(snapshot);
            // 自动落盘是静默的，给行反馈用户才知道改动已经生效，不用疑心要不要再点保存。
            StatusMessage = $"已自动保存 {DateTime.Now:HH:mm:ss}";
        });
    }
}

public sealed record DownloadSourceOption(DownloadSource Source, string Label);

public sealed record SettingsSectionOption(string Id, string Title);

public sealed record LinkLatencyModeOption(LinkLatencyMode Mode, string Label);

public sealed record VersionIsolationOption(VersionIsolationDefault Value, string Label, string Description);
