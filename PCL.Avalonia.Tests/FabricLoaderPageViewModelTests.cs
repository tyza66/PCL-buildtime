using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class FabricLoaderPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class FakeJavaListService : IJavaListService
    {
        private readonly List<JavaInfo> _infos;

        public FakeJavaListService(params JavaInfo[] infos)
        {
            _infos = infos.ToList();
        }

        public IReadOnlyList<JavaInfo> Scan() => _infos;

        public JavaInfo? GetJava(string path)
            => _infos.FirstOrDefault(j => j.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        public void Refresh()
        {
        }
    }

    private sealed class FakeFabricLoaderService : IFabricLoaderService
    {
        public IReadOnlyList<FabricLoaderVersion> Versions { get; set; } = [];

        public FabricInstallResult Result { get; set; } = new("fabric-loader-0.16.9-1.20.1", "1.20.1", "0.16.9", []);

        public string? LastGameVersion { get; private set; }

        public string? LastLoaderVersion { get; private set; }

        public List<FabricInstallProgress> ReportedProgress { get; } = [];

        public Task<IReadOnlyList<FabricLoaderVersion>> GetVersionsAsync(
            string gameVersion,
            CancellationToken cancellationToken = default)
        {
            LastGameVersion = gameVersion;
            return Task.FromResult(Versions);
        }

        public Task<FabricInstallResult> InstallAsync(
            string gameVersion,
            string loaderVersion,
            string minecraftFolder,
            DownloadSource source,
            IProgress<FabricInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastGameVersion = gameVersion;
            LastLoaderVersion = loaderVersion;
            foreach (var report in ReportedProgress)
            {
                progress?.Report(report);
            }

            return Task.FromResult(Result);
        }
    }

    private static (FakeSettingsService Settings, FakeFabricLoaderService Service, SessionState Session, FabricLoaderPageViewModel ViewModel)
        CreateViewModel(params JavaInfo[] javas)
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings { MinecraftFolder = "/games/mc", DownloadSource = DownloadSource.Mojang },
        };
        var service = new FakeFabricLoaderService();
        var session = new SessionState();
        var viewModel = new FabricLoaderPageViewModel(
            settings,
            service,
            new FakePlatformService(),
            new FakeJavaListService(javas),
            session);
        return (settings, service, session, viewModel);
    }

    [Fact]
    public async Task RefreshAsync_LoadsFabricVersions()
    {
        var (_, service, _, viewModel) = CreateViewModel();
        service.Versions =
        [
            new FabricLoaderVersion("0.16.9", "1.20.1", true, 17),
            new FabricLoaderVersion("0.15.0", "1.20.1", false, 17),
        ];
        viewModel.GameVersionText = "1.20.1";

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Versions.Count);
        Assert.Equal("1.20.1", service.LastGameVersion);
        Assert.Contains("2", viewModel.StatusMessage);
    }

    [Fact]
    public async Task InstallAsync_InstallsSelectedLoaderAndNotifiesSession()
    {
        var (settings, service, session, viewModel) = CreateViewModel();
        service.Versions = [new FabricLoaderVersion("0.16.9", "1.20.1", true, 17)];
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        var selected = viewModel.Versions[0];
        viewModel.SelectedLoader = selected;
        string? installedVersionId = null;
        session.VersionInstalled += (_, versionId) => installedVersionId = versionId;

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.Equal(("1.20.1", "0.16.9"), (service.LastGameVersion, service.LastLoaderVersion));
        Assert.Equal("/games/mc", settings.Settings.MinecraftFolder);
        Assert.Equal("已安装 fabric-loader-0.16.9-1.20.1", viewModel.StatusMessage);
        Assert.Equal("fabric-loader-0.16.9-1.20.1", installedVersionId);
    }

    [Fact]
    public async Task InstallAsync_ZeroTotalLibraries_NeverFlashesZeroSlashZero()
    {
        var (_, service, _, viewModel) = CreateViewModel();
        service.Versions = [new FabricLoaderVersion("0.16.9", "1.20.1", true, 17)];
        service.ReportedProgress.Add(new FabricInstallProgress(FabricInstallStage.Libraries, null, 0, 0));
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedLoader = viewModel.Versions[0];

        await viewModel.InstallCommand.ExecuteAsync(null);
        var text = await WaitForProgressTextAsync(() => viewModel.ProgressText);

        Assert.DoesNotContain("0/0", text);
        Assert.Equal("正在下载支持库...", text);
    }

    private static async Task<string> WaitForProgressTextAsync(Func<string> read)
    {
        for (var attempt = 0; attempt < 100 && !read().Contains("正在下载"); attempt++)
        {
            await Task.Delay(20);
        }

        return read();
    }

    [Fact]
    public async Task InstallAsync_FailureShowsSummary()
    {
        var (_, service, _, viewModel) = CreateViewModel();
        service.Versions = [new FabricLoaderVersion("0.16.9", "1.20.1", true, 17)];
        service.Result = new FabricInstallResult(
            "fabric-loader-0.16.9-1.20.1",
            "1.20.1",
            "0.16.9",
            ["支持库 asm 下载失败"]);
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedLoader = viewModel.Versions[0];

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.StartsWith("安装未完成", viewModel.StatusMessage);
        Assert.Contains("asm 下载失败", viewModel.StatusMessage);
    }

    [Fact]
    public void JavaHint_NoJavaDetected_TellsUserToInstallFirst()
    {
        var (_, _, _, viewModel) = CreateViewModel();
        viewModel.GameVersionText = "1.20.1";

        Assert.Contains("没有检测到", viewModel.JavaHintText);
        Assert.Contains("Java 17", viewModel.JavaHintText);
        Assert.True(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public void JavaHint_BestJavaBelowRequirement_WarnsWithDetectedVersion()
    {
        var (_, _, _, viewModel) = CreateViewModel(
            new JavaInfo("/jdk8/bin/java", "1.8.0_392", "x64", 8, true));
        viewModel.GameVersionText = "1.20.1";

        Assert.Contains("最高只检测到 Java 8", viewModel.JavaHintText);
        Assert.True(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public void JavaHint_BestJavaMeetsRequirement_ReportsSatisfied()
    {
        var (_, _, _, viewModel) = CreateViewModel(
            new JavaInfo("/jdk8/bin/java", "1.8.0_392", "x64", 8, true),
            new JavaInfo("/jdk17/bin/java", "17.0.9", "x64", 17, true));
        // 没选中加载器，走版本映射：1.20.1 要 Java 17，本机最高 17，满足。
        viewModel.GameVersionText = "1.20.1";

        Assert.Contains("Java 17 满足要求", viewModel.JavaHintText);
        Assert.False(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public async Task JavaHint_SelectedLoaderMinJava_WinsOverVersionMap()
    {
        var (_, service, _, viewModel) = CreateViewModel(
            new JavaInfo("/jdk17/bin/java", "17.0.9", "x64", 17, true));
        // 版本映射说 1.20.1 要 Java 17，但加载器接口声明要 21：以接口为准，17 就不够。
        service.Versions = [new FabricLoaderVersion("0.16.9", "1.20.1", true, 21)];
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedLoader = viewModel.Versions[0];

        Assert.Contains("Java 21", viewModel.JavaHintText);
        Assert.Contains("最高只检测到 Java 17", viewModel.JavaHintText);
        Assert.True(viewModel.JavaHintIsWarning);
    }

    [Fact]
    public async Task JavaHint_LoaderWithoutMinJava_FallsBackToVersionMap()
    {
        var (_, service, _, viewModel) = CreateViewModel(
            new JavaInfo("/jdk17/bin/java", "17.0.9", "x64", 17, true));
        service.Versions = [new FabricLoaderVersion("0.16.9", "1.20.1", true, 0)];
        viewModel.GameVersionText = "1.20.1";
        await viewModel.RefreshCommand.ExecuteAsync(null);
        viewModel.SelectedLoader = viewModel.Versions[0];

        Assert.Contains("Java 17 满足要求", viewModel.JavaHintText);
        Assert.False(viewModel.JavaHintIsWarning);
    }
}
