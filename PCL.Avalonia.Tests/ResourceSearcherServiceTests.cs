using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.Tests;

public sealed class ResourceSearcherServiceTests
{
    private sealed class FakeModrinthApi : IModrinthApi
    {
        public IReadOnlyList<ModrinthProject> Projects { get; set; } = [];

        public Exception? Exception { get; set; }

        public List<(string Query, string GameVersion, string Loader, string ProjectType)> Calls { get; } = [];

        public Task<ModrinthSearchPage> SearchProjectsAsync(
            string query,
            string gameVersion,
            string loader,
            string projectType = "mod",
            int offset = 0,
            int limit = 40,
            string tag = "",
            CancellationToken cancellationToken = default)
        {
            Calls.Add((query, gameVersion, loader, projectType));
            return Exception is null
                ? Task.FromResult(new CurseForgeSearchPage(Projects, Projects.Count))
                : Task.FromException<ModrinthSearchPage>(Exception);
        }

        public Task<IReadOnlyList<ModrinthProjectVersion>> GetVersionsAsync(
            string projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModrinthProjectVersion>>([]);
    }

    private sealed class FakeCurseForgeApi : ICurseForgeApi
    {
        public IReadOnlyList<CurseForgeProject> Projects { get; set; } = [];

        public Exception? Exception { get; set; }

        public List<(string Query, int ClassId)> Calls { get; } = [];

        public Task<CurseForgeSearchPage> SearchProjectsAsync(
            string query,
            int classId = 6,
            string gameVersion = "",
            string loader = "",
            string categoryId = "",
            int index = 0,
            int pageSize = 40,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((query, classId));
            return Exception is null
                ? Task.FromResult(new CurseForgeSearchPage(Projects, Projects.Count))
                : Task.FromException<CurseForgeSearchPage>(Exception);
        }

        public Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
            int projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeModFile>>([]);

        public Task<IReadOnlyList<CurseForgeModFile>> GetModpackFilesAsync(
            int projectId,
            string gameVersion,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeModFile>>([]);

        public Task<CurseForgeModFile?> GetFileAsync(
            int projectId,
            int fileId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<CurseForgeModFile?>(null);
    }

    private static ModrinthProject ModrinthProject(string id, string title) => new()
    {
        ProjectId = id,
        Title = title,
        Description = "描述",
        Author = "作者",
        Downloads = 123456,
        Categories = ["utility"],
    };

    private static CurseForgeProject CurseForgeProject(int id, string title) => new()
    {
        Id = id,
        Slug = title.ToLowerInvariant(),
        Name = title,
        Summary = "描述",
        Authors = [new CurseForgeAuthor { Name = "作者" }],
        DownloadCount = 654321,
        Categories = ["Utility"],
    };

    [Fact]
    public async Task SearchAsync_Modrinth_ResourcePack_PassesProjectTypeAndSkipsLoader()
    {
        var api = new FakeModrinthApi { Projects = [ModrinthProject("abc", "Faithful")] };
        var service = new ResourceSearcherService(api, new FakeCurseForgeApi());

        var results = await service.SearchAsync(
            ResourceType.ResourcePack,
            "faithful",
            "1.20.1",
            "fabric",
            ResourceSource.Modrinth);

        var item = Assert.Single(results);
        Assert.Equal(ResourceSource.Modrinth, item.Source);
        Assert.Equal(ResourceType.ResourcePack, item.Type);
        var (query, gameVersion, loader, projectType) = Assert.Single(api.Calls);
        Assert.Equal("faithful", query);
        Assert.Equal("1.20.1", gameVersion);
        Assert.Equal("", loader);
        Assert.Equal("resourcepack", projectType);
        Assert.Equal("123.5K 下载", item.DownloadsText);
    }

    [Fact]
    public async Task SearchAsync_Modrinth_Mod_PassesLoader()
    {
        var api = new FakeModrinthApi { Projects = [ModrinthProject("abc", "JEI")] };
        var service = new ResourceSearcherService(api, new FakeCurseForgeApi());

        var results = await service.SearchAsync(
            ResourceType.Mod,
            "jei",
            "1.20.1",
            "fabric",
            ResourceSource.Modrinth);

        var item = Assert.Single(results);
        Assert.Equal("JEI", item.Title);
        var (_, _, loader, projectType) = Assert.Single(api.Calls);
        Assert.Equal("fabric", loader);
        Assert.Equal("mod", projectType);
        Assert.Equal("123.5K 下载", item.DownloadsText);
    }

    [Fact]
    public async Task SearchAsync_CurseForge_Shader_PassesClassId()
    {
        var api = new FakeCurseForgeApi { Projects = [CurseForgeProject(123, "BSL")] };
        var service = new ResourceSearcherService(new FakeModrinthApi(), api);

        var results = await service.SearchAsync(
            ResourceType.Shader,
            "bsl",
            "1.20.1",
            "fabric",
            ResourceSource.CurseForge);

        var item = Assert.Single(results);
        Assert.Equal(ResourceSource.CurseForge, item.Source);
        Assert.Equal(ResourceType.Shader, item.Type);
        Assert.Equal("654.3K 下载", item.DownloadsText);
        var (query, classId) = Assert.Single(api.Calls);
        Assert.Equal("bsl", query);
        Assert.Equal(6552, classId);
    }

    [Fact]
    public async Task SearchAsync_ModrinthFailure_WithNoResults_ThrowsAggregatedError()
    {
        var api = new FakeModrinthApi { Exception = new InvalidOperationException("network down") };
        var service = new ResourceSearcherService(api, new FakeCurseForgeApi());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchAsync(
                ResourceType.Mod,
                "jei",
                "1.20.1",
                "fabric",
                ResourceSource.Modrinth));

        Assert.Contains("Modrinth:network down", exception.Message);
    }
}
