namespace PCL.Avalonia.Services.Link;

public interface ILinkPeerFetcher
{
    Task<IReadOnlyList<string>> ResolvePeersAsync(
        LinkInviteInfo invite,
        bool isServer,
        string customPeer,
        CancellationToken cancellationToken = default);
}

public interface ILinkBinaryInstaller
{
    string CorePath { get; }

    string CliPath { get; }

    Task EnsureInstalledAsync(
        int installedVersion,
        Action<int> persistVersion,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}

public interface IEasyTierProcess : IDisposable
{
    event Action<string>? LogLine;

    bool IsRunning { get; }

    string RecentLog { get; }

    void Start(IReadOnlyList<string> arguments);

    void Kill();
}

public interface ILinkProcessFactory
{
    IEasyTierProcess StartCore(string executablePath);
}

public interface ILinkCliRunner
{
    Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public interface ILinkService : IDisposable
{
    LinkState State { get; }

    string StatusMessage { get; }

    double Progress { get; }

    string? ErrorMessage { get; }

    LinkSession? Session { get; }

    IReadOnlyList<LinkPeer> Peers { get; }

    LinkNatType NatType { get; }

    event Action? StateChanged;

    Task<LinkSession> CreateRoomAsync(
        int serverPort,
        LinkLatencyMode latencyMode,
        string customPeer,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<LinkSession> JoinRoomAsync(
        string inviteCode,
        LinkLatencyMode latencyMode,
        string customPeer,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task RefreshPeersAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
