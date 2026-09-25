using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Link;

namespace PCL.Avalonia.Tests;

public sealed class LinkArgumentsTests
{
    private static readonly LinkSession ServerSession = new(
        IsServer: true,
        ServerPort: 25565,
        ClientPort: 0,
        RpcPort: 15780,
        ListenersPort: 15781,
        NetworkName: "P63D9-ABCDE",
        NetworkSecret: "12345",
        Hostname: "Server-ab2013",
        DiscoverNodeId: 2,
        Peers: ["tcp://public.easytier.cn:11010"]);

    private static readonly LinkSession ClientSession = ServerSession with
    {
        IsServer = false,
        ClientPort = 26000,
        Hostname = "Client-ab2013",
        DiscoverNodeId = -2,
        Peers = ["tcp://public.easytier.cn:11010", "tcp://relay.example:11010"],
    };

    [Fact]
    public void BuildCoreArguments_Server_ContainsNetworkAndWhitelist()
    {
        var arguments = LinkArguments.BuildCoreArguments(ServerSession, LinkLatencyMode.PreferredDirect);

        Assert.Contains("--network-name=P63D9-ABCDE", arguments);
        Assert.Contains("--network-secret=12345", arguments);
        Assert.Contains("--listeners", arguments);
        Assert.Contains("15781", arguments);
        Assert.Contains("--rpc-portal", arguments);
        Assert.Contains("15780", arguments);
        Assert.Contains("--private-mode", arguments);
        Assert.Contains("-i", arguments);
        Assert.Contains("10.114.114.114", arguments);
        Assert.Contains("--hostname=Server-ab2013", arguments);
        Assert.Contains("--tcp-whitelist=25565", arguments);
        Assert.Contains("--udp-whitelist=25565", arguments);
        Assert.DoesNotContain("-d", arguments);
        Assert.DoesNotContain("--latency-first", arguments);
        Assert.Contains("-p=tcp://public.easytier.cn:11010", arguments);
    }

    [Fact]
    public void BuildCoreArguments_Client_AddsPortForwardingAndLatencyFirst()
    {
        var arguments = LinkArguments.BuildCoreArguments(
            ClientSession,
            LinkLatencyMode.PreferredLowLatency);

        Assert.Contains("-d", arguments);
        Assert.Contains("--tcp-whitelist=0", arguments);
        Assert.Contains("--udp-whitelist=0", arguments);
        Assert.Contains("--port-forward tcp://[::1]:26000/10.114.114.114:25565", arguments);
        Assert.Contains("--port-forward udp://[::1]:26000/10.114.114.114:25565", arguments);
        Assert.Contains("--port-forward tcp://127.0.0.1:26000/10.114.114.114:25565", arguments);
        Assert.Contains("--port-forward udp://127.0.0.1:26000/10.114.114.114:25565", arguments);
        Assert.Contains("-p=tcp://public.easytier.cn:11010", arguments);
        Assert.Contains("-p=tcp://relay.example:11010", arguments);
        Assert.Contains("--latency-first", arguments);
    }
}
