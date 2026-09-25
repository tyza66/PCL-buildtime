using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Link;

namespace PCL.Avalonia.Tests;

public sealed class LinkPeerFetcherTests
{
    private const string ApiJson = """
        {
          "data": {
            "items": [
              {
                "id": 2,
                "address": "tcp://discover.easytier.cn:11010",
                "is_active": true,
                "is_approved": true,
                "usage_percentage": 30,
                "health_percentage_24h": 95,
                "allow_relay": false,
                "tags": ["国内", "MC中继"]
              },
              {
                "id": 7,
                "address": "tcp://relay.easytier.cn:11010",
                "is_active": true,
                "is_approved": true,
                "usage_percentage": 40,
                "health_percentage_24h": 90,
                "allow_relay": true,
                "tags": ["国内", "MC中继"]
              },
              {
                "id": 9,
                "address": "tcp://inactive.example:11010",
                "is_active": false,
                "is_approved": true,
                "usage_percentage": 0,
                "health_percentage_24h": 10,
                "allow_relay": false,
                "tags": ["国内", "MC中继"]
              }
            ]
          }
        }
        """;

    private sealed class FakeDownloadClient : IDownloadClient
    {
        private readonly IReadOnlyList<(string Url, string Response)> _responses;

        public FakeDownloadClient(params (string Url, string Response)[] responses)
        {
            _responses = responses;
        }

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            foreach (var url in urls)
            {
                var response = _responses.FirstOrDefault(item => item.Url == url);
                if (response.Url is not null)
                {
                    return Task.FromResult(response.Response);
                }
            }

            throw new HttpRequestException("not found");
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task ResolvePeers_Host_UsesBestDiscoverNodeWithoutRelays()
    {
        var fetcher = new LinkPeerFetcher(new FakeDownloadClient(
            ("https://uptime.easytier.cn/api/nodes?page=1&per_page=1000", ApiJson)));

        var peers = await fetcher.ResolvePeersAsync(
            new LinkInviteInfo("P63D9-ABCDE", "12345", 25565, -1),
            isServer: true,
            customPeer: "");

        Assert.Equal(["tcp://discover.easytier.cn:11010"], peers);
    }

    [Fact]
    public async Task ResolvePeers_Client_UsesInviteNodeAndAddsRelay()
    {
        var fetcher = new LinkPeerFetcher(new FakeDownloadClient(
            ("https://uptime.easytier.cn/api/nodes?page=1&per_page=1000", ApiJson)));

        var peers = await fetcher.ResolvePeersAsync(
            new LinkInviteInfo("P63D9-ABCDE", "12345", 25565, 2),
            isServer: false,
            customPeer: "");

        Assert.Contains("tcp://discover.easytier.cn:11010", peers);
        Assert.Contains("tcp://relay.easytier.cn:11010", peers);
    }

    [Fact]
    public async Task ResolvePeers_CustomNodeInvite_WithoutCustomPeer_Throws()
    {
        var fetcher = new LinkPeerFetcher(new FakeDownloadClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fetcher.ResolvePeersAsync(
                new LinkInviteInfo("P63D9-ABCDE", "12345", 25565, -2),
                isServer: false,
                customPeer: ""));

        Assert.Contains("自定义节点", exception.Message);
    }

    [Fact]
    public async Task ResolvePeers_ApiUnavailable_UsesBuiltInFallback()
    {
        var fetcher = new LinkPeerFetcher(new FakeDownloadClient());

        var peers = await fetcher.ResolvePeersAsync(
            new LinkInviteInfo("P63D9-ABCDE", "12345", 25565, 0),
            isServer: false,
            customPeer: "");

        Assert.NotEmpty(peers);
        Assert.Contains("tcp://public.easytier.cn:11010", peers);
    }
}
