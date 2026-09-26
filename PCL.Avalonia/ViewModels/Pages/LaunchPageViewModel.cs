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
    private readonly IJavaListService _javaListService;
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
        IFolderOpener folderOpener,
        IJavaListService javaListService)
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
        _javaListService = javaListService;
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

    [ObservableProperty]
    private bool _javaStatusIsError;

    /// <summary>启动页常驻的 Java 状态行：版本不匹配时提前说清"要 25、现在 17"并指路设置页。</summary>
    [ObservableProperty]
    private string _javaStatusText = "";

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
            VersionStatus = "读取已安装版本失败：" + ErrorMessageFormatter.Describe(ex);
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
            VersionStatus = "打开目录失败：" + ErrorMessageFormatter.Describe(ex);
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
        UpdateJavaStatus();
    }

    /// <summary>
    /// 常驻 Java 状态：没装 Java 或版本对不上时直接说清差距和去处（设置页），
    /// 别等用户点完启动才看到一句干巴巴的"未找到 Java"。
    /// </summary>
    private void UpdateJavaStatus()
    {
        var settings = _settingsService.Load();
        var requiredMajor = SelectedVersion is null
            ? (int?)null
            : ResolveRequiredJavaMajor(GetMinecraftFolder(settings), SelectedVersion);
        var javaPath = _javaService.ResolveJavaExecutable(settings, requiredMajor);
        var java = javaPath is null ? null : _javaListService.GetJava(javaPath);

        if (java is null)
        {
            if (javaPath is not null)
            {
                // 用户在设置页手填了路径，扫描列表里没有它，版本号未知但照样能用。
                JavaStatusIsError = false;
                JavaStatusText = $"Java（自定义路径，未识别版本）：{javaPath}";
                return;
            }

            JavaStatusIsError = true;
            JavaStatusText = NotInstalledJavaHint();
            return;
        }

        if (requiredMajor is { } required && java.MajorVersion < required)
        {
            JavaStatusIsError = true;
            JavaStatusText = $"Java 版本过低：该版本需要 Java {required}，当前可用的是 Java {java.MajorVersion}，请到设置页更换或安装新的 Java";
            return;
        }

        JavaStatusIsError = false;
        JavaStatusText = java.Version.Length > 0
            ? $"Java {java.MajorVersion}（{java.Version} · {java.Architecture}） · {java.Path}"
            : $"Java {java.MajorVersion} · {java.Path}";
    }

    private static string NotInstalledJavaHint()
        => "未检测到 Java：请到设置页扫描或指定 Java 路径，否则无法启动游戏";

    private int? ResolveRequiredJavaMajor(string gameFolder, MinecraftVersion version)
        => _catalog.LoadJson(gameFolder, version.Id)?.JavaVersion?.MajorVersion;

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
            var requiredJavaMajor = ResolveRequiredJavaMajor(gameFolder, version);
            var java = _javaService.ResolveJavaExecutable(launchSettings, requiredJavaMajor);
            if (java is null)
            {
                var javaHint = requiredJavaMajor is { } required
                    ? $"未找到 Java：{version.Id} 需要 Java {required}，请先安装该版本或到设置页指定 Java 路径"
                    : "未找到 Java：请先安装 Java 或到设置页指定 Java 路径";
                LogLine(javaHint);
                StatusMessage = javaHint;
                return;
            }

            LogLine($"使用 Java：{java}");
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
            var failure = "启动失败：" + ErrorMessageFormatter.Describe(ex);
            LogLine(failure);
            StatusMessage = failure;
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
            LogLine(GameExitDiagnostics.LogLine(exitCode));
            var advice = GameExitDiagnostics.Describe(exitCode);
            if (advice.Length > 0)
            {
                LogLine($"排查建议：{advice}");
            }

            StatusMessage = $"已退出 {versionId}（{GameExitDiagnostics.Brief(exitCode)}）";
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
