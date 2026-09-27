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
    private readonly IConfirmationService _confirmationService;
    // 同一次启动里同一类错误只提示一次，否则 JVM 复读机能把建议刷满屏。
    private readonly HashSet<string> _firedLogDiagnostics = new(StringComparer.Ordinal);
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
        IJavaListService javaListService,
        IConfirmationService confirmationService)
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
        _confirmationService = confirmationService;
        _session.VersionInstalled += OnVersionInstalled;
        _session.PropertyChanged += OnSessionPropertyChanged;
        SelectedVersion = _session.SelectedVersion;
        UpdateGameFolderText();
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

    /// <summary>Java 状态行 ToolTip：可见文案只放版本号，完整路径悬停时才展开。</summary>
    [ObservableProperty]
    private string _javaStatusTip = "";

    /// <summary>游戏目录行：只放缩略路径，一双眼就能读完。</summary>
    [ObservableProperty]
    private string _gameFolderPathText = "";

    /// <summary>游戏目录行 ToolTip：放完整路径，多个目录时逐行列出。</summary>
    [ObservableProperty]
    private string _gameFolderPathTip = "";

    /// <summary>
    /// 版本隔离状态行：存档写在哪是用户最常懵的问题，隔离开启时说清"在版本目录里"，
    /// 关闭时说清"在公共游戏目录"，免得 Mod 装错地方还怪启动器丢档。
    /// </summary>
    [ObservableProperty]
    private string _isolationStatusText = "";

    /// <summary>状态行 ToolTip：存档与 Mod 的完整保存路径，文案里放不下就放这里。</summary>
    [ObservableProperty]
    private string _isolationStatusTip = "";

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

    /// <summary>日志面板空着的时候给一句提示，别让一大块白卡片看着像没加载完。</summary>
    public bool HasLogContent => !string.IsNullOrWhiteSpace(LogText);

    partial void OnLogTextChanged(string value) => OnPropertyChanged(nameof(HasLogContent));

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

        var folders = UpdateGameFolderText();

        IsRefreshing = true;
        try
        {
            var installed = await Task.Run(() =>
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var versions = new List<LaunchVersionItemViewModel>();
                // Java 扫描放在这个后台线程里只做一次，卡片徽标直接读结果；
                // Java 版本不对在点启动之前就该看见，不要等启动才报。
                var javaBest = JavaHints.BestMajor(_javaListService.Scan());
                foreach (var folder in folders)
                {
                    foreach (var version in _catalog.Scan(folder))
                    {
                        if (!seen.Add(version.Id))
                        {
                            continue;
                        }

                        var item = new LaunchVersionItemViewModel(
                            version,
                            _versionManager.LoadSettings(folder, version.Id),
                            folder,
                            ToggleLaunchItemFavorite,
                            SelectLaunchItem,
                            OpenLaunchItemFolder,
                            DeleteLaunchItemAsync);
                        item.ApplyJavaHint(JavaHints.ForRequirement(
                            ResolveRequiredJavaMajor(folder, version), javaBest));
                        versions.Add(item);
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

    /// <summary>右键菜单「选择该版本」：右键不改变选中项，没点中时用这个把启动目标切过去。</summary>
    private void SelectLaunchItem(LaunchVersionItemViewModel item)
    {
        SelectedInstalledVersion = item;
    }

    /// <summary>右键菜单「收藏/取消收藏」：立刻写盘，星标和菜单文案同步变化。</summary>
    private void ToggleLaunchItemFavorite(LaunchVersionItemViewModel item)
    {
        try
        {
            var target = !item.IsFavorite;
            _versionManager.SetFavorite(item.SourceFolder, item.Id, target);
            item.IsFavorite = target;
            VersionStatus = target
                ? $"已收藏 {item.Id}"
                : $"已取消收藏 {item.Id}";
        }
        catch (Exception ex)
        {
            VersionStatus = "收藏操作失败：" + ErrorMessageFormatter.Describe(ex);
        }
    }

    /// <summary>右键菜单「打开版本文件夹」：直接开到 versions/&lt;id&gt;，找存档和 jar 都靠它。</summary>
    private void OpenLaunchItemFolder(LaunchVersionItemViewModel item)
    {
        try
        {
            _folderOpener.Open(Path.Combine(item.SourceFolder, "versions", item.Id));
        }
        catch (Exception ex)
        {
            VersionStatus = "打开目录失败：" + ErrorMessageFormatter.Describe(ex);
        }
    }

    /// <summary>
    /// 右键菜单「删除版本」：先弹确认框，删掉后如果删的就是当前启动目标，自动落到列表第一项，
    /// 免得启动按钮还指着已经没了的版本。
    /// </summary>
    private async Task DeleteLaunchItemAsync(LaunchVersionItemViewModel item)
    {
        var confirmed = await _confirmationService.ConfirmAsync(
            VersionDeleteConfirmation.Title,
            VersionDeleteConfirmation.Describe(item.Id));
        if (!confirmed)
        {
            VersionStatus = $"已取消删除 {item.Id}";
            return;
        }

        try
        {
            _versionManager.Delete(item.SourceFolder, item.Id);
            InstalledVersions.Remove(item);
            if (ReferenceEquals(SelectedInstalledVersion, item))
            {
                // 删的就是当前启动目标时先清空再落到列表第一项；列表空了就留空，
                // 别再让启动按钮指向一个已经不在磁盘上的版本。
                SelectedInstalledVersion = InstalledVersions.FirstOrDefault();
                if (SelectedInstalledVersion is null)
                {
                    SelectedVersion = null;
                    _session.SelectedVersion = null;
                }
            }

            VersionStatus = $"已删除 {item.Id}";
        }
        catch (Exception ex)
        {
            VersionStatus = "删除版本失败：" + ErrorMessageFormatter.Describe(ex);
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
        UpdateIsolationStatus();
    }

    /// <summary>依赖版本设置与全局默认判定当前隔离状态，在目录行下方常驻显示。</summary>
    private void UpdateIsolationStatus()
    {
        if (SelectedVersion is null)
        {
            IsolationStatusText = "";
            IsolationStatusTip = "";
            return;
        }

        var isolated = false;
        try
        {
            var settings = _settingsService.Load();
            var gameFolder = GetMinecraftFolder(settings);
            var versionSettings = _versionManager.LoadSettings(gameFolder, SelectedVersion.Id);
            isolated = VersionIsolationResolver.IsIsolated(
                SelectedVersion, settings.VersionIsolationDefault, versionSettings);
            // 左列净宽只有 276px，文案必须短，完整路径放 ToolTip 里悬停补全。
            IsolationStatusTip = isolated
                ? $"存档与 Mod 保存路径：{SelectedVersion.Folder}"
                : $"存档与 Mod 保存路径：{gameFolder}";
        }
        catch (Exception ex)
        {
            // 每版本设置读不出来（外置卷 I/O 抖动、INI 损坏等）也不能让这一行凭空消失：
            // 退回全局默认给出判定，并把原因写进 ToolTip，启动逻辑本身仍按全局默认走。
            try
            {
                isolated = VersionIsolationResolver.IsIsolated(
                    SelectedVersion, _settingsService.Load().VersionIsolationDefault);
            }
            catch
            {
                isolated = false;
            }

            IsolationStatusTip = "暂时按全局默认处理：" + ErrorMessageFormatter.Describe(ex);
        }

        IsolationStatusText = isolated
            ? "版本隔离：已开启，存档与 Mod 存在版本目录"
            : "版本隔离：未开启，存档与 Mod 存在公共目录";
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
                JavaStatusText = "Java（自定义路径，未识别版本）";
                JavaStatusTip = $"使用自定义 Java 路径：{javaPath}";
                return;
            }

            JavaStatusIsError = true;
            JavaStatusText = NotInstalledJavaHint();
            JavaStatusTip = JavaStatusText;
            return;
        }

        if (requiredMajor is { } required && java.MajorVersion < required)
        {
            JavaStatusIsError = true;
            JavaStatusText = $"Java 版本过低：该版本需要 Java {required}，当前可用的是 Java {java.MajorVersion}，请到设置页更换或安装新的 Java";
            JavaStatusTip = JavaStatusText;
            return;
        }

        JavaStatusIsError = false;
        // 可见文案只留版本（左列只有 276px，路径一拼就被截成 "/opt/home..."），
        // 版本 + 架构 + 完整路径都放 ToolTip，需要排查时悬停就能抄。
        JavaStatusText = java.Version.Length > 0
            ? $"Java {java.MajorVersion}（{java.Version} · {java.Architecture}）"
            : $"Java {java.MajorVersion}";
        JavaStatusTip = java.Path.Length > 0
            ? $"{JavaStatusText} · {java.Path}"
            : JavaStatusText;
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

    /// <summary>
    /// 刷新目录行：可见文案只放缩略路径（家目录折叠成 ~），完整路径与多目录清单放 ToolTip，
    /// 免得左列那点宽度被一串绝对路径挤成 "/Users/tyza66/Libr..." 这种没法看的样子。
    /// </summary>
    private List<string> UpdateGameFolderText()
    {
        var settings = _settingsService.Load();
        var folders = ResolveGameFolders(settings);
        if (folders.Count == 0)
        {
            GameFolderPathText = "尚未设置游戏目录";
            GameFolderPathTip = "到设置页指定 Minecraft 目录后即可启动游戏";
            return folders;
        }

        var display = AbbreviateHome(folders[0]);
        if (folders.Count > 1)
        {
            display += $"（另有 {folders.Count - 1} 个目录）";
        }

        GameFolderPathText = display;
        GameFolderPathTip = string.Join('\n', folders);

        return folders;
    }

    /// <summary>把用户主目录折叠成 ~，路径短一截才好在一行里读完。</summary>
    private static string AbbreviateHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)
            && path.Length > home.Length
            && path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[home.Length..];
        }

        return path;
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
            _firedLogDiagnostics.Clear();
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

            // 状态栏的黄字只是提醒，点启动时必须真拦住：Java 大版本不够的版本起不来，
            // 不拦的话玩家看到的是莫名闪退，而不是一句"需要 Java 25"。
            var javaMismatch = JavaHints.DescribeMismatch(
                version.Id,
                requiredJavaMajor,
                _javaListService.GetJava(java)?.MajorVersion);
            if (javaMismatch is not null)
            {
                LogLine("启动失败：" + javaMismatch);
                StatusMessage = "启动失败：" + javaMismatch;
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
            var launch = _launcher.Launch(plan, new Progress<string>(OnGameOutput));
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

    /// <summary>
    /// 游戏输出经手处：原样记日志，同时识别 JVM 级错误的典型签名。
    /// 这类错误在进程死前几秒就打出来了，当场给一句"可尝试"比等退出码更有用。
    /// </summary>
    private void OnGameOutput(string line)
    {
        LogLine(line);
        var diagnostic = GameLogDiagnostics.Match(line);
        if (diagnostic is { } hit && _firedLogDiagnostics.Add(hit.Key))
        {
            LogLine("排查建议：" + hit.Advice);
            StatusMessage = hit.Advice;
        }
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
    /// <summary>
    /// 启动页右侧列表项。右键菜单的四个操作由启动页注入：这里只负责把意图转给上层，
    /// 具体落盘（收藏、删除）或打开文件夹都由启动页统一处理，状态提示也走它的 VersionStatus。
    /// </summary>
    public LaunchVersionItemViewModel(
        MinecraftVersion version,
        VersionSettings settings,
        string sourceFolder,
        Action<LaunchVersionItemViewModel>? toggleFavorite = null,
        Action<LaunchVersionItemViewModel>? select = null,
        Action<LaunchVersionItemViewModel>? openFolder = null,
        Func<LaunchVersionItemViewModel, Task>? delete = null)
    {
        Version = version;
        SourceFolder = sourceFolder;
        Id = version.Id;
        TypeText = ResolveTypeText(version);
        ReleaseTimeText = version.ReleaseTime == DateTimeOffset.UnixEpoch
            ? ""
            : version.ReleaseTimeText;
        IsFavorite = settings.IsFavorite;
        Description = string.IsNullOrWhiteSpace(settings.Description) ? "" : settings.Description.Trim();
        _toggleFavorite = toggleFavorite;
        _select = select;
        _openFolder = openFolder;
        _delete = delete;
    }

    public MinecraftVersion Version { get; }

    /// <summary>扫描到这个版本时所在的游戏目录，收藏和删除都要知道根目录在哪。</summary>
    public string SourceFolder { get; }

    public string Id { get; }

    public string TypeText { get; }

    public string ReleaseTimeText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteText))]
    private bool _isFavorite;

    /// <summary>右键菜单里的收藏项跟着状态换文案。</summary>
    public string FavoriteText => IsFavorite ? "取消收藏" : "收藏";

    [ObservableProperty]
    private string _description = "";

    /// <summary>有自定义描述就顶掉类型徽标，一眼能认出是哪个整合包实例。</summary>
    public string Subtitle => Description.Length > 0 ? Description : TypeText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaBadgeIsError))]
    private JavaHintLevel _javaHintLevel;

    [ObservableProperty]
    private string _javaBadgeText = "";

    [ObservableProperty]
    private string _javaBadgeTip = "";

    /// <summary>Java 缺失或版本不够时徽标转红；满足和未知都保持次要色，不让用户误判成出错。</summary>
    public bool JavaBadgeIsError => JavaHintLevel is JavaHintLevel.TooLow or JavaHintLevel.Missing;

    /// <summary>徽标数据由启动页刷新时统一算好后灌进来，条目本身不做任何文件读取。</summary>
    public void ApplyJavaHint(JavaHints hint)
    {
        JavaHintLevel = hint.Level;
        JavaBadgeText = hint.Text;
        JavaBadgeTip = hint.Detail;
    }

    private readonly Action<LaunchVersionItemViewModel>? _toggleFavorite;
    private readonly Action<LaunchVersionItemViewModel>? _select;
    private readonly Action<LaunchVersionItemViewModel>? _openFolder;
    private readonly Func<LaunchVersionItemViewModel, Task>? _delete;

    [RelayCommand]
    private void Favorite() => _toggleFavorite?.Invoke(this);

    [RelayCommand]
    private void SelectItem() => _select?.Invoke(this);

    [RelayCommand]
    private void OpenFolder() => _openFolder?.Invoke(this);

    [RelayCommand]
    private async Task Delete() => await (_delete?.Invoke(this) ?? Task.CompletedTask);

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
