using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class FabricLoaderPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFabricLoaderService _loaderService;
    private readonly IPlatformService _platform;
    private readonly IJavaListService _javaList;
    private readonly SessionState _session;
    private CancellationTokenSource? _cancellationTokenSource;

    public FabricLoaderPageViewModel(
        ISettingsService settingsService,
        IFabricLoaderService loaderService,
        IPlatformService platform,
        IJavaListService javaListService,
        SessionState session)
    {
        _settingsService = settingsService;
        _loaderService = loaderService;
        _platform = platform;
        _javaList = javaListService;
        _session = session;
        _bestJavaMajor = JavaHints.BestMajor(_javaList.Scan());
    }

    public ObservableCollection<FabricLoaderVersionItemViewModel> Versions { get; } = [];

    [ObservableProperty]
    private string _gameVersionText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private FabricLoaderVersionItemViewModel? _selectedLoader;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusMessage = "";

    private int? _bestJavaMajor;

    /// <summary>
    /// Fabric 的列表项只说了"要 Java N"，这里再补一句本机装没装：
    /// 选中加载器时用它接口给的 minJavaVersion（精确），没选中时回落到
    /// Mojang 的「游戏版本 → Java」对应关系。没有或版本不够就把下一步写进提示。
    /// </summary>
    public string JavaHintText
    {
        get
        {
            if (RequiredJavaMajor() is not { } required)
            {
                return "";
            }

            if (_bestJavaMajor is null)
            {
                return $"该加载器需要 Java {required}，但本机没有检测到 Java。"
                    + $"请先安装 Java {required}：设置页可一键安装或手动指定路径，否则装完也启动不了";
            }

            return _bestJavaMajor < required
                ? $"该加载器需要 Java {required}，当前最高只检测到 Java {_bestJavaMajor}。"
                  + $"请先安装 Java {required} 再下载，否则装完也启动不了"
                : $"该加载器需要 Java {required}，当前 Java {_bestJavaMajor} 满足要求";
        }
    }

    /// <summary>Java 缺失或版本不够时提示行转红，和整合包、下载页的警告样式一致。</summary>
    public bool JavaHintIsWarning
    {
        get
        {
            if (RequiredJavaMajor() is not { } required)
            {
                return false;
            }

            return _bestJavaMajor is null || _bestJavaMajor < required;
        }
    }

    private int? RequiredJavaMajor()
    {
        if (SelectedLoader?.Version.MinJavaVersion is int minJava && minJava > 0)
        {
            return minJava;
        }

        return MinecraftJavaRequirement.GetRequiredMajor(GameVersionText);
    }

    private void RefreshJavaHint()
    {
        _bestJavaMajor = JavaHints.BestMajor(_javaList.Scan());
        OnPropertyChanged(nameof(JavaHintText));
        OnPropertyChanged(nameof(JavaHintIsWarning));
    }

    partial void OnGameVersionTextChanged(string value) => RefreshJavaHint();

    partial void OnSelectedLoaderChanged(FabricLoaderVersionItemViewModel? value)
    {
        OnPropertyChanged(nameof(JavaHintText));
        OnPropertyChanged(nameof(JavaHintIsWarning));
    }

    private bool CanInstall => SelectedLoader is not null && !IsInstalling;

    private bool CanCancel => IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var selected = SelectedLoader;
        if (selected is null)
        {
            return;
        }

        IsInstalling = true;
        IsProgressVisible = true;
        ProgressPercent = 0;
        ProgressText = "";
        _cancellationTokenSource = new CancellationTokenSource();
        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var progress = new Progress<FabricInstallProgress>(OnInstallProgress);
            var result = await _loaderService.InstallAsync(
                    GameVersionText.Trim(),
                    selected.Version.Version,
                    folder,
                    settings.DownloadSource,
                    progress,
                    _cancellationTokenSource.Token)
                .ConfigureAwait(true);
            if (result.Success)
            {
                StatusMessage = $"已安装 {result.VersionId}";
                _session.NotifyVersionInstalled(result.VersionId);
            }
            else
            {
                StatusMessage = $"安装未完成：{string.Join("；", result.Errors.Take(5))}";
            }
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
            IsInstalling = false;
            IsProgressVisible = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing || IsInstalling)
        {
            return;
        }

        IsRefreshing = true;
        StatusMessage = "";
        try
        {
            if (string.IsNullOrWhiteSpace(GameVersionText))
            {
                StatusMessage = "请先填写游戏版本";
                return;
            }

            var versions = await _loaderService.GetVersionsAsync(GameVersionText.Trim());
            Versions.Clear();
            foreach (var version in versions)
            {
                Versions.Add(new FabricLoaderVersionItemViewModel(version));
            }

            SelectedLoader = null;
            StatusMessage = $"已获取 {Versions.Count} 个 Fabric 加载器版本";
        }
        catch (Exception ex)
        {
            StatusMessage = "获取加载器版本失败：" + ErrorMessageFormatter.Describe(ex);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private string GetMinecraftFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
    }

    private void OnInstallProgress(FabricInstallProgress value)
    {
        ProgressText = value.Stage switch
        {
            FabricInstallStage.BaseVersion => $"正在安装原版版本：{value.ItemName}",
            FabricInstallStage.ProfileJson => $"正在下载 Fabric 版本资料：{value.ItemName}",
            FabricInstallStage.Libraries => CountedProgressText.Format("正在下载支持库", value.CompletedItems, value.TotalItems, value.ItemName),
            FabricInstallStage.Complete => $"已完成 {value.ItemName}",
            _ => "处理中",
        };
        if (value.TotalItems > 0)
        {
            ProgressPercent = value.CompletedItems * 100.0 / value.TotalItems;
        }
    }
}

public sealed partial class FabricLoaderVersionItemViewModel : ObservableObject
{
    public FabricLoaderVersionItemViewModel(FabricLoaderVersion version)
    {
        Version = version;
    }

    public FabricLoaderVersion Version { get; }

    public string VersionText => Version.Version;

    public string MinecraftVersionText => Version.MinecraftVersion;

    public string StabilityText => Version.IsStable ? "稳定版" : "测试版";

    public string JavaText => Version.MinJavaVersion > 0 ? $"需要 Java {Version.MinJavaVersion}" : "";
}
