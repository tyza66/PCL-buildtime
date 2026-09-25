using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class VersionPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new AppSettings { MinecraftFolder = "/games/mc" };

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeCatalog : IVersionCatalogService
    {
        public IReadOnlyList<MinecraftVersion> Installed { get; set; } = [];

        public List<string> ScannedFolders { get; } = [];

        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder)
        {
            ScannedFolders.Add(minecraftFolder);
            return Installed;
        }

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id) => null;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private static MinecraftVersion Version(string id)
        => new()
        {
            Id = id,
            Folder = $"/games/mc/versions/{id}",
            JsonPath = $"/games/mc/versions/{id}/{id}.json",
        };

    private static VersionPageViewModel CreateViewModel(
        FakeCatalog catalog,
        SessionState session,
        FakeSettingsService? settings = null)
        => new(settings ?? new FakeSettingsService(), catalog, session, new FakePlatformService());

    [Fact]
    public void Refresh_SelectsFirstVersion()
    {
        var catalog = new FakeCatalog
        {
            Installed = [Version("1.20.1"), Version("1.19.4")],
        };

        var viewModel = CreateViewModel(catalog, new SessionState());

        Assert.Equal("1.20.1", viewModel.SelectedVersion?.Id);
        Assert.Contains("已找到 2", viewModel.StatusMessage);
        Assert.Single(catalog.ScannedFolders);
    }

    [Fact]
    public void InstalledEvent_RefreshesAndSelectsInstalledVersion()
    {
        var session = new SessionState();
        var catalog = new FakeCatalog();
        var viewModel = CreateViewModel(catalog, session);

        catalog.Installed = [Version("1.20.1")];
        session.NotifyVersionInstalled("1.20.1");

        Assert.Equal("1.20.1", viewModel.SelectedVersion?.Id);
        Assert.Equal(2, catalog.ScannedFolders.Count);
        Assert.Contains("/games/mc", catalog.ScannedFolders);
    }
}
