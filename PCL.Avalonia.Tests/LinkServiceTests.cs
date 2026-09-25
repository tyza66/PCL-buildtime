using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Link;

namespace PCL.Avalonia.Tests;

public sealed class LinkServiceTests
{
    private const string SuccessOutput = """
        [
          {"hostname":"Mac.local","cost":"Local","ipv4":"","lat_ms":"0","nat_type":"3"},
          {"hostname":"Client-ab12cd","cost":"Direct","ipv4":"10.114.114.114","lat_ms":"8.4","nat_type":"4"}
        ]
        """;

    private const string ServerOutput = """
        [
          {"hostname":"Mac.local","cost":"Local","ipv4":"","lat_ms":"0","nat_type":"5"},
          {"hostname":"Server-abcdef","cost":"Relay","ipv4":"10.114.114.114","lat_ms":"25.0","nat_type":"5"}
        ]
        """;

    private sealed class FakeInstaller : ILinkBinaryInstaller
    {
        public string CorePath => "/tmp/easytier-core";

        public string CliPath => "/tmp/easytier-cli";

        public int EnsureCalls { get; private set; }

        public Task EnsureInstalledAsync(
            int installedVersion,
            Action<int> persistVersion,
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessFactory : ILinkProcessFactory
    {
        public bool StartRunning { get; set; } = true;

        public FakeEasyTierProcess? Process { get; private set; }

        public string? LastExecutable { get; private set; }

        public IEasyTierProcess StartCore(string executablePath)
        {
            LastExecutable = executablePath;
            Process = new FakeEasyTierProcess { IsRunning = StartRunning };
            return Process;
        }
    }

    private sealed class FakeEasyTierProcess : IEasyTierProcess
    {
        public bool IsRunning { get; set; } = true;

        public string RecentLog { get; set; } = "started";

        public IReadOnlyList<string>? Arguments { get; private set; }

        public int KillCount { get; private set; }

        public event Action<string>? LogLine;

        public void RaiseLogLine(string line) => LogLine?.Invoke(line);

        public void Start(IReadOnlyList<string> arguments)
        {
            Arguments = arguments;
        }

        public void Kill()
        {
            KillCount++;
            IsRunning = false;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakePeerFetcher : ILinkPeerFetcher
    {
        public IReadOnlyList<string> Peers { get; set; } = ["tcp://public.easytier.cn:11010"];

        public Task<IReadOnlyList<string>> ResolvePeersAsync(
            LinkInviteInfo invite,
            bool isServer,
            string customPeer,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Peers);
    }

    private sealed class FakeCliRunner : ILinkCliRunner
    {
        public string Output { get; set; } = SuccessOutput;

        public int CallCount { get; private set; }

        public IReadOnlyList<string>? LastArguments { get; private set; }

        public Task<string> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastArguments = arguments;
            return Task.FromResult(Output);
        }
    }

    private static (LinkService Service, FakeInstaller Installer, FakeProcessFactory Factory, FakePeerFetcher Fetcher, FakeCliRunner Cli) CreateService()
    {
        var installer = new FakeInstaller();
        var factory = new FakeProcessFactory();
        var fetcher = new FakePeerFetcher();
        var cli = new FakeCliRunner();
        return (
            new LinkService(installer, factory, fetcher, cli),
            installer,
            factory,
            fetcher,
            cli);
    }

    [Fact]
    public async Task CreateRoomAsync_StartsCoreAndReportsConnected()
    {
        var (service, installer, factory, fetcher, cli) = CreateService();
        var events = 0;
        service.StateChanged += () => events++;

        var session = await service.CreateRoomAsync(
            25565,
            LinkLatencyMode.PreferredLowLatency,
            "",
            progress: null);

        Assert.True(session.IsServer);
        Assert.Equal(25565, session.ServerPort);
        Assert.StartsWith("P63DD-", session.NetworkName);
        Assert.Equal(1, installer.EnsureCalls);
        Assert.Contains("tcp://public.easytier.cn:11010", session.Peers);
        Assert.NotNull(factory.Process);
        Assert.NotNull(factory.Process?.Arguments);
        Assert.Contains("--network-name=" + session.NetworkName, factory.Process!.Arguments!);
        Assert.Contains("--tcp-whitelist=25565", factory.Process.Arguments);
        Assert.Contains("--latency-first", factory.Process.Arguments);
        Assert.Equal("/tmp/easytier-core", factory.LastExecutable);
        Assert.Equal(LinkState.Finished, service.State);
        Assert.Equal(1, service.Progress);
        Assert.Equal("联机成功", service.StatusMessage);
        Assert.Equal(LinkNatType.FullCone, service.NatType);
        Assert.Contains(service.Peers, peer => peer.Name == "Client-ab12cd" && peer.Ping == 8.4);
        Assert.True(events > 0);
        Assert.Contains("-o", cli.LastArguments ?? []);
    }

    [Fact]
    public async Task JoinRoomAsync_ValidInvite_StartsClientMode()
    {
        var (service, _, factory, _, cli) = CreateService();
        cli.Output = ServerOutput;

        var session = await service.JoinRoomAsync(
            "P0001-ABCDE-12345-02001",
            LinkLatencyMode.PreferredDirect,
            "");

        Assert.False(session.IsServer);
        Assert.Equal(1, session.ServerPort);
        Assert.Equal(LinkState.Finished, service.State);
        Assert.Contains("localhost:", session.ClientAddress);
        var arguments = factory.Process?.Arguments ?? [];
        Assert.Contains("-d", arguments);
        Assert.Contains("--tcp-whitelist=0", arguments);
        Assert.Contains(arguments, argument =>
            argument.StartsWith("--port-forward tcp://[::1]:", StringComparison.Ordinal));
        Assert.Contains(service.Peers, peer => peer.Name == "Server-abcdef" && peer.Relay);
        Assert.Equal(LinkNatType.PortRestricted, service.NatType);
    }

    [Fact]
    public async Task JoinRoomAsync_InvalidInvite_DoesNotStart()
    {
        var (service, _, factory, _, _) = CreateService();

        var exception = await Assert.ThrowsAsync<FormatException>(() =>
            service.JoinRoomAsync("not-an-invite", LinkLatencyMode.PreferredDirect, ""));

        Assert.Contains("邀请码", exception.Message);
        Assert.Equal(LinkState.Waiting, service.State);
        Assert.Null(factory.Process);
    }

    [Fact]
    public async Task CreateRoomAsync_CoreCrash_ReportsFailure()
    {
        var (service, _, factory, _, _) = CreateService();
        var events = 0;
        service.StateChanged += () => events++;
        factory.StartRunning = false;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateRoomAsync(25565, LinkLatencyMode.PreferredDirect, ""));

        Assert.Contains("联机模块已崩溃", exception.Message);
        Assert.Contains("started", exception.Message);
        Assert.Equal(LinkState.Failed, service.State);
        Assert.Null(service.Session);
        Assert.Contains("联机模块已崩溃", service.ErrorMessage);
        Assert.True(events > 0);
    }

    [Fact]
    public async Task RefreshPeersAsync_WithoutSession_SkipsCli()
    {
        var (service, _, _, _, cli) = CreateService();

        await service.RefreshPeersAsync();

        Assert.Equal(0, cli.CallCount);
    }

    [Fact]
    public async Task RefreshPeersAsync_UpdatesPeersAndRaisesEvent()
    {
        var (service, _, _, _, cli) = CreateService();
        await service.CreateRoomAsync(25565, LinkLatencyMode.PreferredDirect, "");
        var callsBefore = cli.CallCount;
        var events = 0;
        service.StateChanged += () => events++;
        cli.Output = ServerOutput;

        await service.RefreshPeersAsync();

        Assert.True(cli.CallCount > callsBefore);
        Assert.Contains(service.Peers, peer => peer.Name == "Server-abcdef");
        Assert.Equal(LinkNatType.PortRestricted, service.NatType);
        Assert.Equal(1, events);
    }

    [Fact]
    public async Task StopAsync_ResetsStateAndKillsCore()
    {
        var (service, _, factory, _, _) = CreateService();
        await service.CreateRoomAsync(25565, LinkLatencyMode.PreferredDirect, "");
        Assert.True(factory.Process!.IsRunning);

        await service.StopAsync();

        Assert.Equal(LinkState.Waiting, service.State);
        Assert.Null(service.Session);
        Assert.Empty(service.Peers);
        Assert.Equal(0, service.Progress);
        Assert.Equal("准备初始化", service.StatusMessage);
        Assert.Equal(1, factory.Process.KillCount);
        Assert.False(factory.Process.IsRunning);
    }
}
