using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class ModsPageViewModelTests
{

    private static string ModsFolder(string gameFolder) => Path.Combine(gameFolder, "mods");
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new() { MinecraftFolder = "/games/mc" };

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeModsService : IModsService
    {
        public IReadOnlyList<ModInfo> ScanResult { get; set; } = [];

        public List<(ModInfo Mod, bool Enabled)> ToggleCalls { get; } = [];

        public List<ModInfo> DeleteCalls { get; } = [];

        public List<string> ScannedFolders { get; } = [];

        public IReadOnlyList<ModInfo> Scan(string minecraftFolder)
        {
            ScannedFolders.Add(minecraftFolder);
            return ScanResult;
        }

        public ModInfo SetEnabled(ModInfo mod, bool enabled)
        {
            ToggleCalls.Add((mod, enabled));
            return mod with
            {
                IsEnabled = enabled,
                FileName = mod.FileName + (enabled ? "" : ".disabled"),
            };
        }

        public void Delete(ModInfo mod) => DeleteCalls.Add(mod);
    }

    private static ModInfo Mod(string name, bool enabled = true) => new()
    {
        FileName = name + (enabled ? ".jar" : ".jar.disabled"),
        DisplayName = name,
        FilePath = $"/games/mc/mods/{name}.jar",
        IsEnabled = enabled,
        SizeBytes = 2048,
        LastModifiedUtc = DateTime.UtcNow,
    };

    private sealed class FakeVersionManager : IVersionManagerService
    {
        public Dictionary<string, VersionSettings> Settings { get; } = [];

        public Exception? LoadError { get; init; }

        public VersionSettings LoadSettings(string minecraftFolder, string versionId)
        {
            if (LoadError is not null)
            {
                throw LoadError;
            }

            return Settings.TryGetValue(versionId, out var value) ? value : new();
        }

        public void SetFavorite(string minecraftFolder, string versionId, bool isFavorite)
        {
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

        public void SetInstanceIsolation(string minecraftFolder, string versionId, bool? independent)
        {
        }

        public void SetDescription(string minecraftFolder, string versionId, string description)
        {
        }

        public string Rename(string minecraftFolder, string versionId, string newName) => newName;

        public void Delete(string minecraftFolder, string versionId)
        {
        }
    }

    private static MinecraftVersion Version(string id) => new()
    {
        Id = id,
        Folder = $"/games/mc/versions/{id}",
        JsonPath = $"/games/mc/versions/{id}/{id}.json",
    };

    private static ModsPageViewModel CreateViewModel(
        FakeModsService service,
        SessionState? session = null,
        FakeVersionManager? versionManager = null)
        => new(
            new FakeSettingsService(),
            service,
            new FakePlatformService(),
            session ?? new SessionState(),
            versionManager ?? new FakeVersionManager());

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    [Fact]
    public void Constructor_LoadsModsFromSettingsFolder()
    {
        var service = new FakeModsService
        {
            ScanResult = [Mod("OptiFine"), Mod("fabric-api", enabled: false)],
        };

        var viewModel = CreateViewModel(service);

        Assert.Equal(2, viewModel.Mods.Count);
        Assert.Equal("OptiFine", viewModel.Mods[0].DisplayName);
        Assert.False(viewModel.Mods[1].IsEnabled);
        Assert.Contains("2", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ToggleCommand_CallsServiceAndUpdatesItem()
    {
        var service = new FakeModsService { ScanResult = [Mod("example")] };
        var viewModel = CreateViewModel(service);
        var item = viewModel.Mods[0];

        await item.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(("example.jar", false), (service.ToggleCalls[0].Mod.FileName, service.ToggleCalls[0].Enabled));
        Assert.False(item.IsEnabled);
        Assert.Contains("已禁用", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DeleteCommand_FirstClickOnlyArmsConfirmation()
    {
        var service = new FakeModsService { ScanResult = [Mod("example")] };
        var viewModel = CreateViewModel(service);
        var item = viewModel.Mods[0];

        await item.DeleteCommand.ExecuteAsync(null);

        // 第一击不许碰磁盘：误触一下不该让 Mod 文件直接没了。
        Assert.Empty(service.DeleteCalls);
        Assert.Single(viewModel.Mods);
        Assert.True(item.IsConfirmingDelete);
        Assert.Equal("确认删除", item.DeleteButtonText);
        Assert.Contains("再点一次", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DeleteCommand_SecondClickCallsServiceAndRemovesItem()
    {
        var service = new FakeModsService { ScanResult = [Mod("example")] };
        var viewModel = CreateViewModel(service);
        var item = viewModel.Mods[0];

        await item.DeleteCommand.ExecuteAsync(null);
        await item.DeleteCommand.ExecuteAsync(null);

        Assert.Single(service.DeleteCalls);
        Assert.Empty(viewModel.Mods);
        Assert.Contains("已删除", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ToggleCommand_CancelsPendingDeleteConfirmation()
    {
        var service = new FakeModsService { ScanResult = [Mod("example")] };
        var viewModel = CreateViewModel(service);
        var item = viewModel.Mods[0];
        await item.DeleteCommand.ExecuteAsync(null);

        await item.ToggleCommand.ExecuteAsync(null);

        // 改成禁用说明用户改主意了，确认态收回，按钮恢复普通"删除"。
        Assert.False(item.IsConfirmingDelete);
        Assert.Equal("删除", item.DeleteButtonText);
        Assert.Empty(service.DeleteCalls);
        Assert.Contains("已禁用", viewModel.StatusMessage);
    }

    [Fact]
    public void SearchText_FiltersMods()
    {
        var service = new FakeModsService
        {
            ScanResult = [Mod("OptiFine"), Mod("fabric-api")],
        };
        var viewModel = CreateViewModel(service);

        viewModel.SearchText = "opti";

        var item = Assert.Single(viewModel.Mods);
        Assert.Equal("OptiFine", item.DisplayName);
    }

    [Fact]
    public void Refresh_ScansIsolatedVersionFolder_WhenSelectedVersionIsIsolated()
    {
        var service = new FakeModsService { ScanResult = [Mod("Sodium")] };
        var session = new SessionState { SelectedVersion = Version("1.20.1-fabric") };
        var versionManager = new FakeVersionManager
        {
            Settings = { ["1.20.1-fabric"] = new VersionSettings { Independent = true } },
        };

        var viewModel = CreateViewModel(service, session, versionManager);

        Assert.Equal(ModsFolder("/games/mc/versions/1.20.1-fabric"), service.ScannedFolders[^1]);
        Assert.Contains("隔离目录", viewModel.StatusMessage);
        Assert.Contains(ModsFolder("/games/mc/versions/1.20.1-fabric"), viewModel.StatusMessage);
    }

    [Fact]
    public void Refresh_ScansSharedFolder_WhenIsolationSwitchedOffForVersion()
    {
        var service = new FakeModsService { ScanResult = [Mod("Sodium")] };
        var session = new SessionState { SelectedVersion = Version("1.20.1-fabric") };
        var versionManager = new FakeVersionManager
        {
            Settings = { ["1.20.1-fabric"] = new VersionSettings { Independent = false } },
        };

        var viewModel = CreateViewModel(service, session, versionManager);

        Assert.Equal(ModsFolder("/games/mc"), service.ScannedFolders[^1]);
        Assert.DoesNotContain("隔离目录", viewModel.StatusMessage);
    }

    [Fact]
    public void SelectedVersionChange_RescansModsForNewVersion()
    {
        var service = new FakeModsService { ScanResult = [] };
        var session = new SessionState();
        var viewModel = CreateViewModel(service, session);
        Assert.Equal(ModsFolder("/games/mc"), service.ScannedFolders[^1]);

        session.SelectedVersion = Version("1.20.1-neoforge");

        Assert.Equal(2, service.ScannedFolders.Count);
        Assert.Equal(ModsFolder("/games/mc/versions/1.20.1-neoforge"), service.ScannedFolders[^1]);
    }

    [Fact]
    public void Refresh_VersionSettingsUnreadable_FallsBackToGlobalDefault()
    {
        var service = new FakeModsService { ScanResult = [] };
        var session = new SessionState { SelectedVersion = Version("1.20.1") };
        var versionManager = new FakeVersionManager { LoadError = new IOException("外置卷 I/O 抖动") };
        var settings = new AppSettings { MinecraftFolder = "/games/mc" };

        var viewModel = new ModsPageViewModel(
            new FakeSettingsService { Settings = settings },
            service,
            new FakePlatformService(),
            session,
            versionManager);

        Assert.Contains("读取版本隔离设置失败", viewModel.StatusMessage);
        // 全局默认 All：读不出每版本设置时仍按默认规则隔离，不阻断列表。
        Assert.Equal(ModsFolder("/games/mc/versions/1.20.1"), service.ScannedFolders[^1]);
    }
}
