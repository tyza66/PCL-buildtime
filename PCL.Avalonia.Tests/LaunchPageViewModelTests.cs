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

        public int ProcessId => 42;

        public bool HasExited => ExitTcs.Task.IsCompleted;

        public int KillCount { get; private set; }

        public int DisposeCount { get; private set; }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
            => ExitTcs.Task.WaitAsync(cancellationToken);

        public void Kill() => KillCount++;

        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeLauncher : IGameLauncher
    {
        public IGameLaunch LaunchResult { get; set; } = new FakeGameLaunch();

        public int LaunchCount { get; private set; }

        public AppSettings? LastBuildSettings { get; private set; }

        public LaunchPlan BuildLaunchPlan(MinecraftVersion version, AppSettings settings, string javaExecutable)
        {
            LastBuildSettings = settings;
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
        SyncDispatcher dispatcher)
        => new(new FakeSettingsService(), new FakeJavaService(), launcher, session, dispatcher);

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

        await WaitUntilAsync(() => !viewModel.IsRunning);
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

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.True(condition(), "等待条件超时");
                return;
            }

            await Task.Delay(10);
        }
    }
}
