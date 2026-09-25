using System.Text.Json;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class CurseForgeApiTests
{
    private sealed class FakeDownloadClient : IDownloadClient
    {
        public string PostResponse { get; set; } = "";

        public string GetResponse { get; set; } = "";

        public List<string> PostUrls { get; } = [];

        public List<string> PostBodies { get; } = [];

        public List<string> GetUrls { get; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            GetUrls.AddRange(urls);
            return Task.FromResult(GetResponse);
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
        {
            PostUrls.AddRange(urls);
            PostBodies.Add(json);
            return Task.FromResult(PostResponse);
        }
    }

    [Fact]
    public async Task SearchProjectsAsync_ParsesProjects_AndPostsSearchBody()
    {
        var client = new FakeDownloadClient
        {
            PostResponse = """
                {
                  "data": [
                    {
                      "id": 123,
                      "slug": "jei",
                      "name": "Just Enough Items",
                      "summary": "物品与配方查看",
                      "logo": { "url": "https://example.com/jei.png" },
                      "authors": [ { "name": "mezz" } ],
                      "downloadCount": 654321,
                      "categorySections": [
                        { "name": "Fabric", "projectId": 123 },
                        { "name": "Utility" }
                      ]
                    }
                  ]
                }
                """,
        };
        var api = new CurseForgeApi(client);

        var results = await api.SearchProjectsAsync("jei");

        var project = Assert.Single(results);
        Assert.Equal(123, project.Id);
        Assert.Equal("Just Enough Items", project.Name);
        Assert.Equal("物品与配方查看", project.Summary);
        Assert.Equal("mezz", Assert.Single(project.Authors).Name);
        Assert.Equal(654321, project.DownloadCount);

        var url = Assert.Single(client.PostUrls);
        Assert.Equal("https://api.curseforge.com/v1/mods/search", url);
        var body = Assert.Single(client.PostBodies);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(432, json.RootElement.GetProperty("gameId").GetInt32());
        Assert.Equal(6, json.RootElement.GetProperty("classId").GetInt32());
        Assert.Equal("jei", json.RootElement.GetProperty("searchFilter").GetString());
    }

    [Fact]
    public async Task SearchProjectsAsync_SupportsClassId()
    {
        var client = new FakeDownloadClient { PostResponse = """{ "data": [] }""" };
        var api = new CurseForgeApi(client);

        await api.SearchProjectsAsync("pack", classId: 4471);

        var body = Assert.Single(client.PostBodies);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(4471, json.RootElement.GetProperty("classId").GetInt32());
    }

    [Fact]
    public async Task GetFilesAsync_ParsesFiles_AndBuildsFilters()
    {
        var client = new FakeDownloadClient
        {
            GetResponse = """
                {
                  "data": [
                    {
                      "id": 456,
                      "displayName": "JEI 15.2.0.27",
                      "fileName": "jei-15.2.0.27.jar",
                      "downloadUrl": "https://edge.forgecdn.net/files/jei.jar",
                      "fileLength": 12345,
                      "fileDate": "2024-02-01T00:00:00Z",
                      "fileHashes": [
                        { "algo": 1, "value": "abc123" }
                      ]
                    }
                  ]
                }
                """,
        };
        var api = new CurseForgeApi(client);

        var files = await api.GetFilesAsync(123, "1.20.1", "fabric");

        var file = Assert.Single(files);
        Assert.Equal(456, file.Id);
        Assert.Equal("jei-15.2.0.27.jar", file.FileName);
        Assert.Equal("https://edge.forgecdn.net/files/jei.jar", file.DownloadUrl);
        Assert.Equal(12345, file.FileLength);
        Assert.Equal("abc123", file.Sha1);
        Assert.Equal(DateTimeOffset.Parse("2024-02-01T00:00:00Z"), file.FileDate);

        var url = Assert.Single(client.GetUrls);
        Assert.Contains("/v1/mods/123/files", url);
        var decoded = Uri.UnescapeDataString(url);
        Assert.Contains("gameVersion=1.20.1", decoded);
        Assert.Contains("modLoaderType=4", decoded);
    }

    [Fact]
    public async Task GetModpackFilesAsync_FiltersModpackFiles()
    {
        var client = new FakeDownloadClient
        {
            GetResponse = """
                {
                  "data": [
                    {
                      "id": 101,
                      "displayName": "Example Pack 1.0.0",
                      "fileName": "example-pack-1.0.0-modpack.zip",
                      "downloadUrl": "https://edge.forgecdn.net/files/pack.zip",
                      "fileLength": 999,
                      "fileDate": "2024-02-01T00:00:00Z",
                      "fileHashes": [ { "algo": 1, "value": "abc123" } ]
                    },
                    {
                      "id": 102,
                      "displayName": "Not A Pack",
                      "fileName": "helper.jar",
                      "downloadUrl": "https://edge.forgecdn.net/files/helper.jar",
                      "fileLength": 10
                    }
                  ]
                }
                """,
        };
        var api = new CurseForgeApi(client);

        var files = await api.GetModpackFilesAsync(999, "1.20.1");

        var file = Assert.Single(files);
        Assert.Equal("example-pack-1.0.0-modpack.zip", file.FileName);
        Assert.Equal("abc123", file.Sha1);
        var url = Assert.Single(client.GetUrls);
        Assert.Contains("/v1/mods/999/files", url);
        Assert.Contains("gameVersion=1.20.1", Uri.UnescapeDataString(url));
    }

    [Fact]
    public async Task GetFileAsync_ParsesSingleFile_AndBuildsUrl()
    {
        var client = new FakeDownloadClient
        {
            GetResponse = """
                {
                  "data": {
                    "id": 456,
                    "displayName": "JEI 15.2.0.27",
                    "fileName": "jei-15.2.0.27.jar",
                    "downloadUrl": "https://edge.forgecdn.net/files/jei.jar",
                    "fileLength": 12345,
                    "fileHashes": [ { "algo": 1, "value": "abc123" } ]
                  }
                }
                """,
        };
        var api = new CurseForgeApi(client);

        var file = await api.GetFileAsync(123, 456);

        Assert.NotNull(file);
        Assert.Equal(456, file!.Id);
        Assert.Equal("jei-15.2.0.27.jar", file.FileName);
        Assert.Equal("https://edge.forgecdn.net/files/jei.jar", file.DownloadUrl);
        Assert.Equal("abc123", file.Sha1);
        Assert.Equal("https://api.curseforge.com/v1/mods/123/files/456", Assert.Single(client.GetUrls));
    }

    [Fact]
    public async Task GetFileAsync_EmptyResponse_ReturnsNull()
    {
        var client = new FakeDownloadClient { GetResponse = """{ "data": null }""" };
        var api = new CurseForgeApi(client);

        var file = await api.GetFileAsync(123, 456);

        Assert.Null(file);
    }
}
