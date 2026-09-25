using System.Text.Json;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Link;

public sealed class LinkPeerFetcher : ILinkPeerFetcher
{
    public const string PrimaryNodesUrl =
        "https://uptime.easytier.cn/api/nodes?page=1&per_page=1000";
    public const string FallbackNodesUrl =
        "https://easytier.meloong.com/?page=1&per_page=1000";
    private const int DefaultDiscoverNodeId = 1;

    private static readonly EasyTierNode[] BuiltInNodes =
    [
        new EasyTierNode
        {
            Id = DefaultDiscoverNodeId,
            Address = "tcp://public.easytier.cn:11010",
            IsActive = true,
            IsApproved = true,
            AllowRelay = true,
            Tags = ["国内", "MC中继"],
        },
        new EasyTierNode
        {
            Id = 2,
            Address = "tcp://public2.easytier.cn:11010",
            IsActive = true,
            IsApproved = true,
            AllowRelay = true,
            Tags = ["国内", "MC中继"],
        },
        new EasyTierNode
        {
            Id = 3,
            Address = "tcp://mc1.easytier.cn:55558",
            IsActive = true,
            IsApproved = true,
            AllowRelay = true,
            Tags = ["国内", "MC中继"],
        },
    ];

    private readonly IDownloadClient _downloadClient;

    public LinkPeerFetcher(IDownloadClient downloadClient)
    {
        _downloadClient = downloadClient;
    }

    public async Task<IReadOnlyList<string>> ResolvePeersAsync(
        LinkInviteInfo invite,
        bool isServer,
        string customPeer,
        CancellationToken cancellationToken = default)
    {
        var customPeers = SplitCustomPeers(customPeer);
        if (customPeers.Count > 0)
        {
            if (invite.DiscoverNodeId == -2 && isServer)
            {
                throw new InvalidOperationException("房主创建房间时不需要填写自定义节点。");
            }

            return customPeers;
        }

        if (invite.DiscoverNodeId == -2)
        {
            throw new InvalidOperationException(
                "该邀请码需要自定义节点，请在“设置 → 联机”中填写与房主相同的自定义节点。");
        }

        var nodes = await FetchNodesAsync(cancellationToken).ConfigureAwait(false);
        var discover = SelectDiscoverNode(nodes, invite, isServer);
        var result = new List<string> { discover.Address };
        if (!isServer)
        {
            result.AddRange(
                nodes
                    .Where(node => node.AllowRelay && node.Address != discover.Address)
                    .OrderBy(node => node.Load)
                    .Take(2)
                    .Select(node => node.Address));
        }

        return result.Distinct().ToList();
    }

    private async Task<IReadOnlyList<EasyTierNode>> FetchNodesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _downloadClient.GetStringAsync(
                [PrimaryNodesUrl],
                cancellationToken).ConfigureAwait(false);
            var nodes = ParseNodes(raw);
            if (nodes.Count > 0)
            {
                return nodes;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        try
        {
            var raw = await _downloadClient.GetStringAsync(
                [FallbackNodesUrl],
                cancellationToken).ConfigureAwait(false);
            var nodes = ParseNodes(raw);
            if (nodes.Count > 0)
            {
                return nodes;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        return BuiltInNodes;
    }

    public static IReadOnlyList<EasyTierNode> ParseNodes(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var nodes = new List<EasyTierNode>();
        foreach (var element in items.EnumerateArray())
        {
            try
            {
                var node = ParseNode(element);
                if (node is not null && node.IsActive && node.IsApproved)
                {
                    nodes.Add(node);
                }
            }
            catch (JsonException)
            {
            }
        }

        return nodes;
    }

    private static EasyTierNode? ParseNode(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement)
            || !element.TryGetProperty("address", out var addressElement)
            || !element.TryGetProperty("is_active", out var activeElement)
            || !element.TryGetProperty("is_approved", out var approvedElement))
        {
            return null;
        }

        var tags = element.TryGetProperty("tags", out var tagsElement)
                   && tagsElement.ValueKind == JsonValueKind.Array
            ? tagsElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? "")
                .ToArray()
            : [];
        return new EasyTierNode
        {
            Id = idElement.GetInt32(),
            Address = addressElement.GetString() ?? "",
            IsActive = activeElement.GetBoolean(),
            IsApproved = approvedElement.GetBoolean(),
            UsagePercentage = GetDouble(element, "usage_percentage"),
            HealthPercentage24H = GetDouble(element, "health_percentage_24h"),
            AllowRelay = GetBoolean(element, "allow_relay"),
            Tags = tags,
        };
    }

    private static EasyTierNode SelectDiscoverNode(
        IReadOnlyList<EasyTierNode> nodes,
        LinkInviteInfo invite,
        bool isServer)
    {
        var candidate = isServer
            ? nodes
                .Where(node => !node.AllowRelay)
                .OrderBy(node => node.Load)
                .FirstOrDefault()
            : invite.DiscoverNodeId > 0
                ? nodes.FirstOrDefault(node => node.Id == invite.DiscoverNodeId)
                : null;

        if (candidate is not null && !string.IsNullOrWhiteSpace(candidate.Address))
        {
            return candidate;
        }

        return nodes.FirstOrDefault(node => node.Id == DefaultDiscoverNodeId)
               ?? BuiltInNodes[0];
    }

    private static IReadOnlyList<string> SplitCustomPeers(string customPeer)
    {
        return customPeer
            .Split(['，', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();
    }

    private static double GetDouble(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    private static bool GetBoolean(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}
