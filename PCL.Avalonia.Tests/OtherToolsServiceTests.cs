using PCL.Avalonia.Services.Platform;

namespace PCL.Avalonia.Tests;

public sealed class OtherToolsServiceTests
{
    private readonly OtherToolsService _service = new();

    [Fact]
    public void GetEnvironmentInfo_ReturnsPathsAndRuntimeInformation()
    {
        var info = _service.GetEnvironmentInfo("/games/mc", "/config/pcl");

        Assert.Equal("/games/mc", info.MinecraftFolder);
        Assert.Equal("/config/pcl", info.ConfigDirectory);
        Assert.False(string.IsNullOrWhiteSpace(info.AppVersion));
        Assert.False(string.IsNullOrWhiteSpace(info.Runtime));
        Assert.False(string.IsNullOrWhiteSpace(info.OperatingSystem));
    }

    [Fact]
    public void ScanGarbage_FindsTemporaryFilesRecursively()
    {
        using var root = new TempFolder();
        Directory.CreateDirectory(Path.Combine(root.Path, "versions", "1.20.1"));
        Directory.CreateDirectory(Path.Combine(root.Path, "libraries"));
        Directory.CreateDirectory(Path.Combine(root.Path, "assets"));
        Directory.CreateDirectory(Path.Combine(root.Path, "mods"));
        File.WriteAllText(Path.Combine(root.Path, "versions", "1.20.1", "1.20.1.jar.tmp"), "abc");
        File.WriteAllText(Path.Combine(root.Path, "libraries", "foo.part"), "de");
        File.WriteAllText(Path.Combine(root.Path, "assets", "bar.download"), "f");
        File.WriteAllText(Path.Combine(root.Path, "mods", "keep.jar"), "keep");
        File.WriteAllText(Path.Combine(root.Path, "options.txt"), "keep");

        var report = _service.ScanGarbage([root.Path]);

        Assert.Equal(3, report.FileCount);
        Assert.Equal(6, report.Bytes);
    }

    [Fact]
    public void CleanGarbage_RemovesOnlyTemporaryFiles()
    {
        using var root = new TempFolder();
        var first = Path.Combine(root.Path, "one.tmp");
        var second = Path.Combine(root.Path, "two.download");
        var keep = Path.Combine(root.Path, "keep.jar");
        File.WriteAllText(first, "123");
        File.WriteAllText(second, "45");
        File.WriteAllText(keep, "keep");

        var report = _service.CleanGarbage([root.Path]);

        Assert.Equal(2, report.FileCount);
        Assert.Equal(5, report.Bytes);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(keep));
        Assert.Equal(0, _service.ScanGarbage([root.Path]).FileCount);
    }

    [Fact]
    public void ScanGarbage_IgnoresMissingRoots()
    {
        var report = _service.ScanGarbage(["/not/on/disk", ""]);

        Assert.Equal(0, report.FileCount);
        Assert.Equal(0, report.Bytes);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pcl-other-tools-" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
