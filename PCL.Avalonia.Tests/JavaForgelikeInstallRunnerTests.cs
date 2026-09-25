using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class JavaForgelikeInstallRunnerTests
{
    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Settings { get; set; } = new() { JavaPath = "/opt/java/bin/java" };

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

    private sealed class FakeJavaService : IJavaService
    {
        public string? Java { get; set; } = "/opt/java/bin/java";

        public string? ResolveJavaExecutable(AppSettings settings)
            => string.IsNullOrWhiteSpace(settings.JavaPath) ? Java : settings.JavaPath;
    }

    private sealed class RecordingLauncher
    {
        public Queue<IReadOnlyList<string>> Outputs { get; } = new();

        public List<JavaRunRequest> Requests { get; } = [];

        public Task<JavaRunResult> RunAsync(JavaRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new JavaRunResult(0, Outputs.Dequeue()));
        }
    }

    private static string ExtractNoop(string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        return destination;
    }

    private static string NewTempFolder()
        => Path.Combine(
            Path.GetTempPath(),
            "pcl-forgelike-tests",
            Guid.NewGuid().ToString("N"));

    private static JavaForgelikeInstallRunner CreateRunner(
        RecordingLauncher launcher,
        AppSettings? settings = null,
        string? java = null)
    {
        var settingsService = new FakeSettingsService();
        if (settings is not null)
        {
            settingsService.Settings = settings;
        }

        return new JavaForgelikeInstallRunner(
            settingsService,
            new FakeJavaService { Java = java },
            launcher.RunAsync,
            ExtractNoop);
    }

    [Theory]
    [InlineData("openjdk version \"17.0.9\" 2023-10-17", 17)]
    [InlineData("java version \"1.8.0_392\"", 8)]
    [InlineData("openjdk 21.0.2 2024-01-16", 21)]
    public void ParseJavaMajorVersion_ParsesCommonJavaOutputs(string output, int expected)
    {
        Assert.Equal(expected, JavaForgelikeInstallRunner.ParseJavaMajorVersion(output));
    }

    [Fact]
    public void ParseJavaMajorVersion_ReturnsNullForUnrecognizedOutput()
    {
        Assert.Null(JavaForgelikeInstallRunner.ParseJavaMajorVersion("command not found"));
    }

    [Fact]
    public void BuildArguments_WrapperAndJava9_AddsExportsAndWrapperJar()
    {
        var args = JavaForgelikeInstallRunner.BuildArguments(
            javaMajor: 17,
            minecraftFolder: "/games/mc",
            targetInstaller: "/games/mc/tmp/forge_installer.jar",
            injectorJar: "/games/mc/tmp/Cache/forge_installer.jar",
            wrapperJar: "/games/mc/tmp/Cache/JavaWrapper.jar",
            separator: ':',
            useWrapper: true);

        Assert.Equal("--add-exports", args[0]);
        Assert.Equal("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED", args[1]);
        Assert.Equal("-Doolloo.jlw.tmpdir=/games/mc/tmp", args[2]);
        Assert.Equal("-cp", args[3]);
        Assert.Equal(
            "/games/mc/tmp/Cache/forge_installer.jar:/games/mc/tmp/forge_installer.jar",
            args[4]);
        Assert.Equal("-jar", args[5]);
        Assert.Equal("/games/mc/tmp/Cache/JavaWrapper.jar", args[6]);
        Assert.Equal("com.bangbang93.ForgeInstaller", args[7]);
        Assert.Equal("/games/mc", args[8]);
    }

    [Fact]
    public void BuildArguments_NoWrapperAndJava8_UsesSemicolonClasspath()
    {
        var args = JavaForgelikeInstallRunner.BuildArguments(
            javaMajor: 8,
            minecraftFolder: @"C:\games\mc",
            targetInstaller: @"C:\games\mc\tmp\forge_installer.jar",
            injectorJar: @"C:\games\mc\tmp\Cache\forge_installer.jar",
            wrapperJar: @"C:\games\mc\tmp\Cache\JavaWrapper.jar",
            separator: ';',
            useWrapper: false);

        Assert.DoesNotContain(args, argument => argument == "--add-exports");
        Assert.DoesNotContain(args, argument => argument.StartsWith("-Doolloo", StringComparison.Ordinal));
        Assert.DoesNotContain(args, argument => argument == "-jar");
        var classPath = args[Array.IndexOf(args.ToArray(), "-cp") + 1];
        Assert.Contains(';', classPath);
        Assert.Equal("com.bangbang93.ForgeInstaller", args[^2]);
        Assert.Equal(@"C:\games\mc", args[^1]);
    }

    [Fact]
    public async Task RunAsync_RunsWrapperThenRetriesWithoutWrapper()
    {
        var launcher = new RecordingLauncher();
        launcher.Outputs.Enqueue(["openjdk version \"17.0.9\" 2023-10-17"]);
        launcher.Outputs.Enqueue(["Extracting json"]);
        launcher.Outputs.Enqueue(["Downloading libraries", "true"]);
        var runner = CreateRunner(launcher);
        var folder = NewTempFolder();

        await runner.RunAsync(
            folder,
            Path.Combine(folder, "tmp", "forge_installer.jar"),
            ForgelikeKind.Forge);

        Assert.Equal(3, launcher.Requests.Count);
        Assert.Equal(["-version"], launcher.Requests[0].Arguments);
        Assert.Contains("-jar", launcher.Requests[1].Arguments);
        Assert.Contains(
            launcher.Requests[1].Arguments,
            argument => argument.EndsWith("JavaWrapper.jar", StringComparison.Ordinal));
        Assert.DoesNotContain(launcher.Requests[2].Arguments, argument => argument == "-jar");
        Assert.All(launcher.Requests, request => Assert.Equal(folder, request.WorkingDirectory));
    }

    [Fact]
    public async Task RunAsync_TrueWithinLastFiveLines_DoesNotRetry()
    {
        var launcher = new RecordingLauncher();
        launcher.Outputs.Enqueue(["java version \"1.8.0_392\""]);
        launcher.Outputs.Enqueue(["line1", "line2", "line3", "line4", "true", "line6"]);
        var runner = CreateRunner(launcher);
        var folder = NewTempFolder();

        await runner.RunAsync(
            folder,
            Path.Combine(folder, "tmp", "forge_installer.jar"),
            ForgelikeKind.NeoForge);

        Assert.Equal(2, launcher.Requests.Count);
    }

    [Fact]
    public async Task RunAsync_WithoutSuccess_ThrowsAfterBothAttempts()
    {
        var launcher = new RecordingLauncher();
        launcher.Outputs.Enqueue(["openjdk version \"17.0.9\" 2023-10-17"]);
        launcher.Outputs.Enqueue(["Extracting json"]);
        launcher.Outputs.Enqueue(["Injecting profile"]);
        var runner = CreateRunner(launcher);
        var folder = NewTempFolder();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            folder,
            Path.Combine(folder, "tmp", "forge_installer.jar"),
            ForgelikeKind.Forge));

        Assert.Contains("安装器出错", ex.Message);
        Assert.Equal(3, launcher.Requests.Count);
        Assert.DoesNotContain(launcher.Requests[2].Arguments, argument => argument == "-jar");
    }

    [Fact]
    public async Task RunAsync_WithoutJava_ThrowsBeforeLaunching()
    {
        var launcher = new RecordingLauncher();
        var runner = CreateRunner(launcher, settings: new AppSettings(), java: null);
        var folder = NewTempFolder();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            folder,
            Path.Combine(folder, "tmp", "forge_installer.jar"),
            ForgelikeKind.Forge));

        Assert.Contains("Java", ex.Message);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public async Task RunAsync_ExtractsEmbeddedJarsIntoCacheFolder()
    {
        var launcher = new RecordingLauncher();
        launcher.Outputs.Enqueue(["openjdk version \"17.0.9\" 2023-10-17"]);
        launcher.Outputs.Enqueue(["true"]);
        var extractions = new List<string>();
        var folder = NewTempFolder();
        var settingsService = new FakeSettingsService();
        var runner = new JavaForgelikeInstallRunner(
            settingsService,
            new FakeJavaService(),
            launcher.RunAsync,
            destination =>
            {
                extractions.Add(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                return destination;
            });

        await runner.RunAsync(
            folder,
            Path.Combine(folder, "tmp", "forge_installer.jar"),
            ForgelikeKind.Forge);

        Assert.Contains(Path.Combine(folder, "tmp", "Cache", "forge-installer.jar"), extractions);
        Assert.Contains(Path.Combine(folder, "tmp", "Cache", "JavaWrapper.jar"), extractions);
    }
}
