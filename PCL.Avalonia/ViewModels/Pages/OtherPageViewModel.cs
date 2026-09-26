using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Platform;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class OtherPageViewModel : ObservableObject
{
    private readonly IOtherToolsService _tools;
    private readonly IFolderOpener _folderOpener;
    private readonly string _gameFolder;
    private readonly string _configPath;

    public OtherPageViewModel(
        ISettingsService settingsService,
        IPlatformService platformService,
        IOtherToolsService otherToolsService,
        IFolderOpener folderOpener)
    {
        var settings = settingsService.Load();
        _gameFolder = string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? platformService.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
        _configPath = platformService.GetConfigDirectory();
        _tools = otherToolsService;
        _folderOpener = folderOpener;

        var info = _tools.GetEnvironmentInfo(_gameFolder, _configPath);
        AppVersion = info.AppVersion;
        Runtime = info.Runtime;
        OperatingSystem = info.OperatingSystem;
        MinecraftFolder = info.MinecraftFolder;
        ConfigDirectory = info.ConfigDirectory;
    }

    [ObservableProperty]
    private string _appVersion = "";

    [ObservableProperty]
    private string _runtime = "";

    [ObservableProperty]
    private string _operatingSystem = "";

    [ObservableProperty]
    private string _minecraftFolder = "";

    [ObservableProperty]
    private string _configDirectory = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private int _garbageFileCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GarbageSizeText))]
    private long _garbageBytes;

    public string GarbageSizeText => FormatBytes(GarbageBytes);

    [RelayCommand]
    private void OpenGameFolder() => OpenFolder(_gameFolder, "游戏目录");

    [RelayCommand]
    private void OpenConfigFolder() => OpenFolder(_configPath, "启动器配置目录");

    [RelayCommand]
    private void ScanGarbage()
    {
        try
        {
            var report = _tools.ScanGarbage([_gameFolder, _configPath]);
            GarbageFileCount = report.FileCount;
            GarbageBytes = report.Bytes;
            StatusMessage = report.FileCount == 0
                ? "没有发现临时文件"
                : $"发现 {report.FileCount} 个临时文件，共 {FormatBytes(report.Bytes)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "扫描临时文件失败：" + ErrorMessageFormatter.Describe(ex);
        }
    }

    [RelayCommand]
    private void CleanGarbage()
    {
        try
        {
            var report = _tools.CleanGarbage([_gameFolder, _configPath]);
            GarbageFileCount = 0;
            GarbageBytes = 0;
            StatusMessage = report.FileCount == 0
                ? "没有需要清理的临时文件"
                : $"已清理 {report.FileCount} 个临时文件，释放 {FormatBytes(report.Bytes)}";
        }
        catch (Exception ex)
        {
            StatusMessage = "清理临时文件失败：" + ErrorMessageFormatter.Describe(ex);
        }
    }

    private void OpenFolder(string path, string label)
    {
        try
        {
            _folderOpener.Open(path);
            StatusMessage = $"已打开{label}：{path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开{label}失败：{ErrorMessageFormatter.Describe(ex)}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
