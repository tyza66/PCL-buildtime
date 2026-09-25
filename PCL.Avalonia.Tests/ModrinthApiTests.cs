using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ModrinthApiTests
{
    private sealed class FakeDownloadClient : IDownloadClient
    {
        public string Response { get; set; } = "";

        public List<string> Urls { get; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            Urls.AddRange(urls);
            return Task.FromResult(Response);
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task SearchProjectsAsync_ParsesHits_AndBuildsFacets()
    {
        var client = new FakeDownloadClient
        {
            Response = """
                {
                  "hits": [
                    {
                      "project_id": "abc",
                      "slug": "jei",
                      "title": "Just Enough Items",
                      "description": "物品与配方查看",
                      "author": "mezz",
                      "downloads": 123456,
                      "follows": 789,
                      "categories": ["fabric", "utility"],
                      "updated": "2024-02-01T00:00:00Z"
                    }
                  ]
                }
                """,
        };
        var api = new ModrinthApi(client);

        var results = await api.SearchProjectsAsync("jei", "1.20.1", "fabric");

        var project = Assert.Single(results);
        Assert.Equal("abc", project.ProjectId);
        Assert.Equal("Just Enough Items", project.Title);
        Assert.Equal("mezz", project.Author);
        Assert.Equal(123456, project.Downloads);
        Assert.Contains("utility", project.Categories);
        Assert.Equal(DateTimeOffset.Parse("2024-02-01T00:00:00Z"), project.UpdatedAt);

        var url = Assert.Single(client.Urls);
        Assert.Contains("query=jei", url);
        var decoded = Uri.UnescapeDataString(url);
        Assert.Contains("versions:1.20.1", decoded);
        Assert.Contains("categories:fabric", decoded);
    }

    [Fact]
    public async Task GetVersionsAsync_ParsesFiles_AndBuildsFilters()
    {
        var client = new FakeDownloadClient
        {
            Response = """
                [
                  {
                    "id": "v1",
                    "project_id": "abc",
                    "name": "JEI 15.2.0.27",
                    "version_number": "15.2.0.27",
                    "date_published": "2024-02-01T00:00:00Z",
                    "game_versions": ["1.20.1"],
                    "loaders": ["fabric"],
                    "files": [
                      {
                        "url": "https://cdn.modrinth.com/data/jei.jar",
                        "filename": "jei-15.2.0.27.jar",
                        "primary": true,
                        "size": 12345,
                        "sha1": "abc123"
                      }
                    ]
                  }
                ]
                """,
        };
        var api = new ModrinthApi(client);

        var versions = await api.GetVersionsAsync("abc", "1.20.1", "fabric");

        var version = Assert.Single(versions);
        Assert.Equal("v1", version.Id);
        Assert.Equal("15.2.0.27", version.VersionNumber);
        Assert.Contains("1.20.1", version.GameVersions);
        var file = Assert.Single(version.Files);
        Assert.True(file.Primary);
        Assert.Equal("jei-15.2.0.27.jar", file.Filename);
        Assert.Equal("abc123", file.Sha1);

        var url = Assert.Single(client.Urls);
        Assert.Contains("project/abc/version", url);
        var decoded = Uri.UnescapeDataString(url);
        Assert.Contains("1.20.1", decoded);
        Assert.Contains("fabric", decoded);
    }
}
