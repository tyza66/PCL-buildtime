using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Minecraft;
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
        public string? ResolveJavaExecutable(AppSettings settings) => "/games/java";
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

        public int LaunchCount { get; private set; }

        public AppSettings? LastBuildSettings { get; private set; }

        public Account? LastAccount { get; private set; }

        public LaunchPlan BuildLaunchPlan(
            MinecraftVersion version,
            AppSettings settings,
            string javaExecutable,
            Account? account = null)
        {
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

    private sealed class SyncDispatcher : IUiDispatcher
    {
        public int PostCount { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            action();
        }
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
        FakeAccountService? accounts = null)
        => new(
            new FakeSettingsService(),
            new FakeJavaService(),
            launcher,
            session,
            dispatcher,
            microsoft ?? new FakeMicrosoftAuthenticationService(),
            accounts ?? new FakeAccountService());

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
        Assert.Equal(1, launch.DisposeCount);
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
}
