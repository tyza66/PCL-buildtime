using Avalonia.Threading;
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
    private GameLaunch? _activeLaunch;

    public LaunchPageViewModel(
        ISettingsService settingsService,
        IJavaService javaService,
        IGameLauncher launcher,
        SessionState session)
    {
        _settingsService = settingsService;
        _javaService = javaService;
        _launcher = launcher;
        _session = session;
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
            _activeLaunch = _launcher.Launch(plan, new Progress<string>(LogLine));
            IsRunning = true;
            LogLine($"Java 进程已启动（PID {_activeLaunch.ProcessId}）");
            StatusMessage = $"正在运行 {version.Id}";
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
        _activeLaunch?.Kill();
        _activeLaunch?.Dispose();
        _activeLaunch = null;
        IsRunning = false;
        LogLine("已请求终止游戏进程");
        StatusMessage = SelectedVersion is null ? "请先选择一个版本" : $"已停止 {SelectedVersion.Id}";
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
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => LogLine(line));
            return;
        }

        LogText = LogText.Length == 0 ? line : LogText + "\n" + line;
    }
}
