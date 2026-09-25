using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class VersionManagerServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _versionsRoot;
    private readonly VersionManagerService _service = new();

    public VersionManagerServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcl-version-manager-" + Guid.NewGuid().ToString("N"));
        _versionsRoot = Path.Combine(_root, "versions");
        Directory.CreateDirectory(_versionsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CreateVersion(string id)
    {
        var folder = Path.Combine(_versionsRoot, id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, id + ".json"), $$"""{"id":"{{id}}","mainClass":"net.minecraft.client.main.Main","type":"release"}""");
    }

    [Fact]
    public void LoadSettings_ReturnsDefaults_WhenIniMissing()
    {
        CreateVersion("1.20.1");

        var settings = _service.LoadSettings(_root, "1.20.1");

        Assert.False(settings.IsFavorite);
        Assert.False(settings.IsHidden);
        Assert.Equal("", settings.Description);
    }

    [Fact]
    public void SetFavoriteHiddenDescription_RoundTrips()
    {
        CreateVersion("1.20.1");

        _service.SetFavorite(_root, "1.20.1", true);
        _service.SetHidden(_root, "1.20.1", true);
        _service.SetDescription(_root, "1.20.1", "我的世界 1.20.1");

        var settings = _service.LoadSettings(_root, "1.20.1");

        Assert.True(settings.IsFavorite);
        Assert.True(settings.IsHidden);
        Assert.Equal("我的世界 1.20.1", settings.Description);
    }

    [Fact]
    public void Rename_MovesFolderRewritesJsonAndRenamesSupportFiles()
    {
        CreateVersion("1.20.1");
        var oldFolder = Path.Combine(_versionsRoot, "1.20.1");
        File.WriteAllText(Path.Combine(oldFolder, "1.20.1.jar"), "jar");
        Directory.CreateDirectory(Path.Combine(oldFolder, "1.20.1-natives"));
        Directory.CreateDirectory(Path.Combine(oldFolder, "PCL"));
        var oldPath = Path.Combine(_root, "versions", "1.20.1");
        File.WriteAllText(Path.Combine(oldFolder, "PCL", "Setup.ini"), $"IsStar=True{Environment.NewLine}Path={oldPath}");

        var newId = _service.Rename(_root, "1.20.1", "1.20.2");

        Assert.Equal("1.20.2", newId);
        Assert.False(Directory.Exists(oldFolder));
        var newFolder = Path.Combine(_versionsRoot, "1.20.2");
        Assert.True(File.Exists(Path.Combine(newFolder, "1.20.2.json")));
        Assert.True(File.Exists(Path.Combine(newFolder, "1.20.2.jar")));
        Assert.True(Directory.Exists(Path.Combine(newFolder, "1.20.2-natives")));
        Assert.False(File.Exists(Path.Combine(newFolder, "1.20.1.json")));
        var json = File.ReadAllText(Path.Combine(newFolder, "1.20.2.json"));
        Assert.Contains("\"id\":\"1.20.2\"", json);
        var ini = File.ReadAllText(Path.Combine(newFolder, "PCL", "Setup.ini"));
        Assert.Contains("1.20.2", ini);
        Assert.DoesNotContain("1.20.1", ini);
    }

    [Fact]
    public void Rename_RejectsInvalidOrExistingNames()
    {
        CreateVersion("1.20.1");

        Assert.Throws<ArgumentException>(() => _service.Rename(_root, "1.20.1", " "));
        Assert.Throws<ArgumentException>(() => _service.Rename(_root, "1.20.1", "../escape"));

        CreateVersion("1.20.2");
        Assert.Throws<InvalidOperationException>(() => _service.Rename(_root, "1.20.1", "1.20.2"));
    }

    [Fact]
    public void Delete_RemovesVersionFolder()
    {
        CreateVersion("1.20.1");

        _service.Delete(_root, "1.20.1");

        Assert.False(Directory.Exists(Path.Combine(_versionsRoot, "1.20.1")));
    }
}
