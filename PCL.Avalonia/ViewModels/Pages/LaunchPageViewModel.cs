using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class LaunchPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IJavaService _javaService;
    private readonly IGameLauncher _launcher;
    private readonly SessionState _session;
    private readonly IUiDispatcher _dispatcher;
    private IGameLaunch? _activeLaunch;

    public LaunchPageViewModel(
        ISettingsService settingsService,
        IJavaService javaService,
        IGameLauncher launcher,
        SessionState session,
        IUiDispatcher dispatcher)
    {
        _settingsService = settingsService;
        _javaService = javaService;
        _launcher = launcher;
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
            var settings = _settingsService.Load();
            var java = _javaService.ResolveJavaExecutable(settings);
            if (java is null)
            {
                LogLine("未找到 Java，请先在设置页配置 Java 路径");
                StatusMessage = "未找到 Java";
                return;
            }

            var plan = await Task.Run(() => _launcher.BuildLaunchPlan(version, settings, java));
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
            IsRunning = false;
            LogLine($"游戏进程已退出（退出码 {exitCode}）");
            StatusMessage = $"已退出 {versionId}（退出码 {exitCode}）";
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
