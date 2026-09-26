using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class FabricLoaderPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFabricLoaderService _loaderService;
    private readonly IPlatformService _platform;
    private readonly SessionState _session;
    private CancellationTokenSource? _cancellationTokenSource;

    public FabricLoaderPageViewModel(
        ISettingsService settingsService,
        IFabricLoaderService loaderService,
        IPlatformService platform,
        SessionState session)
    {
        _settingsService = settingsService;
        _loaderService = loaderService;
        _platform = platform;
        _session = session;
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
            FabricInstallStage.Libraries => $"正在下载支持库 {value.CompletedItems}/{value.TotalItems}：{value.ItemName}",
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
