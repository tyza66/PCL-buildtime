using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class GameLauncherTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _minecraftFolder;
    private readonly string _javaPath;
    private readonly VersionCatalogService _catalog = new();

    public GameLauncherTests()
    {
        _minecraftFolder = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaLaunch", Guid.NewGuid().ToString("N"));
        _javaPath = Path.Combine(_minecraftFolder, "java");
        Directory.CreateDirectory(_minecraftFolder);
        File.WriteAllText(_javaPath, "");
        BuildFixture();
    }

    public void Dispose()
    {
        if (Directory.Exists(_minecraftFolder))
        {
            Directory.Delete(_minecraftFolder, recursive: true);
        }
    }

    private static string OsName => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS()
            ? "osx"
            : "linux";

    private static string ArchSuffix => Environment.Is64BitProcess ? "64" : "32";

    private void BuildFixture()
    {
        var versions = Path.Combine(_minecraftFolder, "versions");
        Directory.CreateDirectory(versions);

        var rootFolder = Path.Combine(versions, "1.20.1");
        Directory.CreateDirectory(rootFolder);
        File.WriteAllText(
            Path.Combine(rootFolder, "1.20.1.json"),
            JsonSerializer.Serialize(new
            {
                id = "1.20.1",
                type = "release",
                releaseTime = "2023-06-12T00:00:00Z",
                mainClass = "net.minecraft.client.main.Main",
                assets = "1.20",
                assetIndex = new { id = "1.20" },
                arguments = new
                {
                    jvm = new object[]
                    {
                        "-Djava.library.path=${natives_directory}",
                        "-Dlauncher=${launcher_name}",
                        new
                        {
                            rules = new[] { new { action = "allow", os = new { name = OsName } } },
                            value = "-Dos.ok=true",
                        },
                    },
                    game = new[]
                    {
                        "--username ${auth_player_name}",
                        "--gameDir ${game_directory}",
                        "--assetIndex ${assets_index_name}",
                    },
                },
                libraries = new[]
                {
                    new
                    {
                        name = "com.example:core:1.0",
                        downloads = new { artifact = new { path = "com/example/core/1.0/core-1.0.jar" } },
                    },
                },
            }, JsonOptions));
        File.WriteAllText(Path.Combine(rootFolder, "1.20.1.jar"), "jar");

        var forgeFolder = Path.Combine(versions, "forge-1.20.1");
        Directory.CreateDirectory(forgeFolder);
        File.WriteAllText(
            Path.Combine(forgeFolder, "forge-1.20.1.json"),
            JsonSerializer.Serialize(new
            {
                id = "forge-1.20.1",
                type = "forge",
                releaseTime = "2023-06-15T00:00:00Z",
                inheritsFrom = "1.20.1",
                arguments = new
                {
                    game = new[] { "--tweakClass forge.bootstrapper.ForgeTweaker" },
                },
                libraries = new object[]
                {
                    new
                    {
                        name = "net.minecraftforge:forge:1.20.1",
                        downloads = new { artifact = new { path = "net/minecraftforge/forge/1.20.1/forge-1.20.1.jar" } },
                    },
                    new
                    {
                        name = "net.example:natives:1.0",
                        natives = new { windows = "natives-${arch}", osx = "natives-${arch}", linux = "natives-${arch}" },
                        rules = new[] { new { action = "allow", os = new { name = OsName } } },
                        downloads = new
                        {
                            classifiers = new Dictionary<string, object>
                            {
                                [$"natives-{ArchSuffix}"] = new
                                {
                                    path = $"net/example/natives/1.0/natives-1.0-natives-{ArchSuffix}.jar",
                                },
                            },
                        },
                    },
                },
            }, JsonOptions));

        WriteLibrary("com/example/core/1.0/core-1.0.jar");
        WriteLibrary("net/minecraftforge/forge/1.20.1/forge-1.20.1.jar");
        var nativeJar = Path.Combine(
            _minecraftFolder,
            "libraries",
            "net",
            "example",
            "natives",
            "1.0",
            $"natives-1.0-natives-{ArchSuffix}.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(nativeJar)!);
        using (var zip = ZipFile.Open(nativeJar, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("libexample.so").Open(), Encoding.UTF8))
            {
                writer.Write("native-binary");
            }

            zip.CreateEntry("META-INF/MANIFEST.MF");
        }
    }

    private void WriteLibrary(string relativePath)
    {
        var path = Path.Combine(_minecraftFolder, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "library");
    }

    private MinecraftVersion FindForgeVersion()
    {
        return _catalog.Scan(_minecraftFolder).Single(version => version.Id == "forge-1.20.1");
    }

    [Fact]
    public void BuildLaunchPlan_ResolvesInheritance_AndBuildsClassPath()
    {
        var version = FindForgeVersion();
        var settings = new AppSettings
        {
            MinecraftFolder = _minecraftFolder,
            UserName = "Steve",
            MaxMemoryMb = 4096,
        };

        var plan = new GameLauncher(_catalog).BuildLaunchPlan(version, settings, _javaPath);

        Assert.Equal("net.minecraft.client.main.Main", plan.MainClass);
        var classPathParts = plan.ClassPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(
            Path.Combine(_minecraftFolder, "libraries", "com", "example", "core", "1.0", "core-1.0.jar"),
            classPathParts);
        Assert.Contains(
            Path.Combine(_minecraftFolder, "libraries", "net", "minecraftforge", "forge", "1.20.1", "forge-1.20.1.jar"),
            classPathParts);
        Assert.Contains(Path.Combine(_minecraftFolder, "versions", "1.20.1", "1.20.1.jar"), classPathParts);
        Assert.Contains("-cp", plan.Arguments);
        Assert.Contains(plan.ClassPath, plan.Arguments);
    }

    [Fact]
    public void BuildLaunchPlan_AddsMemoryOfflineAndGameArguments()
    {
        var version = FindForgeVersion();
        var settings = new AppSettings
        {
            MinecraftFolder = _minecraftFolder,
            UserName = "Steve",
            MaxMemoryMb = 4096,
        };

        var plan = new GameLauncher(_catalog).BuildLaunchPlan(version, settings, _javaPath);

        Assert.Contains("-Xmx4096M", plan.Arguments);
        Assert.Contains("--username", plan.Arguments);
        Assert.Contains("Steve", plan.Arguments);
        Assert.Contains("--gameDir", plan.Arguments);
        Assert.Contains(_minecraftFolder, plan.Arguments);
        Assert.Contains("--assetIndex", plan.Arguments);
        Assert.Contains("1.20", plan.Arguments);
        Assert.Contains("--uuid", plan.Arguments);
        Assert.Contains("--accessToken", plan.Arguments);
        Assert.Contains("0", plan.Arguments);
        Assert.Contains("-Dlauncher=PCL2.Avalonia", plan.Arguments);
        Assert.Contains("-Dos.ok=true", plan.Arguments);
    }

    [Fact]
    public void BuildLaunchPlan_ExtractsNatives_AndReplacesMarkers()
    {
        var version = FindForgeVersion();
        var settings = new AppSettings
        {
            MinecraftFolder = _minecraftFolder,
            UserName = "Steve",
            MaxMemoryMb = 4096,
        };

        var plan = new GameLauncher(_catalog).BuildLaunchPlan(version, settings, _javaPath);

        Assert.True(Directory.Exists(plan.NativesDirectory));
        Assert.True(File.Exists(Path.Combine(plan.NativesDirectory, "libexample.so")));
        Assert.False(File.Exists(Path.Combine(plan.NativesDirectory, "META-INF", "MANIFEST.MF")));
        Assert.DoesNotContain(plan.Arguments, argument => argument.Contains("${auth_player_name}", StringComparison.Ordinal));
    }
}
