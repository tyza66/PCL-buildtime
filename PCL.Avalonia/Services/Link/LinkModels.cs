using System.Text.Json.Serialization;

namespace PCL.Avalonia.Services.Link;

public enum LinkState
{
    Waiting,
    Loading,
    Failed,
    Finished,
}

public enum LinkPeerType
{
    Client,
    Server,
    Misc,
    Self,
}

public enum LinkNatType
{
    Unknown = 0,
    OpenInternet = 1,
    NoPat = 2,
    FullCone = 3,
    Restricted = 4,
    PortRestricted = 5,
    Symmetric = 6,
    SymUdpFirewall = 7,
    SymmetricEasyInc = 8,
    SymmetricEasyDec = 9,
    Pending = 10,
}

public sealed record LinkPeer(
    LinkPeerType Type,
    string Name,
    double Ping,
    bool Relay,
    LinkNatType NatType);

public sealed record LinkSession(
    bool IsServer,
    int ServerPort,
    int ClientPort,
    int RpcPort,
    int ListenersPort,
    string NetworkName,
    string NetworkSecret,
    string Hostname,
    int DiscoverNodeId,
    IReadOnlyList<string> Peers)
{
    public string ClientAddress => IsServer ? "" : $"localhost:{ClientPort}";

    public LinkInviteInfo Invite => new(
        NetworkName,
        NetworkSecret,
        ServerPort,
        IsServer ? -1 : DiscoverNodeId);
}

public sealed record LinkInviteInfo(
    string NetworkName,
    string NetworkSecret,
    int ServerPort,
    int DiscoverNodeId);

public sealed class EasyTierNode
{
    public int Id { get; init; }

    public string Address { get; init; } = "";

    public bool IsActive { get; init; }

    public bool IsApproved { get; init; }

    public double UsagePercentage { get; init; }

    public double HealthPercentage24H { get; init; }

    public bool AllowRelay { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    [JsonIgnore]
    public double Load => UsagePercentage * Math.Max(0, 110 - HealthPercentage24H);
}
