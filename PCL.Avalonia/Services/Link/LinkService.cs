using System.Globalization;
using System.Net;
using System.Net.Sockets;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Link;

public sealed class LinkService : ILinkService
{
    private readonly ILinkBinaryInstaller _installer;
    private readonly ILinkProcessFactory _processFactory;
    private readonly ILinkPeerFetcher _peerFetcher;
    private readonly ILinkCliRunner _cliRunner;
    private readonly CancellationTokenSource _lifeTimeCts = new();
    private IEasyTierProcess? _coreProcess;
    private IReadOnlyList<LinkPeer> _peers = [];
    private LinkNatType _natType = LinkNatType.Pending;
    private LinkState _state = LinkState.Waiting;
    private string _statusMessage = "";
    private double _progress;
    private string? _errorMessage;
    private LinkSession? _session;
    private int _discoverNodeId;

    public LinkService(
        ILinkBinaryInstaller installer,
        ILinkProcessFactory processFactory,
        ILinkPeerFetcher peerFetcher,
        ILinkCliRunner cliRunner)
    {
        _installer = installer;
        _processFactory = processFactory;
        _peerFetcher = peerFetcher;
        _cliRunner = cliRunner;
        _statusMessage = "准备初始化";
    }

    public static LinkService Create(string easyTierDirectory, IDownloadClient downloadClient)
    {
        var installer = new LinkBinaryInstaller(easyTierDirectory, downloadClient);
        var peerFetcher = new LinkPeerFetcher(downloadClient);
        return new LinkService(
            installer,
            new LinkProcessFactory(),
            peerFetcher,
            new LinkCliRunner(installer.CliPath));
    }

    public event Action? StateChanged;

    public LinkState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            StateChanged?.Invoke();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
        }
    }

    public double Progress
    {
        get => _progress;
        private set => _progress = value;
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => _errorMessage = value;
    }

    public LinkSession? Session
    {
        get => _session;
        private set => _session = value;
    }

    public IReadOnlyList<LinkPeer> Peers => _peers;

    public LinkNatType NatType => _natType;

    public async Task<LinkSession> CreateRoomAsync(
        int serverPort,
        LinkLatencyMode latencyMode,
        string customPeer,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var networkName = "P" + serverPort.ToString("X4", CultureInfo.InvariantCulture) + "-"
            + LinkInviteCodec.GenerateRandomCode();
        var networkSecret = LinkInviteCodec.GenerateRandomCode();
        var invite = new LinkInviteInfo(networkName, networkSecret, serverPort, -1);
        return await StartAsync(
            invite,
            isServer: true,
            latencyMode,
            customPeer,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LinkSession> JoinRoomAsync(
        string inviteCode,
        LinkLatencyMode latencyMode,
        string customPeer,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var codeError = LinkInviteCodec.ValidateCode(inviteCode);
        if (codeError is not null)
        {
            throw new FormatException(codeError);
        }

        var invite = LinkInviteCodec.ParseInvite(inviteCode);
        return await StartAsync(
            invite,
            isServer: false,
            latencyMode,
            customPeer,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshPeersAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null || !IsCoreRunning())
        {
            return;
        }

        var output = await RunCliForPeersAsync(cancellationToken).ConfigureAwait(false);
        UpdatePeers(output);
    }

    public async Task StopAsync()
    {
        KillCore();
        _peers = [];
        _natType = LinkNatType.Pending;
        _session = null;
        _errorMessage = null;
        Progress = 0;
        StatusMessage = "准备初始化";
        State = LinkState.Waiting;
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task<LinkSession> StartAsync(
        LinkInviteInfo invite,
        bool isServer,
        LinkLatencyMode latencyMode,
        string customPeer,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        StopCore();
        Progress = 0;
        ErrorMessage = null;
        _peers = [];
        _natType = LinkNatType.Pending;
        _discoverNodeId = invite.DiscoverNodeId;
        State = LinkState.Loading;

        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifeTimeCts.Token);
        var ct = timeoutCts.Token;
        try
        {
            Report(0.07, "正在下载联机模块……");
            await _installer.EnsureInstalledAsync(
                0,
                _ => { },
                status: null,
                cancellationToken: ct).ConfigureAwait(false);

            Report(0.13, "正在获取节点列表……");
            var peers = await _peerFetcher.ResolvePeersAsync(
                invite,
                isServer,
                customPeer,
                ct).ConfigureAwait(false);

            Report(0.15, "正在启动联机模块……");
            var (clientPort, rpcPort, listenersPort) = FindFreePorts(invite.ServerPort);
            var hostname = (isServer ? "Server-" : "Client-")
                + HashHostname(NetworkName(invite, isServer));
            var session = new LinkSession(
                IsServer: isServer,
                ServerPort: invite.ServerPort,
                ClientPort: clientPort,
                RpcPort: rpcPort,
                ListenersPort: listenersPort,
                NetworkName: invite.NetworkName,
                NetworkSecret: invite.NetworkSecret,
                Hostname: hostname,
                DiscoverNodeId: invite.DiscoverNodeId,
                Peers: peers);
            var arguments = LinkArguments.BuildCoreArguments(session, latencyMode);
            _coreProcess = _processFactory.StartCore(_installer.CorePath);
            _coreProcess.Start(arguments);
            _session = session;

            await WaitForConnectionAsync(
                session,
                isServer,
                progress,
                ct).ConfigureAwait(false);
            Report(1, "联机成功");
            State = LinkState.Finished;
            return session;
        }
        catch (OperationCanceledException)
        {
            KillCore();
            _session = null;
            throw;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            KillCore();
            _session = null;
            State = LinkState.Failed;
            throw;
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }

    private async Task WaitForConnectionAsync(
        LinkSession session,
        bool isServer,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var failCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCoreRunning())
            {
                throw new InvalidOperationException(
                    "联机模块已崩溃，近期日志：" + Environment.NewLine + GetRecentLog());
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            var output = "";
            try
            {
                output = await RunCliForPeersAsync(cancellationToken).ConfigureAwait(false);
                UpdatePeers(output);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failCount++;
            }

            var target = GetTargetPeer(isServer);
            if (target is not null && target.Ping > 0)
            {
                return;
            }

            if (failCount >= 6)
            {
                throw new TimeoutException("无法启动联机模块，请检查网络环境后重试。");
            }

            var peerCount = _peers.Count(peer => peer.Ping > 0);
            if (peerCount == 0)
            {
                Report(
                    isServer
                        ? Math.Min(Progress + 0.02, 0.95)
                        : Math.Min(Progress + 0.02, 0.5),
                    "正在连接到节点……");
            }
            else
            {
                Report(
                    Math.Min(0.45 + peerCount * 0.05, 0.95),
                    isServer ? "正在等待玩家加入……" : "正在连接到房主……");
            }
        }
    }

    private async Task<string> RunCliForPeersAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return "";
        }

        return await _cliRunner.RunAsync(
            ["-o", "json", "-p", $"127.0.0.1:{_session.RpcPort}", "peer"],
            TimeSpan.FromSeconds(3),
            cancellationToken).ConfigureAwait(false);
    }

    private void UpdatePeers(string output)
    {
        if (!output.Contains("lat_ms", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CLI 调用失败：" + Environment.NewLine + output);
        }

        var peers = LinkPeerParser.ParsePeers(output);
        var self = peers.FirstOrDefault(peer => peer.Type == LinkPeerType.Self);
        if (self is not null)
        {
            _natType = self.NatType;
        }

        _peers = peers.Where(peer => peer.Type != LinkPeerType.Self).ToList();
        StateChanged?.Invoke();
    }

    private LinkPeer? GetTargetPeer(bool isServer)
    {
        var targets = isServer
            ? _peers.Where(peer => peer.Ping > 0)
            : _peers.Where(peer => peer.Type == LinkPeerType.Server && peer.Ping > 0);
        return targets.OrderBy(peer => peer.Ping).FirstOrDefault();
    }

    private static (int ClientPort, int RpcPort, int ListenersPort) FindFreePorts(int serverPort)
    {
        var listenerPorts = new HashSet<int>();
        var used = new HashSet<int> { serverPort };
        var result = new int[3];
        var found = 0;
        for (var port = 1024; port <= 65535 && found < 3; port++)
        {
            if (used.Contains(port) || listenerPorts.Contains(port))
            {
                continue;
            }

            if (!CanBind(port))
            {
                continue;
            }

            if (found == 2)
            {
                for (var offset = 1; offset < 3; offset++)
                {
                    var next = port + offset;
                    if (next > 65535 || used.Contains(next) || !CanBind(next))
                    {
                        listenerPorts.Clear();
                        break;
                    }

                    listenerPorts.Add(next);
                }

                if (listenerPorts.Count != 2)
                {
                    continue;
                }
            }

            result[found] = port;
            used.Add(port);
            found++;
        }

        if (found < 3)
        {
            throw new InvalidOperationException("未找到足够的空闲端口，请稍后重试或重启电脑。");
        }

        return (result[0], result[1], result[2]);
    }

    private static bool CanBind(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string HashHostname(string value)
    {
        var hash = value.Aggregate(17, (current, character) => current * 31 + character);
        var base36 = ConvertToBase36(hash);
        return base36.TrimStart('-').PadLeft(6, '0')[..6];
    }

    private static string ConvertToBase36(int value)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var negative = value < 0;
        var remaining = Math.Abs((long)value);
        var builder = new System.Text.StringBuilder();
        do
        {
            builder.Insert(0, digits[(int)(remaining % 36)]);
            remaining /= 36;
        }
        while (remaining > 0);

        return (negative ? "-" : "") + builder.ToString();
    }

    private static string NetworkName(LinkInviteInfo invite, bool isServer)
    {
        return isServer ? invite.NetworkName : invite.NetworkName + invite.NetworkSecret;
    }

    private bool IsCoreRunning() => _coreProcess is not null && _coreProcess.IsRunning;

    private string GetRecentLog()
    {
        return _coreProcess?.RecentLog ?? "";
    }

    private void KillCore()
    {
        try
        {
            _coreProcess?.Kill();
        }
        catch
        {
        }
        finally
        {
            _coreProcess?.Dispose();
            _coreProcess = null;
        }
    }

    private void StopCore()
    {
        KillCore();
        _session = null;
    }

    private void Report(double progress, string status)
    {
        Progress = progress;
        StatusMessage = status;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _lifeTimeCts.Cancel();
        KillCore();
        _lifeTimeCts.Dispose();
    }
}
