using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class ResourceDownloadPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new() { MinecraftFolder = "/games/mc" };

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/mc";
    }

    private sealed class FakeSearchService : IResourceSearchService
    {
        public IReadOnlyList<ResourceProjectItem> Results { get; set; } = [];

        public Exception? Exception { get; set; }

        public List<(ResourceType Type, string Query, string GameVersion, string Loader, string Tag, ResourceSource Source)> Calls { get; } = [];

        public Task<ResourceSearchResult> SearchAsync(
            ResourceType type,
            string query,
            string gameVersion,
            string loader,
            string tag,
            ResourceSource source,
            int page = 0,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((type, query, gameVersion, loader, tag, source));
            return Exception is null
                ? Task.FromResult(new ResourceSearchResult(Results, Results.Count))
                : Task.FromException<ResourceSearchResult>(Exception);
        }
    }

    private sealed class FakeModrinthApi : IModrinthApi
    {
        public List<ModrinthProjectVersion> Versions { get; set; } = [];

        public List<(string ProjectId, string GameVersion, string Loader)> VersionCalls { get; } = [];

        public Task<ModrinthSearchPage> SearchProjectsAsync(
            string query,
            string gameVersion,
            string loader,
            string projectType = "mod",
            int offset = 0,
            int limit = 40,
            string tag = "",
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ModrinthSearchPage([], 0));

        public Task<IReadOnlyList<ModrinthProjectVersion>> GetVersionsAsync(
            string projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
        {
            VersionCalls.Add((projectId, gameVersion, loader));
            return Task.FromResult<IReadOnlyList<ModrinthProjectVersion>>(Versions);
        }
    }

    private sealed class FakeCurseForgeApi : ICurseForgeApi
    {
        public List<CurseForgeModFile> Files { get; set; } = [];

        public List<(int ProjectId, string GameVersion, string Loader)> FileCalls { get; } = [];

        public Task<CurseForgeSearchPage> SearchProjectsAsync(
            string query,
            int classId = 6,
            string gameVersion = "",
            string loader = "",
            string categoryId = "",
            int index = 0,
            int pageSize = 40,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CurseForgeSearchPage([], 0));

        public Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
            int projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
        {
            FileCalls.Add((projectId, gameVersion, loader));
            return Task.FromResult<IReadOnlyList<CurseForgeModFile>>(Files);
        }

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

    private sealed class FakeDownloadService : IResourceDownloadService
    {
        public ResourceType? LastType { get; private set; }

        public ResourceFileItem? LastFile { get; private set; }

        public string? LastFolder { get; private set; }

        public TaskCompletionSource? Gate { get; set; }

        public async Task<string> InstallAsync(
            ResourceType type,
            ResourceFileItem file,
            string minecraftFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastType = type;
            LastFile = file;
            LastFolder = minecraftFolder;
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Path.Combine(minecraftFolder, file.FileName);
        }
    }

    private static ResourceProjectItem Project(
        ResourceSource source = ResourceSource.Modrinth,
        ResourceType type = ResourceType.ResourcePack)
        => new()
        {
            ProjectId = source == ResourceSource.CurseForge ? "123" : "abc",
            Title = "Faithful",
            Description = "高清原版纹理",
            AuthorText = "作者",
            DownloadsText = "1K 下载",
            CategoriesText = "资源",
            Source = source,
            Type = type,
        };

    private static ModrinthProjectVersion Version() => new()
    {
        Id = "v1",
        ProjectId = "abc",
        Name = "Faithful 1.0",
        Files =
        [
            new ModrinthFile
            {
                Url = "https://cdn.modrinth.com/data/faithful.zip",
                Filename = "faithful.zip",
                Primary = true,
                Size = 12345,
                Sha1 = "abc123",
            },
        ],
    };

    private static CurseForgeModFile CurseFile() => new()
    {
        Id = 456,
        DisplayName = "BSL 1.0",
        FileName = "bsl.zip",
        DownloadUrl = "https://edge.forgecdn.net/files/bsl.zip",
        FileLength = 12345,
        FileHashes = [new CurseForgeFileHash { Algo = 1, Value = "abc123" }],
        FileDate = DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
    };

    private static ResourceDownloadPageViewModel CreateViewModel(
        FakeSearchService search,
        FakeDownloadService download,
        FakeModrinthApi? modrinthApi = null,
        FakeCurseForgeApi? curseForgeApi = null)
        => new(
            new FakeSettingsService(),
            search,
            download,
            new FakePlatformService(),
            modrinthApi ?? new FakeModrinthApi(),
            curseForgeApi ?? new FakeCurseForgeApi());

    [Fact]
    public async Task SearchCommand_LoadsProjectsAndPassesFilters()
    {
        var search = new FakeSearchService { Results = [Project()] };
        var viewModel = CreateViewModel(search, new FakeDownloadService());
        viewModel.ResourceType = viewModel.ResourceTypeOptions.Single(option => option.Value == ResourceType.ResourcePack);
        viewModel.SearchText = "faithful";
        viewModel.GameVersion = "1.20.1";
        viewModel.Loader = "fabric";

        await viewModel.SearchCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.Projects);
        Assert.Equal("Faithful", item.Title);
        Assert.Equal("高清原版纹理", item.Description);
        var call = Assert.Single(search.Calls);
        Assert.Equal(
            (ResourceType.ResourcePack, "faithful", "1.20.1", "fabric", ResourceSource.Modrinth),
            (call.Type, call.Query, call.GameVersion, call.Loader, call.Source));
        Assert.Contains("找到 1 个资源包", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_Modrinth_InstallsSelectedProject()
    {
        var download = new FakeDownloadService();
        var api = new FakeModrinthApi { Versions = [Version()] };
        var viewModel = CreateViewModel(
            new FakeSearchService { Results = [Project()] },
            download,
            api);
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.NotNull(download.LastFile);
        Assert.Equal(ResourceType.ResourcePack, download.LastType);
        Assert.Equal("faithful.zip", download.LastFile?.FileName);
        Assert.Equal("/games/mc", download.LastFolder);
        var versionCall = Assert.Single(api.VersionCalls);
        Assert.Equal(("abc", "1.20.1", ""), (versionCall.ProjectId, versionCall.GameVersion, versionCall.Loader));
        Assert.True(viewModel.Projects[0].IsInstalled);
        Assert.Contains("已安装 Faithful", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_CurseForge_InstallsSelectedFile()
    {
        var download = new FakeDownloadService();
        var api = new FakeCurseForgeApi { Files = [CurseFile()] };
        var viewModel = CreateViewModel(
            new FakeSearchService { Results = [Project(ResourceSource.CurseForge, ResourceType.Shader)] },
            download,
            curseForgeApi: api);
        viewModel.Source = viewModel.SourceOptions.Single(option => option.Value == ResourceSource.CurseForge);
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.Equal(ResourceType.Shader, download.LastType);
        Assert.Equal("bsl.zip", download.LastFile?.FileName);
        Assert.Equal("/games/mc", download.LastFolder);
        var fileCall = Assert.Single(api.FileCalls);
        Assert.Equal((123, "1.20.1", ""), (fileCall.ProjectId, fileCall.GameVersion, fileCall.Loader));
        Assert.True(viewModel.Projects[0].IsInstalled);
    }

    [Fact]
    public async Task InstallCommand_NoCompatibleVersion_ShowsMessage()
    {
        var viewModel = CreateViewModel(
            new FakeSearchService { Results = [Project()] },
            new FakeDownloadService());
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.False(viewModel.Projects[0].IsInstalled);
        Assert.Contains("没有适配", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_ShowsProgressWhileInstalling()
    {
        var download = new FakeDownloadService { Gate = new TaskCompletionSource() };
        var viewModel = CreateViewModel(
            new FakeSearchService { Results = [Project()] },
            download,
            new FakeModrinthApi { Versions = [Version()] });
        await viewModel.SearchCommand.ExecuteAsync(null);

        var installTask = viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsProgressVisible);
        Assert.True(viewModel.IsBusy);
        download.Gate.SetResult();

        await installTask;

        Assert.False(viewModel.IsProgressVisible);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.Projects[0].IsInstalled);
    }

    [Fact]
    public void ResourceTypeChanged_ClearsProjectsAndHidesLoader()
    {
        var viewModel = CreateViewModel(new FakeSearchService(), new FakeDownloadService());
        viewModel.Projects.Add(new ResourceProjectItemViewModel(Project(), _ => Task.CompletedTask));
        viewModel.StatusMessage = "旧状态";

        Assert.True(viewModel.IsLoaderVisible);
        viewModel.ResourceType = viewModel.ResourceTypeOptions.Single(option => option.Value == ResourceType.Shader);

        Assert.Empty(viewModel.Projects);
        Assert.Equal("", viewModel.StatusMessage);
        Assert.False(viewModel.IsLoaderVisible);
        Assert.Equal("any", viewModel.Loader);
    }
}
