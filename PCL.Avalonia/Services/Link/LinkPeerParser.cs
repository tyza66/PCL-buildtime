using System.Globalization;
using System.Text.Json;

namespace PCL.Avalonia.Services.Link;

public static class LinkPeerParser
{
    public static IReadOnlyList<LinkPeer> ParsePeers(string json)
    {
        var peers = new List<LinkPeer>();
        using var document = ParseDocument(json);
        if (document is null)
        {
            return peers;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return peers;
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            try
            {
                peers.Add(ParsePeer(element));
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException)
            {
                // 单条节点信息格式异常不影响其他节点。
            }
        }

        return peers;
    }

    private static JsonDocument? ParseDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static LinkPeer ParsePeer(JsonElement element)
    {
        var hostname = GetString(element, "hostname") ?? "";
        var cost = GetString(element, "cost") ?? "";
        var ipv4 = GetString(element, "ipv4") ?? "";
        if (string.IsNullOrWhiteSpace(hostname) && string.IsNullOrWhiteSpace(cost))
        {
            throw new InvalidDataException("节点缺少必要字段");
        }

        var peerType = cost.Contains("Local", StringComparison.OrdinalIgnoreCase)
            ? LinkPeerType.Self
            : hostname.StartsWith("Client", StringComparison.OrdinalIgnoreCase)
                ? LinkPeerType.Client
                : ipv4 == "10.114.114.114" || hostname.StartsWith("Server", StringComparison.OrdinalIgnoreCase)
                    ? LinkPeerType.Server
                    : LinkPeerType.Misc;

        var ping = 0d;
        var latText = GetString(element, "lat_ms");
        if (latText is not null)
        {
            double.TryParse(latText, NumberStyles.Any, CultureInfo.InvariantCulture, out ping);
        }

        var relay = cost.Contains("relay", StringComparison.OrdinalIgnoreCase);
        var natType = ParseNatType(GetString(element, "nat_type"));
        return new LinkPeer(peerType, hostname, ping, relay, natType);
    }

    private static LinkNatType ParseNatType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return LinkNatType.Pending;
        }

        if (int.TryParse(value, out var numeric))
        {
            return Enum.IsDefined(typeof(LinkNatType), numeric)
                ? (LinkNatType)numeric
                : LinkNatType.Pending;
        }

        return Enum.TryParse<LinkNatType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : LinkNatType.Pending;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }
}
