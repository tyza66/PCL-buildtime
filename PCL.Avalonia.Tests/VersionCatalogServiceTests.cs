using System.Text.Json;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class VersionCatalogServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _minecraftFolder;
    private readonly VersionCatalogService _service = new();

    public VersionCatalogServiceTests()
    {
        _minecraftFolder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaVersions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_minecraftFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_minecraftFolder))
        {
            Directory.Delete(_minecraftFolder, recursive: true);
        }
    }

    private void WriteVersion(string id, string type, string releaseTime, string? inheritsFrom = null)
    {
        var folder = Path.Combine(_minecraftFolder, "versions", id);
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, id + ".json"),
            JsonSerializer.Serialize(new
            {
                id,
                type,
                releaseTime,
                inheritsFrom,
                mainClass = "net.minecraft.client.main.Main",
            }, JsonOptions));
    }

    [Fact]
    public void Scan_ReturnsVersions_SortedByReleaseTimeDescending()
    {
        WriteVersion("1.20.1", "release", "2023-06-12T14:00:00Z");
        WriteVersion("1.19.4", "release", "2023-03-14T13:00:00Z");
        WriteVersion("24w10a", "snapshot", "2024-03-06T12:00:00Z");

        var versions = _service.Scan(_minecraftFolder);

        Assert.Equal(["24w10a", "1.20.1", "1.19.4"], versions.Select(v => v.Id));
        Assert.Equal("snapshot", versions[0].Type);
        Assert.Null(versions[1].InheritsFrom);
        Assert.Equal(DateTimeOffset.Parse("2023-06-12T14:00:00Z"), versions[1].ReleaseTime);
    }

    [Fact]
    public void Scan_SkipsCorruptJson_AndFoldersWithoutJson()
    {
        WriteVersion("good", "release", "2024-01-01T00:00:00Z");
        var badFolder = Path.Combine(_minecraftFolder, "versions", "bad");
        Directory.CreateDirectory(badFolder);
        File.WriteAllText(Path.Combine(badFolder, "bad.json"), "{ not json !!");
        var emptyFolder = Path.Combine(_minecraftFolder, "versions", "empty");
        Directory.CreateDirectory(emptyFolder);

        var versions = _service.Scan(_minecraftFolder);

        Assert.Equal(["good"], versions.Select(v => v.Id));
    }

    [Fact]
    public void Scan_NoVersionsFolder_ReturnsEmpty()
    {
        Assert.Empty(_service.Scan(Path.Combine(_minecraftFolder, "missing")));
    }

    [Fact]
    public void LoadJson_FallsBackToAlternateJsonFile()
    {
        var folder = Path.Combine(_minecraftFolder, "versions", "custom");
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, "custom.json"),
            JsonSerializer.Serialize(new
            {
                id = "custom",
                type = "release",
                releaseTime = "2024-02-02T00:00:00Z",
                mainClass = "com.example.Main",
            }, JsonOptions));

        var json = _service.LoadJson(_minecraftFolder, "custom");

        Assert.NotNull(json);
        Assert.Equal("com.example.Main", json.MainClass);
    }
}
