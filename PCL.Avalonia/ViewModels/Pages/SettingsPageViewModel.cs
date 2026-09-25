using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using System.Collections.ObjectModel;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IJavaListService _javaListService;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IPlatformService platformService,
        IThemeService themeService,
        IJavaListService javaListService)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _javaListService = javaListService;
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
        RefreshJavaList();
    }

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
            foreach (var entry in java.OrderByDescending(j => j.Version))
            {
                JavaEntries.Add(entry);
            }

            SelectedJava = JavaEntries.FirstOrDefault(j =>
                j.Path.Equals(JavaPath, StringComparison.OrdinalIgnoreCase));
            StatusMessage = JavaEntries.Count == 0
                ? "未检测到 Java，请手动指定路径"
                : $"检测到 {JavaEntries.Count} 个 Java";
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
    }

    [RelayCommand]
    private void Save()
    {
        var current = _settingsService.Load();
        _settingsService.Save(current with
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
        });
        _themeService.Apply(UseDarkTheme);
        StatusMessage = "设置已保存";
    }
}

public sealed record DownloadSourceOption(DownloadSource Source, string Label);

public sealed record SettingsSectionOption(string Id, string Title);

public sealed record LinkLatencyModeOption(LinkLatencyMode Mode, string Label);
