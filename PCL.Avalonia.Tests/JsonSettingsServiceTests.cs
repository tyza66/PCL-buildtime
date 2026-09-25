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
        Assert.Equal(DownloadSource.Bmclapi, settings.DownloadSource);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSettings()
    {
        var service = new JsonSettingsService(SettingsPath);
        service.Save(new AppSettings
        {
            UseDarkTheme = false,
            MinecraftFolder = "/games/minecraft",
            JavaPath = "/opt/java/bin/java",
            UserName = "Steve",
            MaxMemoryMb = 8192,
            DownloadSource = DownloadSource.Mojang,
        });

        var loaded = service.Load();

        Assert.False(loaded.UseDarkTheme);
        Assert.Equal("/games/minecraft", loaded.MinecraftFolder);
        Assert.Equal("/opt/java/bin/java", loaded.JavaPath);
        Assert.Equal("Steve", loaded.UserName);
        Assert.Equal(8192, loaded.MaxMemoryMb);
        Assert.Equal(DownloadSource.Mojang, loaded.DownloadSource);
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
