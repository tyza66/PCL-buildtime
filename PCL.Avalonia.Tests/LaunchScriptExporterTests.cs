using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class LaunchScriptExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "pcl-launch-script-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static LaunchPlan CreatePlan(params string[] arguments)
        => new()
        {
            JavaExecutable = "/usr/bin/java",
            WorkingDirectory = "/games/.minecraft",
            NativesDirectory = "/games/.minecraft/versions/1.20.1/1.20.1-natives",
            ClassPath = "libraries/a.jar:versions/1.20.1/1.20.1.jar",
            MainClass = "net.minecraft.client.main.Main",
            Arguments = arguments.Length > 0 ? arguments : ["-Xmx2G", "--gameDir", "/games/.minecraft"],
            Version = new MinecraftVersion
            {
                Id = "1.20.1",
                Folder = "/games/.minecraft/versions/1.20.1",
                JsonPath = "/games/.minecraft/versions/1.20.1/1.20.1.json",
            },
        };

    [Fact]
    public void Export_WritesWindowsBatch()
    {
        var exporter = new LaunchScriptExporter();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "launch.bat");

        var result = exporter.Export(CreatePlan(), path);

        Assert.Equal(path, result);
        var text = File.ReadAllText(path);
        Assert.Contains("@echo off", text);
        Assert.Contains("/usr/bin/java", text);
        Assert.Contains("/games/.minecraft", text);
        Assert.Contains("pause", text);
    }

    [Fact]
    public void Export_WritesUnixShellAndQuotesArguments()
    {
        var exporter = new LaunchScriptExporter();
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "launch.sh");

        exporter.Export(CreatePlan("--server", "my server name"), path);

        var text = File.ReadAllText(path);
        Assert.Contains("#!/bin/sh", text);
        Assert.Contains("cd", text);
        Assert.Contains("exec", text);
        Assert.Contains("\"my server name\"", text);
    }
}
