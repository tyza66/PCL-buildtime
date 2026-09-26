using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Platform;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class LaunchPageViewModel : ObservableObject, IPageActivatable
{
    private readonly ISettingsService _settingsService;
    private readonly IJavaService _javaService;
    private readonly IGameLauncher _launcher;
    private readonly IMicrosoftAuthenticationService _microsoftAuthentication;
    private readonly IAccountService _accountService;
    private readonly IVersionManagerService _versionManager;
    private readonly SessionState _session;
    private readonly IUiDispatcher _dispatcher;
    private readonly IVersionCatalogService _catalog;
    private readonly IPlatformService _platform;
    private readonly IFolderOpener _folderOpener;
    private IGameLaunch? _activeLaunch;

    public LaunchPageViewModel(
        ISettingsService settingsService,
        IJavaService javaService,
        IGameLauncher launcher,
        SessionState session,
        IUiDispatcher dispatcher,
        IMicrosoftAuthenticationService microsoftAuthentication,
        IAccountService accountService,
        IVersionManagerService versionManager,
        IVersionCatalogService versionCatalog,
        IPlatformService platformService,
        IFolderOpener folderOpener)
    {
        _settingsService = settingsService;
        _javaService = javaService;
        _launcher = launcher;
        _microsoftAuthentication = microsoftAuthentication;
        _accountService = accountService;
        _versionManager = versionManager;
        _session = session;
        _dispatcher = dispatcher;
        _catalog = versionCatalog;
        _platform = platformService;
        _folderOpener = folderOpener;
        _session.VersionInstalled += OnVersionInstalled;
        _session.PropertyChanged += OnSessionPropertyChanged;
        SelectedVersion = _session.SelectedVersion;
        GameFolderText = ResolveGameFolder();
        UpdateVersionStatus();
        _ = RefreshInstalledAsync();
    }

    /// <summary>切入启动页时重扫一次，装完新版本切回来能立刻看到。</summary>
    public async Task OnActivatedAsync()
    {
        await RefreshInstalledAsync().ConfigureAwait(true);
    }

    public ObservableCollection<LaunchVersionItemViewModel> InstalledVersions { get; } = [];

    [ObservableProperty]
    private LaunchVersionItemViewModel? _selectedInstalledVersion;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _gameFolderText = "";

    [ObservableProperty]
    private string _versionStatus = "";

    partial void OnSelectedInstalledVersionChanged(LaunchVersionItemViewModel? value)
    {
        if (value is null || ReferenceEquals(value.Version, SelectedVersion))
        {
            return;
        }

        SelectedVersion = value.Version;
        _session.SelectedVersion = value.Version;
        UpdateVersionStatus();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    private MinecraftVersion? _selectedVersion;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    private bool _isLaunching;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    private bool CanLaunch => SelectedVersion is not null && !IsLaunching && !IsRunning;

    private bool CanCancel => IsRunning;

    partial void OnSelectedVersionChanged(MinecraftVersion? value)
    {
        UpdateVersionStatus();
    }

    [RelayCommand]
    private async Task RefreshInstalledAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        var settings = _settingsService.Load();
        var folders = ResolveGameFolders(settings);
        GameFolderText = folders.Count == 0
            ? "尚未设置游戏目录"
            : $"游戏目录：{folders[0]}";
        if (folders.Count > 1)
        {
            GameFolderText += $"（另有 {folders.Count - 1} 个目录）";
        }

        IsRefreshing = true;
        try
        {
            var installed = await Task.Run(() =>
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var versions = new List<LaunchVersionItemViewModel>();
                foreach (var folder in folders)
                {
                    foreach (var version in _catalog.Scan(folder))
                    {
                        if (!seen.Add(version.Id))
                        {
                            continue;
                        }

                        versions.Add(new LaunchVersionItemViewModel(
                            version,
                            _versionManager.LoadSettings(folder, version.Id)));
                    }
                }

                return versions;
            }).ConfigureAwait(true);

            var previousId = SelectedVersion?.Id;
            InstalledVersions.Clear();
            foreach (var item in installed)
            {
                InstalledVersions.Add(item);
            }

            try
            {
                SelectedInstalledVersion = InstalledVersions.FirstOrDefault(item =>
                    previousId is null
                        ? ReferenceEquals(item.Version, SelectedVersion)
                        : string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase))
                    ?? InstalledVersions.FirstOrDefault();
            }
            finally
            {
            }

            if (SelectedInstalledVersion is not null)
            {
                // 第一次进来默认选中第一个，方便直接点启动。
                SelectedVersion = SelectedInstalledVersion.Version;
                _session.SelectedVersion = SelectedVersion;
            }

            UpdateVersionStatus();
        }
        catch (Exception ex)
        {
            VersionStatus = "读取已安装版本失败：" + ex.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        var folders = ResolveGameFolders(_settingsService.Load());
        var target = folders.FirstOrDefault(folder => Directory.Exists(folder));
        if (target is null)
        {
            VersionStatus = "游戏目录还不存在：" + (folders.Count > 0 ? folders[0] : "未设置");
            return;
        }

        try
        {
            _folderOpener.Open(target);
        }
        catch (Exception ex)
        {
            VersionStatus = "打开目录失败：" + ex.Message;
        }
    }

    private void OnVersionInstalled(object? sender, string versionId)
    {
        _dispatcher.Post(() => _ = RefreshInstalledAsync());
    }

    private void UpdateVersionStatus()
    {
        VersionStatus = SelectedVersion is null
            ? InstalledVersions.Count == 0
                ? "还没有已安装的版本，先去下载页安装一个"
                : "请选择一个版本"
            : $"当前版本：{SelectedVersion.Id}";
    }

    private List<string> ResolveGameFolders(AppSettings settings)
    {
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallback = string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder.Trim();
        if (fallback.Length > 0 && seen.Add(fallback))
        {
            folders.Add(fallback);
        }

        foreach (var folder in settings.LaunchFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder.Path) && seen.Add(folder.Path.Trim()))
            {
                folders.Add(folder.Path.Trim());
            }
        }

        return folders;
    }

    private string ResolveGameFolder()
    {
        return string.IsNullOrWhiteSpace(_settingsService.Load().MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : _settingsService.Load().MinecraftFolder.Trim();
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        try
        {
            IsLaunching = true;
            StatusMessage = $"正在启动 {version.Id}";
            var account = _session.SelectedAccount;
            if (account?.Type == "microsoft"
                && account.AccessTokenExpiresAt is { } expiresAt
                && expiresAt <= DateTimeOffset.UtcNow)
            {
                StatusMessage = "正版登录已过期，正在刷新令牌…";
                var refreshed = await _microsoftAuthentication.RefreshAsync(
                    account,
                    new Progress<string>(message => _dispatcher.Post(() => StatusMessage = message)));
                if (refreshed is null)
                {
                    LogLine("正版登录令牌已失效，需要重新登录");
                    StatusMessage = "正版登录已失效，请到账号页重新登录";
                    return;
                }

                account = await Task.Run(() => _accountService.AddMicrosoftAccount(refreshed));
                _session.SelectedAccount = account;
            }

            var settings = _settingsService.Load();
            var gameFolder = GetMinecraftFolder(settings);
            var versionSettings = _versionManager.LoadSettings(gameFolder, version.Id);
            settings = settings with
            {
                UserName = account?.Name is { Length: > 0 } accountName
                    ? accountName
                    : settings.UserName,
            };
            var launchSettings = LaunchSettingsMerger.Merge(settings, versionSettings);
            // 版本清单点名要某个 Java 大版本时（如 26.3 要 25）按它挑 Java，否则会默认落到 Java 8。
            var requiredJavaMajor = _catalog.LoadJson(gameFolder, version.Id)?.JavaVersion?.MajorVersion;
            var java = _javaService.ResolveJavaExecutable(launchSettings, requiredJavaMajor);
            if (java is null)
            {
                LogLine("未找到 Java，请先在设置页配置 Java 路径");
                StatusMessage = "未找到 Java";
                return;
            }

            var launchAccount = account;
            var plan = await Task.Run(() => _launcher.BuildLaunchPlan(
                version,
                launchSettings,
                java,
                launchAccount,
                versionSettings));
            LogLine("启动命令：" + plan.CommandLine);
            var launch = _launcher.Launch(plan, new Progress<string>(LogLine));
            _activeLaunch = launch;
            IsRunning = true;
            LogLine($"Java 进程已启动（PID {launch.ProcessId}）");
            StatusMessage = $"正在运行 {version.Id}";
            _ = TrackExitAsync(launch, version.Id);
        }
        catch (Exception ex)
        {
            LogLine("启动失败：" + ex.Message);
            StatusMessage = "启动失败：" + ex.Message;
        }
        finally
        {
            IsLaunching = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        var launch = _activeLaunch;
        if (launch is null)
        {
            return;
        }

        _activeLaunch = null;
        IsRunning = false;
        launch.Kill();
        launch.Dispose();
        LogLine("已请求终止游戏进程");
        StatusMessage = SelectedVersion is null ? "请先选择一个版本" : $"已停止 {SelectedVersion.Id}";
    }

    private async Task TrackExitAsync(IGameLaunch launch, string versionId)
    {
        var exitCode = 0;
        try
        {
            exitCode = await launch.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            exitCode = -1;
        }

        _dispatcher.Post(() =>
        {
            if (!ReferenceEquals(_activeLaunch, launch))
            {
                return;
            }

            _activeLaunch = null;
            LogLine($"游戏进程已退出（退出码 {exitCode}）");
            StatusMessage = $"已退出 {versionId}（退出码 {exitCode}）";
            IsRunning = false;
            launch.Dispose();
        });
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionState.SelectedVersion))
        {
            SelectedVersion = _session.SelectedVersion;
            SyncInstalledSelection();
        }
    }

    private void LogLine(string line)
    {
        _dispatcher.Post(() =>
        {
            LogText = LogText.Length == 0 ? line : LogText + "\n" + line;
        });
    }

    private string GetMinecraftFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder.Trim();
    }

    private void SyncInstalledSelection()
    {
        var selected = InstalledVersions.FirstOrDefault(item =>
            item.Version.Id.Equals(SelectedVersion?.Id, StringComparison.Ordinal));
        if (selected is not null)
        {
            SelectedInstalledVersion = selected;
        }

        UpdateVersionStatus();
    }
}

public sealed partial class LaunchVersionItemViewModel : ObservableObject
{
    public LaunchVersionItemViewModel(MinecraftVersion version, VersionSettings settings)
    {
        Version = version;
        Id = version.Id;
        TypeText = ResolveTypeText(version);
        ReleaseTimeText = version.ReleaseTime == DateTimeOffset.UnixEpoch
            ? ""
            : version.ReleaseTimeText;
        IsFavorite = settings.IsFavorite;
        Description = string.IsNullOrWhiteSpace(settings.Description) ? "" : settings.Description.Trim();
    }

    public MinecraftVersion Version { get; }

    public string Id { get; }

    public string TypeText { get; }

    public string ReleaseTimeText { get; }

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string _description = "";

    /// <summary>有自定义描述就顶掉类型徽标，一眼能认出是哪个整合包实例。</summary>
    public string Subtitle => Description.Length > 0 ? Description : TypeText;

    private static string ResolveTypeText(MinecraftVersion version)
    {
        return version.Loader switch
        {
            LoaderKind.Fabric => "Fabric" + (version.LoaderVersion is { Length: > 0 } v ? $" {v}" : ""),
            LoaderKind.Forge => "Forge" + (version.LoaderVersion is { Length: > 0 } v ? $" {v}" : ""),
            LoaderKind.NeoForge => "NeoForge" + (version.LoaderVersion is { Length: > 0 } v ? $" {v}" : ""),
            LoaderKind.OptiFine => "OptiFine",
            LoaderKind.LiteLoader => "LiteLoader",
            _ => version.Type,
        };
    }
}
