using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class LaunchPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IJavaService _javaService;
    private readonly IGameLauncher _launcher;
    private readonly IMicrosoftAuthenticationService _microsoftAuthentication;
    private readonly IAccountService _accountService;
    private readonly SessionState _session;
    private readonly IUiDispatcher _dispatcher;
    private IGameLaunch? _activeLaunch;

    public LaunchPageViewModel(
        ISettingsService settingsService,
        IJavaService javaService,
        IGameLauncher launcher,
        SessionState session,
        IUiDispatcher dispatcher,
        IMicrosoftAuthenticationService microsoftAuthentication,
        IAccountService accountService)
    {
        _settingsService = settingsService;
        _javaService = javaService;
        _launcher = launcher;
        _microsoftAuthentication = microsoftAuthentication;
        _accountService = accountService;
        _session = session;
        _dispatcher = dispatcher;
        _session.PropertyChanged += OnSessionPropertyChanged;
        SelectedVersion = _session.SelectedVersion;
        StatusMessage = SelectedVersion is null
            ? "请先在版本页选择一个版本"
            : $"当前版本：{SelectedVersion.Id}";
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
            settings = settings with
            {
                UserName = account?.Name is { Length: > 0 } accountName
                    ? accountName
                    : settings.UserName,
            };
            var java = _javaService.ResolveJavaExecutable(settings);
            if (java is null)
            {
                LogLine("未找到 Java，请先在设置页配置 Java 路径");
                StatusMessage = "未找到 Java";
                return;
            }

            var launchAccount = account;
            var plan = await Task.Run(() => _launcher.BuildLaunchPlan(version, settings, java, launchAccount));
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
            StatusMessage = "启动失败";
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
            StatusMessage = SelectedVersion is null
                ? "请先在版本页选择一个版本"
                : $"当前版本：{SelectedVersion.Id}";
        }
    }

    private void LogLine(string line)
    {
        _dispatcher.Post(() =>
        {
            LogText = LogText.Length == 0 ? line : LogText + "\n" + line;
        });
    }
}
