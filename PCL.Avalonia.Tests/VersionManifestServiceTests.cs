using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Tests;

public sealed class VersionManifestServiceTests
{
    private sealed class FakeDownloadClient : IDownloadClient
    {
        public string Json { get; set; } = "{}";

        public IReadOnlyList<string>? LastUrls { get; private set; }

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            LastUrls = urls;
            return Task.FromResult(Json);
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task GetManifestAsync_ParsesVersionsAndLatest()
    {
        var client = new FakeDownloadClient
        {
            Json = """
                {
                  "latest": { "release": "1.21", "snapshot": "24w10a" },
                  "versions": [
                    {
                      "id": "1.21",
                      "type": "release",
                      "url": "https://example.com/1.21.json",
                      "releaseTime": "2024-08-01T00:00:00Z",
                      "sha1": "abc"
                    },
                    {
                      "id": "24w10a",
                      "type": "snapshot",
                      "url": "https://example.com/24w10a.json",
                      "releaseTime": "2024-03-06T12:00:00Z"
                    }
                  ]
                }
                """,
        };
        var service = new VersionManifestService(client);

        var manifest = await service.GetManifestAsync(DownloadSource.Mojang);

        Assert.Equal("1.21", manifest.Latest?.Release);
        Assert.Equal("24w10a", manifest.Latest?.Snapshot);
        Assert.Equal(2, manifest.Versions.Count);
        Assert.Equal("1.21", manifest.Versions[0].Id);
        Assert.Equal("release", manifest.Versions[0].Type);
        Assert.Equal("https://example.com/1.21.json", manifest.Versions[0].Url);
        Assert.Equal("abc", manifest.Versions[0].Sha1);
        Assert.Equal(DownloadUrlResolver.MojangManifestUrl, client.LastUrls?.Single());
    }

    [Fact]
    public async Task GetManifestAsync_EmptyJson_ReturnsEmptyManifest()
    {
        var service = new VersionManifestService(new FakeDownloadClient { Json = "{}" });

        var manifest = await service.GetManifestAsync(DownloadSource.Bmclapi);

        Assert.Empty(manifest.Versions);
        Assert.Null(manifest.Latest);
    }
}
