using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class DownloadPageViewModel : ObservableObject, IPageActivatable
{
    private readonly ISettingsService _settingsService;
    private readonly IVersionManifestService _manifestService;
    private readonly IVersionInstaller _installer;
    private readonly IVersionCatalogService _catalog;
    private readonly IPlatformService _platform;
    private readonly SessionState _session;
    private readonly IVersionJavaInfoService _javaInfo;
    private readonly IJavaListService _javaList;
    private readonly List<DownloadVersionItemViewModel> _allVersions = [];
    private HashSet<string> _installedIds = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationTokenSource? _javaLookupCts;
    private DateTimeOffset _lastRefreshedAt;

    /// <summary>列表超过这个时长才在切入页面时重新拉取，避免每次切换都打一次清单接口。</summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);

    public DownloadPageViewModel(
        ISettingsService settingsService,
        IVersionManifestService manifestService,
        IVersionInstaller installer,
        IVersionCatalogService catalog,
        IPlatformService platform,
        SessionState session,
        IVersionJavaInfoService javaInfo,
        IJavaListService javaList)
    {
        _settingsService = settingsService;
        _manifestService = manifestService;
        _installer = installer;
        _catalog = catalog;
        _platform = platform;
        _session = session;
        _javaInfo = javaInfo;
        _javaList = javaList;
    }

    public async Task OnActivatedAsync()
    {
        if (IsRefreshing || IsInstalling)
        {
            return;
        }

        if (_allVersions.Count > 0 && DateTimeOffset.UtcNow - _lastRefreshedAt < RefreshWindow)
        {
            return;
        }

        await RefreshAsync();
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

    /// <summary>选中版本的 Java 要求行：装之前就说清要哪个 Java，别等启动才报错。</summary>
    [ObservableProperty]
    private string _javaRequirementText = "";

    [ObservableProperty]
    private bool _javaRequirementIsWarning;

    private bool CanInstall => SelectedVersion is not null && !IsInstalling;

    private bool CanCancel => IsInstalling;

    partial void OnSelectedVersionChanged(DownloadVersionItemViewModel? value)
    {
        _ = LoadJavaRequirementAsync(value);
    }

    /// <summary>
    /// 切选择时读一次版本 JSON 的 javaVersion.majorVersion，再和本机已装 Java 比一比。
    /// 用户来回点点是很常见的，用一把取消令牌把过期的请求结果丢掉，只留最后一次选中版本的结论。
    /// </summary>
    private async Task LoadJavaRequirementAsync(DownloadVersionItemViewModel? item)
    {
        _javaLookupCts?.Cancel();
        _javaLookupCts?.Dispose();
        _javaLookupCts = null;

        if (item is null)
        {
            JavaRequirementText = "";
            JavaRequirementIsWarning = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _javaLookupCts = cts;
        try
        {
            var settings = _settingsService.Load();
            var required = await _javaInfo
                .GetRequiredJavaMajorAsync(settings.DownloadSource, item.Entry, item.Id, cts.Token)
                .ConfigureAwait(true);
            if (cts.IsCancellationRequested || !ReferenceEquals(SelectedVersion, item))
            {
                return;
            }

            ApplyJavaRequirement(item, required);
        }
        catch (OperationCanceledException)
        {
            // 用户换了选择，这次查询的结果没人要了。
        }
        catch (Exception)
        {
            // 拿不到 Java 要求只是少一句提示，不该在状态栏报错吓用户。
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedVersion, item))
            {
                JavaRequirementText = $"{item.Id} 的 Java 要求暂时获取失败，安装前可到启动页确认";
                JavaRequirementIsWarning = false;
            }
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_javaLookupCts, cts))
            {
                _javaLookupCts = null;
            }
        }
    }

    private void ApplyJavaRequirement(DownloadVersionItemViewModel item, int? requiredMajor)
    {
        if (requiredMajor is not { } required)
        {
            JavaRequirementText = $"{item.Id} 未提供 Java 要求信息，安装后可在启动页查看";
            JavaRequirementIsWarning = false;
            return;
        }

        var best = _javaList.Scan()
            .Where(java => java.IsValid && java.MajorVersion > 0)
            .OrderBy(java => java.MajorVersion)
            .LastOrDefault();

        if (best is null)
        {
            JavaRequirementIsWarning = true;
            JavaRequirementText =
                $"{item.Id} 需要 Java {required}，但本机没有检测到 Java。"
                + $"请先安装 Java {required}，再到设置页指定路径，否则装完也启动不了";
            return;
        }

        JavaRequirementIsWarning = best.MajorVersion < required;
        JavaRequirementText = best.MajorVersion < required
            ? $"{item.Id} 需要 Java {required}，当前最高只检测到 Java {best.MajorVersion}。"
              + $"请到设置页更换或安装 Java {required}"
            : $"{item.Id} 需要 Java {required}，当前 Java {best.MajorVersion} 满足要求";
    }

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
                _session.NotifyVersionInstalled(selected.Id);
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
        JavaRequirementText = "";
        JavaRequirementIsWarning = false;
        try
        {
            var settings = _settingsService.Load();
            _installedIds = GetInstalledIds(settings);
            var manifest = await _manifestService.GetManifestAsync(settings.DownloadSource);
            _lastRefreshedAt = DateTimeOffset.UtcNow;
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
            StatusMessage = "获取版本清单失败：" + ErrorMessageFormatter.Describe(ex);
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
