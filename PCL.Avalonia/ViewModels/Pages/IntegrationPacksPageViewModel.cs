using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class IntegrationPacksPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ICurseForgeModpackService _modpackService;
    private readonly IModpackInstallerService _modpackInstaller;
    private readonly IPlatformService _platform;
    private readonly IJavaListService _javaList;
    private CancellationTokenSource? _cancellationTokenSource;

    public IntegrationPacksPageViewModel(
        ISettingsService settingsService,
        ICurseForgeModpackService modpackService,
        IModpackInstallerService modpackInstaller,
        IPlatformService platform,
        IJavaListService javaListService)
    {
        _settingsService = settingsService;
        _modpackService = modpackService;
        _modpackInstaller = modpackInstaller;
        _platform = platform;
        _javaList = javaListService;
        _bestJavaMajor = JavaHints.BestMajor(_javaList.Scan());
    }

    public ObservableCollection<IntegrationPackItemViewModel> Projects { get; } = [];

    public IReadOnlyList<string> GameVersions { get; } =
        ["1.21.4", "1.21", "1.20.1", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2"];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _gameVersion = "1.20.1";

    private int? _bestJavaMajor;

    /// <summary>
    /// 按所选游戏版本给出 Java 需求提示，顺带说清本机现在的 Java 装不装得了：
    /// 识别不了的版本返回空串（界面上的提示行自动隐藏），Java 缺失或版本不够时
    /// 直接把安装步骤写进提示，别等整合包下完才发现启动不了。
    /// </summary>
    public string JavaHintText
    {
        get
        {
            if (MinecraftJavaRequirement.GetRequiredMajor(GameVersion) is not { } required)
            {
                return "";
            }

            if (_bestJavaMajor is null)
            {
                return $"{GameVersion} 需要 Java {required}，但本机没有检测到 Java。"
                    + $"请先安装 Java {required}，再到设置页指定路径，否则装完也启动不了";
            }

            return _bestJavaMajor < required
                ? $"{GameVersion} 需要 Java {required}，当前最高只检测到 Java {_bestJavaMajor}。"
                  + $"请先安装 Java {required} 再下载，否则装完也启动不了"
                : $"{GameVersion} 需要 Java {required}，当前 Java {_bestJavaMajor} 满足要求";
        }
    }

    /// <summary>Java 缺失或版本不够时提示行转红，和下载页的警告样式保持一致。</summary>
    public bool JavaHintIsWarning
    {
        get
        {
            if (MinecraftJavaRequirement.GetRequiredMajor(GameVersion) is not { } required)
            {
                return false;
            }

            return _bestJavaMajor is null || _bestJavaMajor < required;
        }
    }

    partial void OnGameVersionChanged(string value)
    {
        // 换版本时顺手再扫一次：用户可能刚在设置页装好 Java，提示要跟着变。
        _bestJavaMajor = JavaHints.BestMajor(_javaList.Scan());
        OnPropertyChanged(nameof(JavaHintText));
        OnPropertyChanged(nameof(JavaHintIsWarning));
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressPercent;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            var results = await _modpackService.SearchAsync(SearchText);
            Projects.Clear();
            foreach (var project in results)
            {
                Projects.Add(new IntegrationPackItemViewModel(project, DownloadAsync, InstallModpackAsync));
            }

            StatusMessage = $"找到 {Projects.Count} 个整合包（{GameVersion}）";
        }
        catch (Exception ex)
        {
            StatusMessage = "搜索失败：" + ErrorMessageFormatter.Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(IntegrationPackItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressPercent = 0;
        ProgressText = "";
        _cancellationTokenSource = new CancellationTokenSource();
        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);
            await _modpackService.InstallAsync(
                item.Project,
                GameVersion,
                folder,
                progress,
                _cancellationTokenSource.Token);
            item.IsDownloaded = true;
            StatusMessage = $"已下载 {item.Title}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "下载已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = "下载失败：" + ErrorMessageFormatter.Describe(ex);
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private async Task InstallModpackAsync(IntegrationPackItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressPercent = 0;
        ProgressText = "";
        _cancellationTokenSource = new CancellationTokenSource();
        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var downloadProgress = new Progress<DownloadProgress>(OnDownloadProgress);
            var zipPath = await _modpackService.InstallAsync(
                item.Project,
                GameVersion,
                folder,
                downloadProgress,
                _cancellationTokenSource.Token);
            item.IsDownloaded = true;

            var installProgress = new Progress<ModpackInstallProgress>(OnInstallProgress);
            var result = await _modpackInstaller.InstallAsync(
                zipPath,
                folder,
                settings.DownloadSource,
                installProgress,
                _cancellationTokenSource.Token);
            item.IsInstalled = result.Success;
            StatusMessage = result.Success
                ? $"已安装整合包 {result.Name}（原版 {result.MinecraftVersion}，Mod {result.InstalledMods.Count} 个）"
                : $"安装完成但有 {result.Errors.Count} 个问题：{string.Join("；", result.Errors.Take(3))}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "安装已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = "安装失败：" + ErrorMessageFormatter.Describe(ex);
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    private string GetMinecraftFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
    }

    private void OnDownloadProgress(DownloadProgress value)
    {
        if (value.TotalLength is > 0)
        {
            ProgressPercent = value.Fraction * 100;
            ProgressText = $"下载中 {value.Received / (1024.0 * 1024.0):F1} MB / {value.TotalLength.Value / (1024.0 * 1024.0):F1} MB";
        }
        else
        {
            ProgressText = $"下载中 {value.Received / (1024.0 * 1024.0):F1} MB";
        }
    }

    private void OnInstallProgress(ModpackInstallProgress value)
    {
        switch (value.Stage)
        {
            case ModpackInstallStage.ReadingManifest:
                ProgressPercent = 5;
                ProgressText = value.ItemName is null
                    ? "读取整合包..."
                    : $"读取整合包 {value.ItemName}";
                break;
            case ModpackInstallStage.BaseVersion:
                ProgressPercent = 10;
                ProgressText = $"准备原版 {value.ItemName}...";
                break;
            case ModpackInstallStage.Mods:
                ProgressPercent = value.TotalItems == 0 ? 50 : 10 + 80.0 * value.CompletedItems / value.TotalItems;
                ProgressText = value.TotalItems == 0
                    ? "检查 Mod"
                    : $"安装 Mod {value.CompletedItems}/{value.TotalItems}";
                break;
            case ModpackInstallStage.Overrides:
                ProgressPercent = 92;
                ProgressText = "写入整合包文件...";
                break;
            case ModpackInstallStage.Complete:
                ProgressPercent = 100;
                ProgressText = "安装完成";
                break;
        }
    }
}

public sealed partial class IntegrationPackItemViewModel : ObservableObject
{
    private readonly Func<IntegrationPackItemViewModel, Task> _download;
    private readonly Func<IntegrationPackItemViewModel, Task> _install;

    public IntegrationPackItemViewModel(
        CurseForgeProject project,
        Func<IntegrationPackItemViewModel, Task> download,
        Func<IntegrationPackItemViewModel, Task> install)
    {
        Project = project;
        Title = project.Name;
        Description = project.Summary;
        AuthorText = project.Authors.Count > 0
            ? project.Authors[0].Name
            : (string.IsNullOrWhiteSpace(project.Slug) ? project.Id.ToString() : project.Slug);
        DownloadsText = FormatDownloads(project.DownloadCount);
        CategoriesText = string.Join(" · ", project.Categories);
        _download = download;
        _install = install;
    }

    public CurseForgeProject Project { get; }

    public string Title { get; }

    public string Description { get; }

    public string AuthorText { get; }

    public string DownloadsText { get; }

    public string CategoriesText { get; }

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    private bool _isInstalled;

    [RelayCommand]
    private Task Download() => _download(this);

    [RelayCommand]
    private Task Install() => _install(this);

    private static string FormatDownloads(int downloads)
    {
        if (downloads >= 1_000_000)
        {
            return $"{downloads / 1_000_000.0:F1}M 下载";
        }

        if (downloads >= 1_000)
        {
            return $"{downloads / 1_000.0:F1}K 下载";
        }

        return $"{downloads} 下载";
    }
}
