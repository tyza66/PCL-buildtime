using PCL.Avalonia.Services.Link;

namespace PCL.Avalonia.Tests;

public sealed class LinkPeerParserTests
{
    [Fact]
    public void ParsePeers_ClassifiesServerClientSelfAndMisc()
    {
        var json = """
            [
              { "hostname": "Server-abc123", "cost": "Direct", "ipv4": "10.114.114.114", "lat_ms": "12.5", "nat_type": "OpenInternet" },
              { "hostname": "Client-def456", "cost": "Relay", "ipv4": "10.114.1.2", "lat_ms": 88, "nat_type": 6 },
              { "hostname": "self-host", "cost": "Local", "ipv4": "10.114.114.1", "lat_ms": "0", "nat_type": "FullCone" },
              { "hostname": "Misc", "cost": "Direct", "ipv4": "10.0.0.1", "lat_ms": "200", "nat_type": "Unknown" }
            ]
            """;

        var peers = LinkPeerParser.ParsePeers(json);

        Assert.Equal(4, peers.Count);
        Assert.Equal(LinkPeerType.Server, peers[0].Type);
        Assert.Equal(12.5, peers[0].Ping);
        Assert.False(peers[0].Relay);
        Assert.Equal(LinkNatType.OpenInternet, peers[0].NatType);
        Assert.Equal(LinkPeerType.Client, peers[1].Type);
        Assert.True(peers[1].Relay);
        Assert.Equal(LinkNatType.Symmetric, peers[1].NatType);
        Assert.Equal(LinkPeerType.Self, peers[2].Type);
        Assert.Equal(LinkPeerType.Misc, peers[3].Type);
    }

    [Fact]
    public void ParsePeers_SkipsMalformedEntries()
    {
        var peers = LinkPeerParser.ParsePeers(
            """[{"hostname":"A","cost":"Direct"},{"unexpected":1}]""");

        Assert.Single(peers);
    }

    [Fact]
    public void ParsePeers_InvalidJson_ReturnsEmpty()
    {
        Assert.Empty(LinkPeerParser.ParsePeers("not json"));
    }
}
