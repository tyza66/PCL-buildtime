using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class ModsPageViewModelTests
{
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

        public IReadOnlyList<ModInfo> Scan(string minecraftFolder) => ScanResult;

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

    private static ModsPageViewModel CreateViewModel(FakeModsService service)
        => new(new FakeSettingsService(), service, new FakePlatformService());

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
    public async Task DeleteCommand_CallsServiceAndRemovesItem()
    {
        var service = new FakeModsService { ScanResult = [Mod("example")] };
        var viewModel = CreateViewModel(service);
        var item = viewModel.Mods[0];

        await item.DeleteCommand.ExecuteAsync(null);

        Assert.Single(service.DeleteCalls);
        Assert.Empty(viewModel.Mods);
        Assert.Contains("已删除", viewModel.StatusMessage);
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
}
