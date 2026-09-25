using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class IntegrationPacksPageViewModelTests
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

    private sealed class FakeModpackService : ICurseForgeModpackService
    {
        public List<CurseForgeProject> Projects { get; set; } = [];

        public bool FailInstall { get; set; }

        public CurseForgeProject? LastProject { get; private set; }

        public string? LastGameVersion { get; private set; }

        public string? LastFolder { get; private set; }

        public Task<IReadOnlyList<CurseForgeProject>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CurseForgeProject>>(Projects);

        public Task<string> InstallAsync(
            CurseForgeProject project,
            string gameVersion,
            string minecraftFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (FailInstall)
            {
                throw new InvalidOperationException("下载失败");
            }

            LastProject = project;
            LastGameVersion = gameVersion;
            LastFolder = minecraftFolder;
            return Task.FromResult(Path.Combine(minecraftFolder, "downloads", "pack.zip"));
        }
    }

    private sealed class FakeModpackInstaller : IModpackInstallerService
    {
        public bool Fail { get; set; }

        public string? LastZipPath { get; private set; }

        public string? LastFolder { get; private set; }

        public DownloadSource? LastSource { get; private set; }

        public Task<ModpackInstallResult> InstallAsync(
            string modpackZipPath,
            string minecraftFolder,
            DownloadSource source,
            IProgress<ModpackInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Fail)
            {
                throw new InvalidOperationException("安装失败");
            }

            LastZipPath = modpackZipPath;
            LastFolder = minecraftFolder;
            LastSource = source;
            return Task.FromResult(new ModpackInstallResult("Example Pack", "1.20.1", ["mod-a.jar"], []));
        }
    }

    private static CurseForgeProject Project() => new()
    {
        Id = 999,
        Slug = "example-pack",
        Name = "Example Pack",
        Summary = "示例整合包",
        Authors = [new CurseForgeAuthor { Name = "author" }],
        DownloadCount = 12345,
    };

    [Fact]
    public async Task SearchCommand_LoadsProjects()
    {
        var service = new FakeModpackService { Projects = [Project()] };
        var viewModel = CreateViewModel(service);
        viewModel.SearchText = "example";

        await viewModel.SearchCommand.ExecuteAsync(null);

        var item = Assert.Single(viewModel.Projects);
        Assert.Equal("Example Pack", item.Title);
        Assert.Contains("找到", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadCommand_DownloadsPack_AndMarksDownloaded()
    {
        var service = new FakeModpackService { Projects = [Project()] };
        var viewModel = CreateViewModel(service, new FakeModpackInstaller());
        viewModel.SearchText = "example";
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].DownloadCommand.ExecuteAsync(null);

        Assert.Equal(999, service.LastProject?.Id);
        Assert.Equal("1.20.1", service.LastGameVersion);
        Assert.Equal("/games/mc", service.LastFolder);
        Assert.True(viewModel.Projects[0].IsDownloaded);
        Assert.False(viewModel.Projects[0].IsInstalled);
        Assert.Contains("已下载", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DownloadCommand_Failure_ShowsMessageAndKeepsUninstalled()
    {
        var service = new FakeModpackService { Projects = [Project()] };
        service.FailInstall = true;
        var viewModel = CreateViewModel(service, new FakeModpackInstaller());
        viewModel.SearchText = "example";
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].DownloadCommand.ExecuteAsync(null);

        Assert.False(viewModel.Projects[0].IsDownloaded);
        Assert.Contains("失败", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_InstallsModpack_AfterDownloadingZip()
    {
        var installer = new FakeModpackInstaller();
        var service = new FakeModpackService { Projects = [Project()] };
        var viewModel = CreateViewModel(service, installer);
        viewModel.SearchText = "example";
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine("/games/mc", "downloads", "pack.zip"), installer.LastZipPath);
        Assert.Equal("/games/mc", installer.LastFolder);
        Assert.Equal(DownloadSource.Bmclapi, installer.LastSource);
        Assert.True(viewModel.Projects[0].IsDownloaded);
        Assert.True(viewModel.Projects[0].IsInstalled);
        Assert.Contains("已安装整合包 Example Pack", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallCommand_Failure_ShowsMessageAndKeepsUninstalled()
    {
        var installer = new FakeModpackInstaller { Fail = true };
        var viewModel = CreateViewModel(new FakeModpackService { Projects = [Project()] }, installer);
        viewModel.SearchText = "example";
        await viewModel.SearchCommand.ExecuteAsync(null);

        await viewModel.Projects[0].InstallCommand.ExecuteAsync(null);

        Assert.False(viewModel.Projects[0].IsInstalled);
        Assert.Contains("安装失败", viewModel.StatusMessage);
    }

    private static IntegrationPacksPageViewModel CreateViewModel(FakeModpackService service)
        => CreateViewModel(service, new FakeModpackInstaller());

    private static IntegrationPacksPageViewModel CreateViewModel(
        FakeModpackService service,
        FakeModpackInstaller installer)
        => new(new FakeSettingsService(), service, installer, new FakePlatformService());
}
