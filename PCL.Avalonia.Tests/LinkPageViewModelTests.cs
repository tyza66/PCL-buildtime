using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Link;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class LinkPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public void Debounce(string key, TimeSpan delay, Action action)
        {
        }
    }

    private sealed class FakeLinkService : ILinkService
    {
        public LinkState State { get; set; } = LinkState.Waiting;

        public string StatusMessage { get; set; } = "";

        public double Progress { get; set; }

        public string? ErrorMessage { get; set; }

        public LinkSession? Session { get; set; }

        public IReadOnlyList<LinkPeer> Peers { get; set; } = [];

        public LinkNatType NatType { get; set; } = LinkNatType.Pending;

        public Func<int, LinkLatencyMode, string, IProgress<double>?, Task<LinkSession>>? CreateHandler { get; set; }

        public Func<string, LinkLatencyMode, string, IProgress<double>?, Task<LinkSession>>? JoinHandler { get; set; }

        public int CreateCalls { get; private set; }

        public int JoinCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public int StopCalls { get; private set; }

        public event Action? StateChanged;

        public Task<LinkSession> CreateRoomAsync(
            int serverPort,
            LinkLatencyMode latencyMode,
            string customPeer,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return CreateHandler is null
                ? Task.FromResult(CreateSession(serverPort, customPeer))
                : CreateHandler(serverPort, latencyMode, customPeer, progress);
        }

        public Task<LinkSession> JoinRoomAsync(
            string inviteCode,
            LinkLatencyMode latencyMode,
            string customPeer,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            JoinCalls++;
            return JoinHandler is null
                ? Task.FromResult(CreateSession(25565, customPeer))
                : JoinHandler(inviteCode, latencyMode, customPeer, progress);
        }

        public Task RefreshPeersAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public void RaiseStateChanged() => StateChanged?.Invoke();

        public void Dispose()
        {
        }

        private static LinkSession CreateSession(int port, string customPeer)
        {
            return new LinkSession(
                IsServer: true,
                ServerPort: port,
                ClientPort: 0,
                RpcPort: 15780,
                ListenersPort: 15781,
                NetworkName: "P63D9-ABCDE",
                NetworkSecret: "12345",
                Hostname: "Server-ab0012",
                DiscoverNodeId: -1,
                Peers: [customPeer]);
        }
    }

    private static LinkPageViewModel CreateViewModel(
        FakeSettingsService settings,
        FakeLinkService linkService)
        => new(linkService, settings, new FakeDispatcher());

    [Fact]
    public void Constructor_LoadsLatencyModeAndCustomPeer()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings
            {
                LinkLatencyMode = LinkLatencyMode.PreferredLowLatency,
                LinkCustomPeer = "tcp://custom.example:11010",
            },
        };

        var viewModel = CreateViewModel(settings, new FakeLinkService());

        Assert.Equal(LinkLatencyMode.PreferredLowLatency, viewModel.LinkLatencyMode.Mode);
        Assert.Equal("tcp://custom.example:11010", viewModel.CustomPeer);
        Assert.False(viewModel.IsSessionActive);
        Assert.Equal("", viewModel.InviteCodeText);
    }

    [Fact]
    public async Task CreateRoomCommand_OnSuccess_ShowsSessionAndInvite()
    {
        var service = new FakeLinkService();
        service.CreateHandler = (port, _, _, _) =>
        {
            service.Session = new LinkSession(
                IsServer: true,
                ServerPort: port,
                ClientPort: 0,
                RpcPort: 15780,
                ListenersPort: 15781,
                NetworkName: "P63D9-ABCDE",
                NetworkSecret: "12345",
                Hostname: "Server-ab0012",
                DiscoverNodeId: -1,
                Peers: ["tcp://public.easytier.cn:11010"]);
            service.Peers =
            [
                new LinkPeer(LinkPeerType.Client, "Client-ab12cd", 8.4, false, LinkNatType.FullCone),
            ];
            service.NatType = LinkNatType.FullCone;
            service.State = LinkState.Finished;
            service.StatusMessage = "联机成功";
            return Task.FromResult(service.Session!);
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), service);

        await viewModel.CreateRoomCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsSessionActive);
        Assert.NotNull(viewModel.Session);
        Assert.Contains("P63D9-ABCDE-12345-02000", viewModel.InviteCodeText);
        Assert.Equal("全锥型", viewModel.NatTypeText);
        Assert.Equal("联机成功", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.RefreshPeersCommand.CanExecute(null));
        Assert.Equal(1, service.CreateCalls);
    }

    [Fact]
    public async Task CreateRoomCommand_InvalidPort_ShowsErrorAndDoesNotStart()
    {
        var service = new FakeLinkService();
        var viewModel = CreateViewModel(new FakeSettingsService(), service);
        viewModel.ServerPort = 80;

        await viewModel.CreateRoomCommand.ExecuteAsync(null);

        Assert.Contains("1024", viewModel.ErrorMessage);
        Assert.Null(viewModel.Session);
        Assert.False(viewModel.IsSessionActive);
        Assert.Equal(0, service.CreateCalls);
    }

    [Fact]
    public async Task JoinRoomCommand_InvalidInvite_ShowsInvalidMessage()
    {
        var service = new FakeLinkService();
        service.JoinHandler = (_, _, _, _) =>
            throw new FormatException("邀请码有误，请让房主使用 PCL 创建房间！");
        var viewModel = CreateViewModel(new FakeSettingsService(), service);
        viewModel.InviteCode = "bad";

        await viewModel.JoinRoomCommand.ExecuteAsync(null);

        Assert.Equal("邀请码无效", viewModel.StatusMessage);
        Assert.Contains("邀请码有误", viewModel.ErrorMessage);
        Assert.Null(viewModel.Session);
        Assert.Equal(1, service.JoinCalls);
    }

    [Fact]
    public async Task JoinRoomCommand_EmptyInvite_ShowsRequiredError()
    {
        var service = new FakeLinkService();
        var viewModel = CreateViewModel(new FakeSettingsService(), service);
        viewModel.InviteCode = "   ";

        await viewModel.JoinRoomCommand.ExecuteAsync(null);

        Assert.Contains("请先输入邀请码", viewModel.ErrorMessage);
        Assert.Equal(0, service.JoinCalls);
    }

    [Fact]
    public async Task RefreshPeersCommand_UpdatesPeersAndNat()
    {
        var service = new FakeLinkService();
        service.Peers = [];
        var viewModel = CreateViewModel(new FakeSettingsService(), service);
        await viewModel.CreateRoomCommand.ExecuteAsync(null);

        service.Peers =
        [
            new LinkPeer(LinkPeerType.Server, "Server-abcdef", 12, true, LinkNatType.PortRestricted),
        ];
        service.NatType = LinkNatType.PortRestricted;

        await viewModel.RefreshPeersCommand.ExecuteAsync(null);

        Assert.Equal(1, service.RefreshCalls);
        var peer = Assert.Single(viewModel.Peers);
        Assert.Equal("Server-abcdef", peer.Name);
        Assert.Equal("端口受限型", viewModel.NatTypeText);
    }

    [Fact]
    public async Task StateChangedEvent_RefreshesSessionAndProgress()
    {
        var service = new FakeLinkService();
        var viewModel = CreateViewModel(new FakeSettingsService(), service);
        await viewModel.CreateRoomCommand.ExecuteAsync(null);

        service.Session = new LinkSession(
            IsServer: false,
            ServerPort: 25565,
            ClientPort: 25888,
            RpcPort: 15780,
            ListenersPort: 15781,
            NetworkName: "P63D9-ABCDE",
            NetworkSecret: "12345",
            Hostname: "Client-abcdef",
            DiscoverNodeId: -1,
            Peers: ["tcp://public.easytier.cn:11010"]);
        service.Peers =
        [
            new LinkPeer(LinkPeerType.Server, "Server-ab12cd", 25, true, LinkNatType.Symmetric),
        ];
        service.NatType = LinkNatType.Symmetric;
        service.Progress = 0.8;
        service.State = LinkState.Finished;
        service.StatusMessage = "正在等待玩家加入……";
        service.RaiseStateChanged();

        Assert.Equal(25888, viewModel.Session?.ClientPort);
        Assert.Equal("对称型", viewModel.NatTypeText);
        Assert.Equal(0.8, viewModel.Progress);
        Assert.Equal("正在等待玩家加入……", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExitLinkCommand_ResetsStateAndStopsService()
    {
        var service = new FakeLinkService();
        var viewModel = CreateViewModel(new FakeSettingsService(), service);
        await viewModel.CreateRoomCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsSessionActive);

        viewModel.ExitLinkCommand.Execute(null);

        Assert.Null(viewModel.Session);
        Assert.False(viewModel.IsSessionActive);
        Assert.Empty(viewModel.Peers);
        Assert.Equal(0, viewModel.Progress);
        Assert.Equal("已退出联机", viewModel.StatusMessage);
        Assert.False(viewModel.RefreshPeersCommand.CanExecute(null));
        Assert.Equal(1, service.StopCalls);
    }
}
