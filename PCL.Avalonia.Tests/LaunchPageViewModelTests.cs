using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Platform;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class LaunchPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new AppSettings { MinecraftFolder = "/games/mc" };

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeJavaService : IJavaService
    {
        public string? Resolved { get; init; } = "/games/java";

        public int? LastRequiredMajor { get; private set; }

        public string? ResolveJavaExecutable(AppSettings settings) => Resolved;

        public string? ResolveJavaExecutable(AppSettings settings, int? requiredMajorVersion)
        {
            LastRequiredMajor = requiredMajorVersion;
            return Resolved;
        }
    }

    private sealed class FakeJavaListService : IJavaListService
    {
        private readonly JavaInfo[] _items;

        public FakeJavaListService(params JavaInfo[] items) => _items = items;

        public IReadOnlyList<JavaInfo> Scan() => _items;

        public JavaInfo? GetJava(string path)
            => _items.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.Ordinal));

        public void Refresh()
        {
        }
    }

    private sealed class FakeGameLaunch : IGameLaunch
    {
        public TaskCompletionSource<int> ExitTcs { get; } = new();
        public TaskCompletionSource DisposeTcs { get; } = new();

        public int ProcessId => 42;

        public bool HasExited => ExitTcs.Task.IsCompleted;

        public int KillCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
            => ExitTcs.Task.WaitAsync(cancellationToken);

        public void Kill() => KillCount++;

        public void Dispose()
        {
            DisposeCount++;
            DisposeTcs.TrySetResult();
        }
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public IGameLaunch LaunchResult { get; set; } = new FakeGameLaunch();

        public Exception? BuildError { get; set; }

        public int LaunchCount { get; private set; }

        public AppSettings? LastBuildSettings { get; private set; }

        public Account? LastAccount { get; private set; }

        public LaunchPlan BuildLaunchPlan(
            MinecraftVersion version,
            AppSettings settings,
            string javaExecutable,
            Account? account = null,
        VersionSettings? versionSettings = null)
        {
            if (BuildError is not null)
            {
                throw BuildError;
            }

            LastBuildSettings = settings;
            LastAccount = account;
            return new()
            {
                JavaExecutable = javaExecutable,
                WorkingDirectory = settings.MinecraftFolder,
                NativesDirectory = "/games/mc/natives",
                ClassPath = "/games/mc/versions/1.20.1/1.20.1.jar",
                MainClass = "net.minecraft.client.main.Main",
                Arguments = [],
                Version = version,
            };
        }

        public IGameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null)
        {
            LaunchCount++;
            return LaunchResult;
        }
    }

    private sealed class FakeMicrosoftAuthenticationService : IMicrosoftAuthenticationService
    {
        public Func<Account, MicrosoftAccountSession?>? RefreshHandler { get; set; }

        public int RefreshCount { get; private set; }

        public Task<MicrosoftAccountSession> LoginAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new MicrosoftAccountSession
            {
                Name = "Alex",
                Uuid = "11111111-2222-3333-4444-555555555555",
                AccessToken = "ms-token",
                RefreshToken = "ms-refresh",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            });

        public Task<MicrosoftAccountSession?> RefreshAsync(
            Account account,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(RefreshHandler?.Invoke(account));
        }
    }

    private sealed class FakeAccountService : IAccountService
    {
        public List<Account> Accounts { get; } = [];

        public IReadOnlyList<Account> Load() => Accounts.ToList();

        public Account AddOfflineAccount(string name)
        {
            var account = new Account { Id = Guid.NewGuid(), Name = name, Type = "offline" };
            Accounts.Add(account);
            return account;
        }

        public Account AddMicrosoftAccount(MicrosoftAccountSession session)
        {
            var account = new Account
            {
                Id = Guid.NewGuid(),
                Name = session.Name,
                Type = "microsoft",
                Uuid = session.Uuid,
                AccessToken = session.AccessToken,
                RefreshToken = session.RefreshToken,
                AccessTokenExpiresAt = session.AccessTokenExpiresAt,
            };
            Accounts.Add(account);
            return account;
        }

        public void RemoveAccount(Guid id)
        {
        }

        public void SetDefaultAccount(Guid id)
        {
        }

        public Account? GetDefaultAccount() => null;
    }

    private sealed class FakeVersionManager : IVersionManagerService
    {
        public List<(string Folder, string Id, bool IsFavorite)> Favorites { get; } = [];
        public List<(string Folder, string Id)> Deleted { get; } = [];

        public VersionSettings LoadSettings(string minecraftFolder, string versionId) => new();

        public void SetFavorite(string minecraftFolder, string versionId, bool isFavorite)
        {
            Favorites.Add((minecraftFolder, versionId, isFavorite));
        }

        public void SetHidden(string minecraftFolder, string versionId, bool isHidden)
        {
        }

        public void SetDisplayType(string minecraftFolder, string versionId, InstanceDisplayType displayType)
        {
        }

        public void SetInstanceLaunchSettings(
            string minecraftFolder,
            string versionId,
            int? maxMemoryMb,
            string? javaPath,
            string? jvmArguments,
            string? gameArguments)
        {
        }

        public void SetDescription(string minecraftFolder, string versionId, string description)
        {
        }

        public string Rename(string minecraftFolder, string versionId, string newName) => newName;

        public void Delete(string minecraftFolder, string versionId)
        {
            Deleted.Add((minecraftFolder, versionId));
        }
    }

    private sealed class SyncDispatcher : IUiDispatcher
    {
        public int PostCount { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            action();
        }

        public void Debounce(string key, TimeSpan delay, Action action)
        {
        }
    }

    private sealed class FakeVersionCatalog : IVersionCatalogService
    {
        private readonly MinecraftVersion[] _scan;

        public int? MajorVersion { get; init; } = 17;

        public FakeVersionCatalog(params MinecraftVersion[] scan) => _scan = scan;

        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder) => _scan;

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id)
            => new()
            {
                Id = id,
                JavaVersion = MajorVersion is null
                    ? null
                    : new JavaVersionJson { Component = "java-runtime-delta", MajorVersion = MajorVersion },
            };
    }

    private sealed class FakePlatform : IPlatformService
    {
        public string GetConfigDirectory() => "/cfg";

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class FakeFolderOpener : IFolderOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string path) => Opened.Add(path);
    }

    private static MinecraftVersion SelectedVersion(string id = "1.20.1")
        => new()
        {
            Id = id,
            Folder = $"/games/mc/versions/{id}",
            JsonPath = $"/games/mc/versions/{id}/{id}.json",
        };

    private static LaunchPageViewModel CreateViewModel(
        FakeLauncher launcher,
        SessionState session,
        SyncDispatcher dispatcher,
        FakeMicrosoftAuthenticationService? microsoft = null,
        FakeAccountService? accounts = null,
        FakeJavaService? java = null,
        FakeVersionCatalog? versionCatalog = null,
        FakeJavaListService? javaList = null,
        FakeVersionManager? versionManager = null,
        FakeFolderOpener? folderOpener = null)
        => new(
            new FakeSettingsService(),
            java ?? new FakeJavaService(),
            launcher,
            session,
            dispatcher,
            microsoft ?? new FakeMicrosoftAuthenticationService(),
            accounts ?? new FakeAccountService(),
            versionManager ?? new FakeVersionManager(),
            versionCatalog ?? new FakeVersionCatalog(),
            new FakePlatform(),
            folderOpener ?? new FakeFolderOpener(),
            javaList ?? new FakeJavaListService(new JavaInfo("/games/java", "17.0.9", "x64", 17, true)));

    /// <summary>启动页构造时会异步扫一遍已安装版本，测试里等到列表刷出来再断言。</summary>
    private static async Task WaitForInstalledVersions(LaunchPageViewModel viewModel, int expected)
    {
        for (var i = 0; i < 200 && viewModel.InstalledVersions.Count < expected; i++)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task LaunchAsync_StartsGameAndTracksExit()
    {
        var launch = new FakeGameLaunch();
        var launcher = new FakeLauncher { LaunchResult = launch };
        var dispatcher = new SyncDispatcher();
        var viewModel = CreateViewModel(launcher, new SessionState { SelectedVersion = SelectedVersion() }, dispatcher);

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRunning);
        Assert.Equal(1, launcher.LaunchCount);

        launch.ExitTcs.SetResult(0);

        await launch.DisposeTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewModel.IsRunning);
        Assert.Contains("退出码 0", viewModel.LogText);
        Assert.Contains("正常退出", viewModel.StatusMessage);
        Assert.DoesNotContain("排查建议", viewModel.LogText);
        Assert.Equal(1, launch.DisposeCount);
    }

    [Fact]
    public async Task LaunchAsync_CrashedGame_GetsDiagnosisAndFixAdvice()
    {
        var launch = new FakeGameLaunch();
        var launcher = new FakeLauncher { LaunchResult = launch };
        var viewModel = CreateViewModel(launcher, new SessionState { SelectedVersion = SelectedVersion() }, new SyncDispatcher());

        await viewModel.LaunchCommand.ExecuteAsync(null);
        launch.ExitTcs.SetResult(1);
        await launch.DisposeTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 崩了就干巴巴一个"退出码 1"等于没报：日志里要点名去哪儿看、先动哪一格。
        Assert.Contains("游戏进程已退出（退出码 1）", viewModel.LogText);
        Assert.Contains("排查建议", viewModel.LogText);
        Assert.Contains("crash-reports", viewModel.LogText);
        Assert.Contains("Mod", viewModel.LogText);
        Assert.Contains("游戏崩溃", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Cancel_KillsAndStopsGame()
    {
        var launch = new FakeGameLaunch();
        var launcher = new FakeLauncher { LaunchResult = launch };
        var viewModel = CreateViewModel(launcher, new SessionState { SelectedVersion = SelectedVersion() }, new SyncDispatcher());

        await viewModel.LaunchCommand.ExecuteAsync(null);

        viewModel.CancelCommand.Execute(null);

        Assert.Equal(1, launch.KillCount);
        Assert.Equal(1, launch.DisposeCount);
        Assert.False(viewModel.IsRunning);
    }

    [Fact]
    public async Task LaunchAsync_UsesSelectedAccountName_OverSettingsUserName()
    {
        var launcher = new FakeLauncher();
        var session = new SessionState
        {
            SelectedVersion = SelectedVersion(),
            SelectedAccount = new Account { Id = Guid.NewGuid(), Name = "SteveAccount" },
        };
        var viewModel = CreateViewModel(launcher, session, new SyncDispatcher());

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.NotNull(launcher.LastBuildSettings);
        Assert.Equal("SteveAccount", launcher.LastBuildSettings!.UserName);
    }

    [Fact]
    public async Task LaunchAsync_RefreshesExpiredMicrosoftToken_AndPassesUpdatedAccount()
    {
        var launch = new FakeGameLaunch();
        var launcher = new FakeLauncher { LaunchResult = launch };
        var microsoft = new FakeMicrosoftAuthenticationService
        {
            RefreshHandler = _ => new MicrosoftAccountSession
            {
                Name = "Alex",
                Uuid = "11111111-2222-3333-4444-555555555555",
                AccessToken = "refreshed-token",
                RefreshToken = "refreshed-refresh",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            },
        };
        var session = new SessionState
        {
            SelectedVersion = SelectedVersion(),
            SelectedAccount = new Account
            {
                Id = Guid.NewGuid(),
                Name = "Alex",
                Type = "microsoft",
                Uuid = "11111111-2222-3333-4444-555555555555",
                AccessToken = "old-token",
                RefreshToken = "old-refresh",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
        };
        var viewModel = CreateViewModel(
            launcher,
            session,
            new SyncDispatcher(),
            microsoft,
            new FakeAccountService());

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(1, microsoft.RefreshCount);
        Assert.NotNull(launcher.LastAccount);
        Assert.Equal("refreshed-token", launcher.LastAccount!.AccessToken);
        Assert.Equal("refreshed-token", session.SelectedAccount?.AccessToken);
    }

    [Fact]
    public async Task LaunchAsync_WhenTokenRefreshFails_DoesNotLaunch()
    {
        var launcher = new FakeLauncher();
        var microsoft = new FakeMicrosoftAuthenticationService
        {
            RefreshHandler = _ => null,
        };
        var session = new SessionState
        {
            SelectedVersion = SelectedVersion(),
            SelectedAccount = new Account
            {
                Id = Guid.NewGuid(),
                Name = "Alex",
                Type = "microsoft",
                Uuid = "11111111-2222-3333-4444-555555555555",
                AccessToken = "old-token",
                RefreshToken = "old-refresh",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
        };
        var viewModel = CreateViewModel(launcher, session, new SyncDispatcher(), microsoft);

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(0, launcher.LaunchCount);
        Assert.Contains("重新登录", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchAsync_PassesRequiredJavaMajorFromVersionJson()
    {
        var launcher = new FakeLauncher();
        var java = new FakeJavaService();
        var viewModel = CreateViewModel(
            launcher,
            new SessionState { SelectedVersion = SelectedVersion() },
            new SyncDispatcher(),
            java: java,
            versionCatalog: new FakeVersionCatalog { MajorVersion = 25 });

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(25, java.LastRequiredMajor);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task LaunchAsync_WhenVersionJsonHasNoJavaVersion_ResolvesWithoutRequiredMajor()
    {
        var launcher = new FakeLauncher();
        var java = new FakeJavaService();
        var viewModel = CreateViewModel(
            launcher,
            new SessionState { SelectedVersion = SelectedVersion() },
            new SyncDispatcher(),
            java: java,
            versionCatalog: new FakeVersionCatalog { MajorVersion = null });

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Null(java.LastRequiredMajor);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public void JavaStatus_ShowsResolvedJavaPathAndVersion()
    {
        var javaList = new FakeJavaListService(new JavaInfo("/games/java", "25.0.1", "x64", 25, true));
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState { SelectedVersion = SelectedVersion() },
            new SyncDispatcher(),
            versionCatalog: new FakeVersionCatalog { MajorVersion = 25 },
            javaList: javaList);

        Assert.False(viewModel.JavaStatusIsError);
        Assert.Contains("Java 25", viewModel.JavaStatusText);
        Assert.Contains("/games/java", viewModel.JavaStatusText);
    }

    [Fact]
    public void JavaStatus_WarnsWhenVersionNeedsNewerJava()
    {
        var javaList = new FakeJavaListService(new JavaInfo("/games/java", "17.0.9", "x64", 17, true));
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState { SelectedVersion = SelectedVersion() },
            new SyncDispatcher(),
            versionCatalog: new FakeVersionCatalog { MajorVersion = 25 },
            javaList: javaList);

        Assert.True(viewModel.JavaStatusIsError);
        Assert.Contains("需要 Java 25", viewModel.JavaStatusText);
        Assert.Contains("Java 17", viewModel.JavaStatusText);
    }

    [Fact]
    public void JavaStatus_WarnsWhenNoJavaDetected()
    {
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState { SelectedVersion = SelectedVersion() },
            new SyncDispatcher(),
            java: new FakeJavaService { Resolved = null },
            javaList: new FakeJavaListService());

        Assert.True(viewModel.JavaStatusIsError);
        Assert.Contains("未检测到 Java", viewModel.JavaStatusText);
    }

    [Fact]
    public async Task LaunchAsync_WhenNoJavaFound_ExplainsRequiredVersion()
    {
        var launcher = new FakeLauncher();
        var viewModel = CreateViewModel(
            launcher,
            new SessionState { SelectedVersion = SelectedVersion() },
            new SyncDispatcher(),
            java: new FakeJavaService { Resolved = null },
            versionCatalog: new FakeVersionCatalog { MajorVersion = 25 },
            javaList: new FakeJavaListService());

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(0, launcher.LaunchCount);
        Assert.Contains("需要 Java 25", viewModel.StatusMessage);
        Assert.Contains("设置页", viewModel.StatusMessage);
    }

    [Fact]
    public async Task LaunchAsync_LogsJavaPath_AndChineseFailureOnLauncherError()
    {
        var launcher = new FakeLauncher();
        launcher.BuildError = new HttpRequestException("no route");
        var viewModel = CreateViewModel(
            launcher,
            new SessionState { SelectedVersion = SelectedVersion() },
            new SyncDispatcher(),
            javaList: new FakeJavaListService(new JavaInfo("/games/java", "25.0.1", "x64", 25, true)));

        await viewModel.LaunchCommand.ExecuteAsync(null);

        Assert.Contains("使用 Java：/games/java", viewModel.LogText);
        Assert.Contains("启动失败：", viewModel.StatusMessage);
        Assert.Contains("下载源", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ContextMenuFavoriteCommand_PersistsTheNewStateAndUpdatesTheStar()
    {
        var versionManager = new FakeVersionManager();
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState(),
            new SyncDispatcher(),
            versionCatalog: new FakeVersionCatalog(SelectedVersion("1.20.1")),
            versionManager: versionManager);
        await WaitForInstalledVersions(viewModel, 1);

        var item = viewModel.InstalledVersions.Single();
        Assert.False(item.IsFavorite);
        Assert.Equal("收藏", item.FavoriteText);

        item.FavoriteCommand.Execute(null);

        Assert.True(item.IsFavorite);
        Assert.Equal("取消收藏", item.FavoriteText);
        Assert.Contains(("/games/mc", "1.20.1", true), versionManager.Favorites);
        Assert.Contains("已收藏 1.20.1", viewModel.VersionStatus);
    }

    [Fact]
    public async Task ContextMenuDeleteCommand_RemovesTheItemAndMovesSelectionForward()
    {
        var versionManager = new FakeVersionManager();
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState(),
            new SyncDispatcher(),
            versionCatalog: new FakeVersionCatalog(SelectedVersion("1.20.1"), SelectedVersion("1.20.2")),
            versionManager: versionManager);
        await WaitForInstalledVersions(viewModel, 2);

        var removed = viewModel.InstalledVersions[0];
        viewModel.SelectedInstalledVersion = removed;

        removed.DeleteCommand.Execute(null);

        Assert.Contains(("/games/mc", "1.20.1"), versionManager.Deleted);
        Assert.DoesNotContain(removed, viewModel.InstalledVersions);
        Assert.Equal("1.20.2", viewModel.SelectedInstalledVersion?.Id);
        Assert.Equal("1.20.2", viewModel.SelectedVersion?.Id);
    }

    [Fact]
    public async Task ContextMenuDeleteCommand_OnTheLastVersion_ClearsTheSelection()
    {
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState(),
            new SyncDispatcher(),
            versionCatalog: new FakeVersionCatalog(SelectedVersion("1.20.1")));
        await WaitForInstalledVersions(viewModel, 1);

        viewModel.SelectedInstalledVersion = viewModel.InstalledVersions[0];
        Assert.NotNull(viewModel.SelectedVersion);

        viewModel.InstalledVersions[0].DeleteCommand.Execute(null);

        Assert.Empty(viewModel.InstalledVersions);
        Assert.Null(viewModel.SelectedInstalledVersion);
        // 删完不能留着指向已删除版本的启动目标，否则启动按钮立刻又报未设置。
        Assert.Null(viewModel.SelectedVersion);
    }

    [Fact]
    public async Task ContextMenuOpenFolderCommand_OpensThatVersionFolder()
    {
        var opener = new FakeFolderOpener();
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState(),
            new SyncDispatcher(),
            versionCatalog: new FakeVersionCatalog(SelectedVersion("1.20.1")),
            folderOpener: opener);
        await WaitForInstalledVersions(viewModel, 1);

        viewModel.InstalledVersions[0].OpenFolderCommand.Execute(null);

        // 用 Path.Combine 拼期望值，Windows 上是反斜杠，写死斜杠会让 CI 在 windows-latest 上挂掉。
        Assert.Equal(new[] { Path.Combine("/games/mc", "versions", "1.20.1") }, opener.Opened);
    }

    [Fact]
    public async Task ContextMenuSelectCommand_SwitchesTheLaunchTarget()
    {
        var viewModel = CreateViewModel(
            new FakeLauncher(),
            new SessionState(),
            new SyncDispatcher(),
            versionCatalog: new FakeVersionCatalog(SelectedVersion("1.20.1"), SelectedVersion("1.20.2")));
        await WaitForInstalledVersions(viewModel, 2);

        var second = viewModel.InstalledVersions[1];
        Assert.NotEqual(second, viewModel.SelectedInstalledVersion);

        second.SelectItemCommand.Execute(null);

        Assert.Equal(second, viewModel.SelectedInstalledVersion);
        Assert.Equal("1.20.2", viewModel.SelectedVersion?.Id);
    }
}
