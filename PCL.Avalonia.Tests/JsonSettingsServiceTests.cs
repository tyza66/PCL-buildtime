using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string _directory;

    public JsonSettingsServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var service = new JsonSettingsService(SettingsPath);

        var settings = service.Load();

        Assert.True(settings.UseDarkTheme);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSettings()
    {
        var service = new JsonSettingsService(SettingsPath);
        service.Save(new AppSettings { UseDarkTheme = false });

        var loaded = service.Load();

        Assert.False(loaded.UseDarkTheme);
    }

    [Fact]
    public void Load_WhenFileIsCorrupt_ReturnsDefaults()
    {
        File.WriteAllText(SettingsPath, "{ this is not valid json !!");
        var service = new JsonSettingsService(SettingsPath);

        var settings = service.Load();

        Assert.True(settings.UseDarkTheme);
    }
}
