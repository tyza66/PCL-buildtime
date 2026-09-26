using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class ForgelikeLoaderPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IForgelikeLoaderService _loaderService;
    private readonly IPlatformService _platform;
    private readonly SessionState _session;
    private CancellationTokenSource? _cancellationTokenSource;

    public ForgelikeLoaderPageViewModel(
        ISettingsService settingsService,
        IForgelikeLoaderService loaderService,
        IPlatformService platform,
        SessionState session)
    {
        _settingsService = settingsService;
        _loaderService = loaderService;
        _platform = platform;
        _session = session;
    }

    public ObservableCollection<ForgelikeLoaderVersionItemViewModel> Versions { get; } = [];

    [ObservableProperty]
    private string _gameVersionText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private ForgelikeLoaderVersionItemViewModel? _selectedLoader;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isNeoForgeMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
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

    public string ModeTitle => IsNeoForgeMode ? "NeoForge" : "Forge";

    public bool IsForgeMode => !IsNeoForgeMode;

    private bool CanInstall => SelectedLoader is not null && !IsInstalling;

    private bool CanCancel => IsInstalling;

    private bool CanRefresh => !IsRefreshing && !IsInstalling;

    partial void OnIsNeoForgeModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsForgeMode));
        OnPropertyChanged(nameof(ModeTitle));
        Versions.Clear();
        SelectedLoader = null;
        StatusMessage = "";
    }

    [RelayCommand]
    private void SelectForge() => IsNeoForgeMode = false;

    [RelayCommand]
    private void SelectNeoForge() => IsNeoForgeMode = true;

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
            var progress = new Progress<ForgelikeInstallProgress>(OnInstallProgress);
            var result = await _loaderService.InstallAsync(
                    selected.Version,
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
            StatusMessage = "安装失败：" + ex.Message;
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

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        StatusMessage = "";
        try
        {
            if (!IsNeoForgeMode && string.IsNullOrWhiteSpace(GameVersionText))
            {
                StatusMessage = "请先填写游戏版本";
                return;
            }

            var versions = IsNeoForgeMode
                ? await _loaderService.GetNeoForgeVersionsAsync()
                : await _loaderService.GetForgeVersionsAsync(GameVersionText.Trim());
            Versions.Clear();
            foreach (var version in versions)
            {
                Versions.Add(new ForgelikeLoaderVersionItemViewModel(version));
            }

            SelectedLoader = null;
            var modeName = IsNeoForgeMode ? "NeoForge" : "Forge";
            StatusMessage = $"已获取 {Versions.Count} 个 {modeName} 加载器版本";
        }
        catch (Exception ex)
        {
            StatusMessage = "获取加载器版本失败：" + ex.Message;
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

    private void OnInstallProgress(ForgelikeInstallProgress value)
    {
        ProgressText = value.Stage switch
        {
            ForgelikeInstallStage.BaseVersion => $"正在安装原版版本：{value.ItemName}",
            ForgelikeInstallStage.Installer => $"正在下载安装器：{value.ItemName}",
            ForgelikeInstallStage.Libraries => $"正在下载支持库 {value.CompletedItems}/{value.TotalItems}：{value.ItemName}",
            ForgelikeInstallStage.Injector => $"正在运行安装器：{value.ItemName}",
            ForgelikeInstallStage.Complete => $"已完成 {value.ItemName}",
            _ => "处理中",
        };
        if (value.TotalItems > 0)
        {
            ProgressPercent = value.CompletedItems * 100.0 / value.TotalItems;
        }
    }
}

public sealed partial class ForgelikeLoaderVersionItemViewModel : ObservableObject
{
    public ForgelikeLoaderVersionItemViewModel(ForgelikeLoaderVersion version)
    {
        Version = version;
    }

    public ForgelikeLoaderVersion Version { get; }

    public string VersionText => Version.VersionName;

    public string MinecraftVersionText => Version.GameVersion;

    public string StabilityText => Version.IsBeta ? "测试版" : "稳定版";

    public string CategoryText => Version.Category ?? "installer";
}
