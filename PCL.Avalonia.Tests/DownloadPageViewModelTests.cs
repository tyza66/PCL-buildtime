using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class DownloadPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new();

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeManifestService : IVersionManifestService
    {
        public VersionManifest Manifest { get; set; } = new();

        public DownloadSource? LastSource { get; private set; }

        public int CallCount { get; private set; }

        public Exception? Exception { get; set; }

        public Task<VersionManifest> GetManifestAsync(
            DownloadSource source,
            CancellationToken cancellationToken = default)
        {
            LastSource = source;
            CallCount++;
            return Exception is null
                ? Task.FromResult(Manifest)
                : Task.FromException<VersionManifest>(Exception);
        }
    }

    private sealed class FakeInstaller : IVersionInstaller
    {
        public VersionInstallResult Result { get; set; } = new("", []);

        public List<(string VersionId, DownloadSource Source, string Folder)> Calls { get; } = [];

        public TaskCompletionSource? Gate { get; set; }

        public async Task<VersionInstallResult> InstallAsync(
            string versionId,
            VersionManifestEntry? entry,
            DownloadSource source,
            string minecraftFolder,
            IProgress<InstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((versionId, source, minecraftFolder));
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Result;
        }
    }

    private sealed class FakeCatalog : IVersionCatalogService
    {
        public IReadOnlyList<MinecraftVersion> Installed { get; set; } = [];

        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder) => Installed;

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id) => null;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class FakeJavaInfoService : IVersionJavaInfoService
    {
        public int? RequiredMajor { get; set; }

        public List<string> RequestedIds { get; } = [];

        public Exception? Exception { get; set; }

        public Task<int?> GetRequiredJavaMajorAsync(
            DownloadSource source,
            VersionManifestEntry? entry,
            string versionId,
            CancellationToken cancellationToken = default)
        {
            RequestedIds.Add(versionId);
            return Exception is null
                ? Task.FromResult(RequiredMajor)
                : Task.FromException<int?>(Exception);
        }
    }

    private sealed class FakeJavaListService : IJavaListService
    {
        private readonly JavaInfo[] _items;

        public FakeJavaListService(params JavaInfo[] items) => _items = items;

        public IReadOnlyList<JavaInfo> Scan() => _items;

        public JavaInfo? GetJava(string path) => _items.FirstOrDefault(item => item.Path == path);

        public void Refresh()
        {
        }
    }

    private static DownloadPageViewModel CreateViewModel(
        FakeSettingsService settings,
        FakeManifestService manifest,
        FakeInstaller installer,
        FakeCatalog catalog,
        SessionState? session = null)
        => new(
            settings,
            manifest,
            installer,
            catalog,
            new FakePlatformService(),
            session ?? new SessionState(),
            new FakeJavaInfoService(),
            new FakeJavaListService());

    private static DownloadPageViewModel CreateViewModel(
        FakeSettingsService settings,
        FakeManifestService manifest,
        FakeInstaller installer,
        FakeCatalog catalog,
        FakeJavaInfoService javaInfo,
        FakeJavaListService? javaList = null)
        => new(
            settings,
            manifest,
            installer,
            catalog,
            new FakePlatformService(),
            new SessionState(),
            javaInfo,
            javaList ?? new FakeJavaListService());

    private static DownloadVersionItemViewModel SelectVersion(DownloadPageViewModel viewModel, string id)
    {
        var item = new DownloadVersionItemViewModel(new VersionManifestEntry { Id = id }, isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;
        return item;
    }

    [Fact]
    public async Task RefreshAsync_LoadsManifestAndMarksInstalled()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings
            {
                MinecraftFolder = "/games/mc",
                DownloadSource = DownloadSource.Mojang,
            },
        };
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions =
                [
                    new VersionManifestEntry
                    {
                        Id = "1.20.1",
                        Type = "release",
                        ReleaseTime = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    },
                    new VersionManifestEntry
                    {
                        Id = "24w10a",
                        Type = "snapshot",
                        ReleaseTime = DateTimeOffset.Parse("2024-03-06T12:00:00Z"),
                    },
                ],
            },
        };
        var catalog = new FakeCatalog
        {
            Installed =
            [
                new MinecraftVersion { Id = "1.20.1", Folder = "/x", JsonPath = "/x/1.20.1.json" },
            ],
        };
        var viewModel = CreateViewModel(settings, manifest, new FakeInstaller(), catalog);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Versions.Count);
        Assert.Equal("24w10a", viewModel.Versions[0].Id);
        Assert.True(viewModel.Versions[1].IsInstalled);
        Assert.Equal(DownloadSource.Mojang, manifest.LastSource);
        Assert.Contains("2", viewModel.StatusMessage);
    }

    [Fact]
    public async Task SearchText_FiltersVersions()
    {
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions =
                [
                    new VersionManifestEntry { Id = "1.20.1", ReleaseTime = DateTimeOffset.Parse("2024-01-01T00:00:00Z") },
                    new VersionManifestEntry { Id = "24w10a", ReleaseTime = DateTimeOffset.Parse("2024-03-06T12:00:00Z") },
                ],
            },
        };
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            manifest,
            new FakeInstaller(),
            new FakeCatalog());
        await viewModel.RefreshCommand.ExecuteAsync(null);

        viewModel.SearchText = "1.20";

        var item = Assert.Single(viewModel.Versions);
        Assert.Equal("1.20.1", item.Id);
    }

    [Fact]
    public async Task InstallAsync_CallsInstaller_AndMarksInstalled()
    {
        var settings = new FakeSettingsService
        {
            Settings = new AppSettings
            {
                MinecraftFolder = "/games/mc",
                DownloadSource = DownloadSource.Bmclapi,
            },
        };
        var installer = new FakeInstaller
        {
            Result = new VersionInstallResult("1.20.1", []),
        };
        var session = new SessionState();
        string? installedVersionId = null;
        session.VersionInstalled += (_, versionId) => installedVersionId = versionId;
        var viewModel = CreateViewModel(settings, new FakeManifestService(), installer, new FakeCatalog(), session);
        var item = new DownloadVersionItemViewModel(
            new VersionManifestEntry { Id = "1.20.1" },
            isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;

        await viewModel.InstallCommand.ExecuteAsync(null);

        var call = Assert.Single(installer.Calls);
        Assert.Equal(("1.20.1", DownloadSource.Bmclapi, "/games/mc"), (call.VersionId, call.Source, call.Folder));
        Assert.True(item.IsInstalled);
        Assert.Equal("已安装 1.20.1", viewModel.StatusMessage);
        Assert.Equal("1.20.1", installedVersionId);
    }

    [Fact]
    public async Task InstallAsync_FailureShowsSummary()
    {
        var installer = new FakeInstaller
        {
            Result = new VersionInstallResult("1.20.1", ["支持库 core 下载失败", "资源 icon.png 下载失败"]),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), new FakeManifestService(), installer, new FakeCatalog());
        var item = new DownloadVersionItemViewModel(new VersionManifestEntry { Id = "1.20.1" }, isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;

        await viewModel.InstallCommand.ExecuteAsync(null);

        Assert.StartsWith("安装未完成", viewModel.StatusMessage);
        Assert.Contains("支持库 core 下载失败", viewModel.StatusMessage);
        Assert.False(item.IsInstalled);
    }

    [Fact]
    public async Task InstallAsync_CancelStopsInstallation()
    {
        var installer = new FakeInstaller
        {
            Gate = new TaskCompletionSource(),
            Result = new VersionInstallResult("1.20.1", []),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), new FakeManifestService(), installer, new FakeCatalog());
        var item = new DownloadVersionItemViewModel(new VersionManifestEntry { Id = "1.20.1" }, isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;

        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        viewModel.CancelCommand.Execute(null);

        await installTask;

        Assert.Equal("安装已取消", viewModel.StatusMessage);
        Assert.False(viewModel.IsInstalling);
        Assert.False(item.IsInstalled);
    }

    [Fact]
    public async Task RefreshAsync_FailureShowsMessage()
    {
        var manifest = new FakeManifestService
        {
            Exception = new InvalidOperationException("network down"),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), manifest, new FakeInstaller(), new FakeCatalog());

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.StartsWith("获取版本清单失败", viewModel.StatusMessage);
        Assert.Empty(viewModel.Versions);
    }

    [Fact]
    public async Task OnActivatedAsync_LoadsManifest_WhenListIsEmpty()
    {
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions = [new VersionManifestEntry { Id = "1.20.1" }],
            },
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), manifest, new FakeInstaller(), new FakeCatalog());

        await viewModel.OnActivatedAsync();

        Assert.Equal(1, manifest.CallCount);
        var version = Assert.Single(viewModel.Versions);
        Assert.Equal("1.20.1", version.Id);
    }

    [Fact]
    public async Task OnActivatedAsync_SkipsRefresh_WhenListIsFresh()
    {
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions = [new VersionManifestEntry { Id = "1.20.1" }],
            },
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), manifest, new FakeInstaller(), new FakeCatalog());

        await viewModel.OnActivatedAsync();
        await viewModel.OnActivatedAsync();

        Assert.Equal(1, manifest.CallCount);
    }

    [Fact]
    public async Task OnActivatedAsync_Retries_WhenRefreshFailed()
    {
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions = [new VersionManifestEntry { Id = "1.20.1" }],
            },
            Exception = new InvalidOperationException("network down"),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), manifest, new FakeInstaller(), new FakeCatalog());

        await viewModel.OnActivatedAsync();
        Assert.StartsWith("获取版本清单失败", viewModel.StatusMessage);

        manifest.Exception = null;
        await viewModel.OnActivatedAsync();

        Assert.Equal(2, manifest.CallCount);
        var version = Assert.Single(viewModel.Versions);
        Assert.Equal("1.20.1", version.Id);
    }

    [Fact]
    public async Task OnActivatedAsync_SkipsRefresh_WhileInstalling()
    {
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest
            {
                Versions = [new VersionManifestEntry { Id = "1.20.1" }],
            },
        };
        var installer = new FakeInstaller
        {
            Gate = new TaskCompletionSource(),
            Result = new VersionInstallResult("1.20.1", []),
        };
        var viewModel = CreateViewModel(new FakeSettingsService(), manifest, installer, new FakeCatalog());
        var item = new DownloadVersionItemViewModel(new VersionManifestEntry { Id = "1.20.1" }, isInstalled: false);
        viewModel.Versions.Add(item);
        viewModel.SelectedVersion = item;

        var installTask = viewModel.InstallCommand.ExecuteAsync(null);
        await viewModel.OnActivatedAsync();

        Assert.Equal(0, manifest.CallCount);
        viewModel.CancelCommand.Execute(null);
        await installTask;
    }

    [Fact]
    public void SelectedVersion_ReportsTheJavaRequirementItWillSatisfy()
    {
        var javaInfo = new FakeJavaInfoService { RequiredMajor = 21 };
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeCatalog(),
            javaInfo,
            new FakeJavaListService(new JavaInfo("/usr/bin/java", "21.0.2", "aarch64", 21, true)));

        SelectVersion(viewModel, "1.20.6");

        Assert.Contains("需要 Java 21", viewModel.JavaRequirementText);
        Assert.Contains("满足要求", viewModel.JavaRequirementText);
        Assert.False(viewModel.JavaRequirementIsWarning);
        Assert.Equal("1.20.6", Assert.Single(javaInfo.RequestedIds));
    }

    [Fact]
    public void SelectedVersion_WarnsWhenTheBestLocalJavaIsTooOld()
    {
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeCatalog(),
            new FakeJavaInfoService { RequiredMajor = 21 },
            new FakeJavaListService(new JavaInfo("/usr/bin/java", "17.0.9", "x86_64", 17, true)));

        SelectVersion(viewModel, "1.20.6");

        Assert.Contains("需要 Java 21", viewModel.JavaRequirementText);
        Assert.Contains("当前最高只检测到 Java 17", viewModel.JavaRequirementText);
        Assert.True(viewModel.JavaRequirementIsWarning);
    }

    [Fact]
    public void SelectedVersion_WarnsWhenNoJavaIsInstalledAtAll()
    {
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeCatalog(),
            new FakeJavaInfoService { RequiredMajor = 21 });

        SelectVersion(viewModel, "1.20.6");

        Assert.Contains("请先安装 Java 21", viewModel.JavaRequirementText);
        Assert.True(viewModel.JavaRequirementIsWarning);
    }

    [Fact]
    public void SelectedVersion_SaysNotProvided_WhenTheManifestCarriesNoJavaRequirement()
    {
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeCatalog(),
            new FakeJavaInfoService());

        SelectVersion(viewModel, "1.12.2");

        Assert.Contains("未提供 Java 要求信息", viewModel.JavaRequirementText);
        Assert.False(viewModel.JavaRequirementIsWarning);
    }

    [Fact]
    public void SelectedVersion_ShowsTransientHint_WhenTheLookupFails()
    {
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeCatalog(),
            new FakeJavaInfoService { Exception = new HttpRequestException("no route to host") });

        SelectVersion(viewModel, "1.20.6");

        Assert.Contains("暂时获取失败", viewModel.JavaRequirementText);
        Assert.False(viewModel.JavaRequirementIsWarning);
    }

    [Fact]
    public void DeselectingVersion_ClearsTheJavaRequirementRow()
    {
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            new FakeManifestService(),
            new FakeInstaller(),
            new FakeCatalog(),
            new FakeJavaInfoService { RequiredMajor = 21 });

        SelectVersion(viewModel, "1.20.6");
        Assert.NotEmpty(viewModel.JavaRequirementText);

        viewModel.SelectedVersion = null;

        Assert.Empty(viewModel.JavaRequirementText);
    }

    [Fact]
    public async Task Refresh_ClearsTheJavaRequirementRow()
    {
        var manifest = new FakeManifestService
        {
            Manifest = new VersionManifest { Versions = [new VersionManifestEntry { Id = "1.20.6" }] },
        };
        var viewModel = CreateViewModel(
            new FakeSettingsService(),
            manifest,
            new FakeInstaller(),
            new FakeCatalog(),
            new FakeJavaInfoService { RequiredMajor = 21 });

        SelectVersion(viewModel, "1.20.6");
        Assert.NotEmpty(viewModel.JavaRequirementText);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.JavaRequirementText);
    }
}
