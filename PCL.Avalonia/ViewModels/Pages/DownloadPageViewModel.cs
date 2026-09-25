using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class DownloadPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IVersionManifestService _manifestService;
    private readonly IVersionInstaller _installer;
    private readonly IVersionCatalogService _catalog;
    private readonly IPlatformService _platform;
    private readonly List<DownloadVersionItemViewModel> _allVersions = [];
    private HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cancellationTokenSource;

    public DownloadPageViewModel(
        ISettingsService settingsService,
        IVersionManifestService manifestService,
        IVersionInstaller installer,
        IVersionCatalogService catalog,
        IPlatformService platform)
    {
        _settingsService = settingsService;
        _manifestService = manifestService;
        _installer = installer;
        _catalog = catalog;
        _platform = platform;
    }

    public ObservableCollection<DownloadVersionItemViewModel> Versions { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private DownloadVersionItemViewModel? _selectedVersion;

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

    private bool CanInstall => SelectedVersion is not null && !IsInstalling;

    private bool CanCancel => IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var selected = SelectedVersion;
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
            var progress = new Progress<InstallProgress>(OnInstallProgress);
            var result = await _installer.InstallAsync(
                    selected.Id,
                    selected.Entry,
                    settings.DownloadSource,
                    folder,
                    progress,
                    _cancellationTokenSource.Token)
                .ConfigureAwait(true);
            if (result.Success)
            {
                _installedIds.Add(selected.Id);
                selected.IsInstalled = true;
                StatusMessage = $"已安装 {selected.Id}";
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
            var settings = _settingsService.Load();
            _installedIds = GetInstalledIds(settings);
            var manifest = await _manifestService.GetManifestAsync(settings.DownloadSource);
            _allVersions.Clear();
            foreach (var entry in manifest.Versions
                         .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                         .OrderByDescending(entry => entry.ReleaseTime ?? DateTimeOffset.MinValue)
                         .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase))
            {
                var id = entry.Id!;
                _allVersions.Add(new DownloadVersionItemViewModel(entry, _installedIds.Contains(id)));
            }

            ApplyFilter();
            StatusMessage = $"已获取 {_allVersions.Count} 个版本（{settings.DownloadSource}）";
        }
        catch (Exception ex)
        {
            StatusMessage = "获取版本清单失败：" + ex.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Versions.Clear();
        foreach (var item in _allVersions)
        {
            if (string.IsNullOrWhiteSpace(query)
                || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Versions.Add(item);
            }
        }
    }

    private HashSet<string> GetInstalledIds(AppSettings settings)
    {
        return new HashSet<string>(
            _catalog.Scan(GetMinecraftFolder(settings)).Select(version => version.Id),
            StringComparer.OrdinalIgnoreCase);
    }

    private string GetMinecraftFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
    }

    private void OnInstallProgress(InstallProgress value)
    {
        ProgressText = value.Stage switch
        {
            InstallStage.VersionJson => $"正在下载版本 JSON：{value.ItemName}",
            InstallStage.VersionJar => $"正在下载客户端：{value.ItemName}",
            InstallStage.Libraries => $"正在下载支持库 {value.CompletedItems}/{value.TotalItems}：{value.ItemName}",
            InstallStage.AssetsIndex => $"正在下载资源索引：{value.ItemName}",
            InstallStage.Assets => $"正在下载资源 {value.CompletedItems}/{value.TotalItems}：{value.ItemName}",
            InstallStage.Complete => $"已完成 {value.ItemName}",
            _ => "处理中",
        };
        if (value.TotalItems > 0)
        {
            ProgressPercent = value.CompletedItems * 100.0 / value.TotalItems;
        }
        else if (value.TotalLength is > 0)
        {
            ProgressPercent = value.DownloadFraction * 100;
        }
    }
}

public sealed partial class DownloadVersionItemViewModel : ObservableObject
{
    public DownloadVersionItemViewModel(VersionManifestEntry entry, bool isInstalled)
    {
        Entry = entry;
        Id = entry.Id ?? "";
        Type = entry.Type ?? "release";
        ReleaseTimeText = entry.ReleaseTime?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "";
        IsInstalled = isInstalled;
    }

    public VersionManifestEntry Entry { get; }

    public string Id { get; }

    public string Type { get; }

    public string TypeText => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        _ => Type,
    };

    public string ReleaseTimeText { get; }

    [ObservableProperty]
    private bool _isInstalled;
}
