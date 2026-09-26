using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Link;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class LinkPageViewModel : ObservableObject, IDisposable
{
    private readonly ILinkService _linkService;
    private readonly ISettingsService _settingsService;
    private readonly IUiDispatcher _dispatcher;

    public LinkPageViewModel(
        ILinkService linkService,
        ISettingsService settingsService,
        IUiDispatcher dispatcher)
    {
        _linkService = linkService;
        _settingsService = settingsService;
        _dispatcher = dispatcher;

        var settings = settingsService.Load();
        LinkLatencyMode = LinkLatencyModes.First(option => option.Mode == settings.LinkLatencyMode);
        CustomPeer = settings.LinkCustomPeer;
        StatusMessage = "请创建房间或输入邀请码加入";
        _linkService.StateChanged += OnLinkStateChanged;
    }

    public IReadOnlyList<LinkLatencyModeOption> LinkLatencyModes { get; } =
    [
        new(PCL.Avalonia.Services.LinkLatencyMode.PreferredDirect, "优先直连"),
        new(PCL.Avalonia.Services.LinkLatencyMode.PreferredLowLatency, "优先低延迟"),
    ];

    [ObservableProperty]
    private int _serverPort = 25565;

    [ObservableProperty]
    private string _inviteCode = "";

    [ObservableProperty]
    private string _customPeer = "";

    [ObservableProperty]
    private LinkLatencyModeOption _linkLatencyMode =
        new(PCL.Avalonia.Services.LinkLatencyMode.PreferredDirect, "优先直连");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRoomCommand))]
    [NotifyCanExecuteChangedFor(nameof(JoinRoomCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshPeersCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionActive))]
    [NotifyPropertyChangedFor(nameof(InviteCodeText))]
    [NotifyCanExecuteChangedFor(nameof(RefreshPeersCommand))]
    private LinkSession? _session;

    [ObservableProperty]
    private IReadOnlyList<LinkPeer> _peers = [];

    [ObservableProperty]
    private string _natTypeText = "未知";

    public bool IsSessionActive => Session is not null;

    public string InviteCodeText => Session is null
        ? ""
        : $"在 PCL 启动器中输入邀请码【{LinkInviteCodec.GenerateInvite(Session.Invite)}】即可加入联机房间！";

    private bool CanCreateOrJoin => !IsBusy;

    private bool CanRefreshPeers => !IsBusy && IsSessionActive;

    [RelayCommand(CanExecute = nameof(CanCreateOrJoin))]
    private async Task CreateRoomAsync()
    {
        if (ServerPort is < 1024 or > 65535)
        {
            ErrorMessage = "端口号需要在 1024 到 65535 之间";
            return;
        }

        await RunAsync(
            () => _linkService.CreateRoomAsync(
                ServerPort,
                LinkLatencyMode.Mode,
                CustomPeer,
                new Progress<double>(value => _dispatcher.Post(() => Progress = value))));
    }

    [RelayCommand(CanExecute = nameof(CanCreateOrJoin))]
    private async Task JoinRoomAsync()
    {
        if (string.IsNullOrWhiteSpace(InviteCode))
        {
            ErrorMessage = "请先输入邀请码";
            return;
        }

        await RunAsync(
            () => _linkService.JoinRoomAsync(
                InviteCode,
                LinkLatencyMode.Mode,
                CustomPeer,
                new Progress<double>(value => _dispatcher.Post(() => Progress = value))));
    }

    [RelayCommand(CanExecute = nameof(CanRefreshPeers))]
    private async Task RefreshPeersAsync()
    {
        try
        {
            await _linkService.RefreshPeersAsync();
            Peers = _linkService.Peers;
            NatTypeText = DescribeNatType(_linkService.NatType);
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorMessageFormatter.Describe(ex);
        }
    }

    [RelayCommand]
    private void ExitLink()
    {
        _ = _linkService.StopAsync();
        Session = null;
        Peers = [];
        Progress = 0;
        ErrorMessage = null;
        StatusMessage = "已退出联机";
    }

    private async Task RunAsync(Func<Task<LinkSession>> operation)
    {
        IsBusy = true;
        ErrorMessage = null;
        Progress = 0;
        try
        {
            var session = await operation();
            Session = session;
            Peers = _linkService.Peers;
            NatTypeText = DescribeNatType(_linkService.NatType);
            StatusMessage = "联机成功";
        }
        catch (FormatException ex)
        {
            ErrorMessage = ErrorMessageFormatter.Describe(ex);
            StatusMessage = "邀请码无效";
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorMessageFormatter.Describe(ex);
            StatusMessage = "联机失败";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLinkStateChanged()
    {
        _dispatcher.Post(() =>
        {
            Session = _linkService.Session;
            Peers = _linkService.Peers;
            NatTypeText = DescribeNatType(_linkService.NatType);
            Progress = _linkService.Progress;
            ErrorMessage = _linkService.ErrorMessage;
            if (_linkService.State == LinkState.Failed || _linkService.State == LinkState.Finished)
            {
                StatusMessage = _linkService.StatusMessage;
            }

            OnPropertyChanged(nameof(IsSessionActive));
            OnPropertyChanged(nameof(InviteCodeText));
        });
    }

    private static string DescribeNatType(LinkNatType natType)
    {
        return natType switch
        {
            LinkNatType.OpenInternet => "开放网络",
            LinkNatType.FullCone => "全锥型",
            LinkNatType.Restricted => "受限型",
            LinkNatType.PortRestricted => "端口受限型",
            LinkNatType.Symmetric => "对称型",
            _ => "未知",
        };
    }

    public void Dispose()
    {
        _linkService.StateChanged -= OnLinkStateChanged;
    }
}
