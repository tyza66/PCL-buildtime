using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Platform;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Tests;

public sealed class OtherPageViewModelTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new() { MinecraftFolder = "/games/mc" };

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => "/config/pcl";

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class FakeOtherToolsService : IOtherToolsService
    {
        public List<(string MinecraftFolder, string ConfigDirectory)> EnvironmentCalls { get; } = [];

        public List<IReadOnlyList<string>> ScanCalls { get; } = [];

        public List<IReadOnlyList<string>> CleanCalls { get; } = [];

        public GarbageReport ScanResult { get; set; } = new(0, 0);

        public GarbageReport CleanResult { get; set; } = new(0, 0);

        public OtherEnvironmentInfo GetEnvironmentInfo(string minecraftFolder, string configDirectory)
        {
            EnvironmentCalls.Add((minecraftFolder, configDirectory));
            return new OtherEnvironmentInfo("1.2.3", ".NET 8.0", "TestOS", minecraftFolder, configDirectory);
        }

        public GarbageReport ScanGarbage(IReadOnlyList<string> roots)
        {
            ScanCalls.Add(roots);
            return ScanResult;
        }

        public GarbageReport CleanGarbage(IReadOnlyList<string> roots)
        {
            CleanCalls.Add(roots);
            return CleanResult;
        }
    }

    private sealed class FakeFolderOpener : IFolderOpener
    {
        public List<string> Opened { get; } = [];

        public void Open(string path) => Opened.Add(path);
    }

    private static (FakeOtherToolsService Tools, FakeFolderOpener Opener, OtherPageViewModel ViewModel) CreateViewModel()
    {
        var tools = new FakeOtherToolsService();
        var opener = new FakeFolderOpener();
        var viewModel = new OtherPageViewModel(
            new FakeSettingsService(),
            new FakePlatformService(),
            tools,
            opener);
        return (tools, opener, viewModel);
    }

    [Fact]
    public void Constructor_LoadsEnvironmentInformation()
    {
        var (tools, _, viewModel) = CreateViewModel();

        Assert.Equal("1.2.3", viewModel.AppVersion);
        Assert.Equal(".NET 8.0", viewModel.Runtime);
        Assert.Equal("TestOS", viewModel.OperatingSystem);
        Assert.Equal("/games/mc", viewModel.MinecraftFolder);
        Assert.Equal("/config/pcl", viewModel.ConfigDirectory);
        var call = Assert.Single(tools.EnvironmentCalls);
        Assert.Equal("/games/mc", call.MinecraftFolder);
        Assert.Equal("/config/pcl", call.ConfigDirectory);
    }

    [Fact]
    public void ScanGarbage_UpdatesCountsAndStatus()
    {
        var (tools, _, viewModel) = CreateViewModel();
        tools.ScanResult = new GarbageReport(3, 42);

        viewModel.ScanGarbageCommand.Execute(null);

        Assert.Equal(3, viewModel.GarbageFileCount);
        Assert.Equal(42, viewModel.GarbageBytes);
        Assert.Contains("3 个临时文件", viewModel.StatusMessage);
        var roots = Assert.Single(tools.ScanCalls);
        Assert.Equal(["/games/mc", "/config/pcl"], roots);
    }

    [Fact]
    public void CleanGarbage_RemovesFilesAndResetsCounts()
    {
        var (tools, _, viewModel) = CreateViewModel();
        tools.ScanResult = new GarbageReport(0, 0);
        tools.CleanResult = new GarbageReport(2, 100);

        viewModel.CleanGarbageCommand.Execute(null);

        Assert.Contains("已清理 2 个临时文件", viewModel.StatusMessage);
        var cleanRoots = Assert.Single(tools.CleanCalls);
        Assert.Equal(["/games/mc", "/config/pcl"], cleanRoots);
        Assert.Empty(tools.ScanCalls);
        Assert.Equal(0, viewModel.GarbageFileCount);
    }

    [Fact]
    public void OpenGameFolder_OpensMinecraftFolder()
    {
        var (_, opener, viewModel) = CreateViewModel();

        viewModel.OpenGameFolderCommand.Execute(null);

        Assert.Equal(["/games/mc"], opener.Opened);
    }

    [Fact]
    public void OpenConfigFolder_OpensConfigDirectory()
    {
        var (_, opener, viewModel) = CreateViewModel();

        viewModel.OpenConfigFolderCommand.Execute(null);

        Assert.Equal(["/config/pcl"], opener.Opened);
    }
}
