using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Tests;

public sealed class VersionJavaInfoServiceTests
{
    [Fact]
    public async Task GetRequiredJavaMajorAsync_ReadsMajorVersion_FromMojangVersionJson()
    {
        var client = new FakeDownloadClient
        {
            Json = "{\"javaVersion\": {\"component\": \"jre-legacy\", \"majorVersion\": 21}}",
        };
        var service = new VersionJavaInfoService(client);
        var entry = new VersionManifestEntry
        {
            Id = "1.20.6",
            Url = "https://piston-meta.mojang.com/v1/packages/abc/1.20.6.json",
        };

        var major = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.20.6");

        Assert.Equal(21, major);
        var request = Assert.Single(client.Requests);
        Assert.Equal(entry.Url, Assert.Single(request));
    }

    [Fact]
    public async Task GetRequiredJavaMajorAsync_BmclapiSource_PrefersTheMirrorBeforeTheOriginal()
    {
        var client = new FakeDownloadClient
        {
            Json = "{\"javaVersion\": {\"majorVersion\": 17}}",
        };
        var service = new VersionJavaInfoService(client);
        var entry = new VersionManifestEntry
        {
            Id = "1.16.5",
            Url = "https://piston-meta.mojang.com/v1/packages/def/1.16.5.json",
        };

        var major = await service.GetRequiredJavaMajorAsync(DownloadSource.Bmclapi, entry, "1.16.5");

        Assert.Equal(17, major);
        var request = Assert.Single(client.Requests);
        Assert.Equal("https://bmclapi2.bangbang93.com/version/1.16.5/json", request[0]);
        Assert.Contains(entry.Url, request);
    }

    [Fact]
    public async Task GetRequiredJavaMajorAsync_CachesPerVersion()
    {
        var client = new FakeDownloadClient
        {
            Json = "{\"javaVersion\": {\"majorVersion\": 21}}",
        };
        var service = new VersionJavaInfoService(client);
        // 缓存按版本 ID 记，但 Mojang 源没有 entry.Url 就发不出请求，这里必须给带 URL 的 entry。
        var entry = new VersionManifestEntry
        {
            Id = "1.20.6",
            Url = "https://piston-meta.mojang.com/v1/packages/abc/1.20.6.json",
        };

        var first = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.20.6");
        var second = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.20.6");

        Assert.Equal(21, first);
        Assert.Equal(21, second);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task GetRequiredJavaMajorAsync_MissingJavaVersion_ReturnsNullAndCachesIt()
    {
        var client = new FakeDownloadClient { Json = "{\"id\": \"1.12.2\"}" };
        var service = new VersionJavaInfoService(client);
        var entry = new VersionManifestEntry
        {
            Id = "1.12.2",
            Url = "https://piston-meta.mojang.com/v1/packages/old/1.12.2.json",
        };

        var major = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.12.2");
        var again = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.12.2");

        Assert.Null(major);
        Assert.Null(again);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task GetRequiredJavaMajorAsync_FailureIsNotCached_SoTheRetryCanStillSucceed()
    {
        var client = new FakeDownloadClient
        {
            Exception = new HttpRequestException("no route to host"),
        };
        var service = new VersionJavaInfoService(client);
        var entry = new VersionManifestEntry
        {
            Id = "1.20.6",
            Url = "https://piston-meta.mojang.com/v1/packages/abc/1.20.6.json",
        };

        var failed = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.20.6");
        client.Exception = null;
        client.Json = "{\"javaVersion\": {\"majorVersion\": 21}}";
        var retried = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.20.6");

        Assert.Null(failed);
        Assert.Equal(21, retried);
        Assert.Equal(2, client.Requests.Count);
    }

    [Fact]
    public async Task GetRequiredJavaMajorAsync_CancellationPropagates_AndIsNotCached()
    {
        var client = new FakeDownloadClient
        {
            Json = "{\"javaVersion\": {\"majorVersion\": 21}}",
        };
        var service = new VersionJavaInfoService(client);
        var entry = new VersionManifestEntry
        {
            Id = "1.20.6",
            Url = "https://piston-meta.mojang.com/v1/packages/abc/1.20.6.json",
        };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.20.6", cancelled.Token));

        var major = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, entry, "1.20.6");
        Assert.Equal(21, major);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task GetRequiredJavaMajorAsync_BlankVersionId_MakesNoRequest()
    {
        var client = new FakeDownloadClient();
        var service = new VersionJavaInfoService(client);

        var major = await service.GetRequiredJavaMajorAsync(DownloadSource.Bmclapi, null, "   ");

        Assert.Null(major);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task GetRequiredJavaMajorAsync_MojangEntryWithoutUrl_ReturnsNullInsteadOfThrowing()
    {
        var client = new FakeDownloadClient();
        var service = new VersionJavaInfoService(client);

        var major = await service.GetRequiredJavaMajorAsync(DownloadSource.Mojang, null, "1.20.6");

        Assert.Null(major);
        Assert.Empty(client.Requests);
    }

    private sealed class FakeDownloadClient : IDownloadClient
    {
        public string Json { get; set; } = "{\"id\": \"probe\"}";

        public Exception? Exception { get; set; }

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(urls);
            return Exception is null
                ? Task.FromResult(Json)
                : Task.FromException<string>(Exception);
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
