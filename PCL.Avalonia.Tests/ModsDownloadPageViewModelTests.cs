using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class ModsDownloadPageViewModelTests
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

    private sealed class FakeApi : IModrinthApi
    {
        public List<ModrinthProject> Projects { get; set; } = [];

        public List<ModrinthProjectVersion> Versions { get; set; } = [];

        public List<string> SearchQueries { get; } = [];

        public Task<IReadOnlyList<ModrinthProject>> SearchProjectsAsync(
            string query,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
        {
            SearchQueries.Add($"{query}|{gameVersion}|{loader}");
            return Task.FromResult<IReadOnlyList<ModrinthProject>>(Projects);
        }

        public Task<IReadOnlyList<ModrinthProjectVersion>> GetVersionsAsync(
            string projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModrinthProjectVersion>>(Versions);
    }

    private sealed class FakeInstaller : IModsDownloadService
    {
        public ModrinthProjectVersion? LastVersion { get; private set; }

        public string? LastFolder { get; private set; }

        public Task<string> InstallAsync(
            ModrinthProjectVersion version,
            string modsFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastVersion = version;
            LastFolder = modsFolder;
            return Task.FromResult(Path.Combine(modsFolder, "installed.jar"));
        }
    }

    private sealed class FakeCurseForgeApi : ICurseForgeApi
    {
        public List<CurseForgeProject> Projects { get; set; } = [];

        public List<CurseForgeModFile> Files { get; set; } = [];

        public List<string> SearchQueries { get; } = [];

        public Task<IReadOnlyList<CurseForgeProject>> SearchProjectsAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            SearchQueries.Add(query);
            return Task.FromResult<IReadOnlyList<CurseForgeProject>>(Projects);
        }

        public Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
            int projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeModFile>>(Files);
    }

    private sealed class FakeCurseForgeInstaller : ICurseForgeDownloadService
    {
        public CurseForgeModFile? LastFile { get; private set; }

        public string? LastFolder { get; private set; }

        public Task<string> InstallAsync(
            CurseForgeModFile file,
            string modsFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastFile = file;
            LastFolder = modsFolder;
            return Task.FromResult(Path.Combine(modsFolder, "installed.jar"));
        }
    }

    private static ModrinthProject Project() => new()
    {
        ProjectId = "abc",
        Title = "Just Enough Items",
        Description = "物品与配方查看",
        Author = "mezz",
        Downloads = 123456,
    };

    private static ModrinthProjectVersion Version() => new()
    {
        Id = "v1",
        ProjectId = "abc",
        VersionNumber = "15.2.0.27",
        Files = [new ModrinthFile { Url = "https://cdn.modrinth.com/data/jei.jar", Filename = "jei.jar", Primary = true }],
    };

    private static CurseForgeProject CurseProject() => new()
    {
        Id = 123,
        Slug = "jei",
        Name = "Just Enough Items",
        Summary = "物品与配方查看",
        Authors = [new CurseForgeAuthor { Name = "mezz" }],
        DownloadCount = 654321,
    };

    private static CurseForgeModFile CurseFile() => new()
    {
        Id = 456,
        DisplayName = "JEI 15.2.0.27",
        FileName = "jei-15.2.0.27.jar",
        DownloadUrl = "https://edge.forgecdn.net/files/jei.jar",
        FileLength = 12345,
        FileHashes = [new CurseForgeFileHash { Algo = 1, Value = "abc123" }],
        FileDate = DateTimeOffset.Parse("2024-02-01T00:00:00Z"),
    };

    [Fact]
    public async Task SearchCommand_LoadsProjects()
    {
        var api = new FakeApi { Projects = [Project()] };
        var viewModel = CreateViewModel(api, new FakeInstaller());
        viewModel.SearchText = "JEI";

        await viewModel.SearchCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.Projects);
        Assert.Equal("Just Enough Items", item.Title);
        Assert.Contains("JEI|1.20.1|fabric", api.SearchQueries);
        Assert.Contains("找到", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SearchCommand_CurseForgeSource_LoadsProjects()
    {
        var api = new FakeCurseForgeApi { Projects = [CurseProject()] };
        var viewModel = CreateViewModel(new FakeApi(), new FakeInstaller(), api, new FakeCurseForgeInstaller());
        viewModel.SearchText = "JEI";
        viewModel.Source = ModDownloadSource.CurseForge;

        await viewModel.SearchCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.Projects);
        Assert.Equal("Just Enough Items", item.Title);
        Assert.Equal("654.3K 下载", item.DownloadsText);
        Assert.Contains("JEI", api.SearchQueries);
        Assert.Contains("CurseForge", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_InstallsSelectedMod_AndMarksInstalled()
    {
        var installer = new FakeInstaller();
        var api = new FakeApi { Projects = [Project()], Versions = [Version()] };
        var viewModel = CreateViewModel(api, installer);
        viewModel.SearchText = "JEI";
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.NotNull(installer.LastVersion);
        Assert.Equal(Path.Combine("/games/mc", "mods"), installer.LastFolder);
        Assert.True(viewModel.Projects[0].IsInstalled);
        Assert.Contains("已安装", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_CurseForgeSource_InstallsLatestFile()
    {
        var installer = new FakeCurseForgeInstaller();
        var api = new FakeCurseForgeApi { Projects = [CurseProject()], Files = [CurseFile()] };
        var viewModel = CreateViewModel(new FakeApi(), new FakeInstaller(), api, installer);
        viewModel.SearchText = "JEI";
        viewModel.Source = ModDownloadSource.CurseForge;
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.NotNull(installer.LastFile);
        Assert.Equal(456, installer.LastFile?.Id);
        Assert.Equal(Path.Combine("/games/mc", "mods"), installer.LastFolder);
        Assert.True(viewModel.Projects[0].IsInstalled);
        Assert.Contains("已安装", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_NoCompatibleVersion_ShowsMessage()
    {
        var viewModel = CreateViewModel(new FakeApi { Projects = [Project()] }, new FakeInstaller());
        viewModel.SearchText = "JEI";
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.False(viewModel.Projects[0].IsInstalled);
        Assert.Contains("没有适配", viewModel.StatusMessage);
    }

    private static ModsDownloadPageViewModel CreateViewModel(
        FakeApi api,
        FakeInstaller installer,
        FakeCurseForgeApi? curseForgeApi = null,
        FakeCurseForgeInstaller? curseForgeInstaller = null)
        => new(
            new FakeSettingsService(),
            api,
            installer,
            new FakePlatformService(),
            curseForgeApi,
            curseForgeInstaller);
}
