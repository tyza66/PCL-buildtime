using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class MacJavaLocatorTests : IDisposable
{
    private readonly string _root;

    public MacJavaLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaMacJava", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateJavaHome(string relativePath)
    {
        var home = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", "java"), string.Empty);
        return home;
    }

    [Fact]
    public void ParseJavaHomeVerboseOutput_ExtractsRegisteredHomesInOrder()
    {
        var output = """
            Matching Java Virtual Machines (2):
                21.0.2 (arm64) "Homebrew" - "OpenJDK 21.0.2" /opt/homebrew/opt/openjdk/libexec/openjdk.jdk/Contents/Home
                17.0.9 (x86_64) "Oracle Corporation" - "Java SE 17.0.9" /Library/Java/JavaVirtualMachines/jdk-17.jdk/Contents/Home
            /opt/homebrew/opt/openjdk/libexec/openjdk.jdk/Contents/Home
            """;

        var homes = MacJavaLocator.ParseJavaHomeVerboseOutput(output);

        Assert.Equal(2, homes.Count);
        Assert.Equal("/opt/homebrew/opt/openjdk/libexec/openjdk.jdk/Contents/Home", homes[0]);
        Assert.Equal("/Library/Java/JavaVirtualMachines/jdk-17.jdk/Contents/Home", homes[1]);
    }

    [Fact]
    public void ParseJavaHomeVerboseOutput_IgnoresMissingOutput()
    {
        Assert.Empty(MacJavaLocator.ParseJavaHomeVerboseOutput(null));
        Assert.Empty(MacJavaLocator.ParseJavaHomeVerboseOutput(string.Empty));
        Assert.Empty(MacJavaLocator.ParseJavaHomeVerboseOutput("Unable to find any JVMs matching version \"1.8+\""));
    }

    [Fact]
    public void JavaExecutablesUnderDirectory_MapsBundleAndHomebrewLayouts()
    {
        var candidates = MacJavaLocator.JavaExecutablesUnderDirectory("/opt/homebrew/opt/openjdk").ToList();

        Assert.Contains("/opt/homebrew/opt/openjdk/bin/java", candidates);
        Assert.Contains("/opt/homebrew/opt/openjdk/Contents/Home/bin/java", candidates);
        Assert.Contains("/opt/homebrew/opt/openjdk/libexec/openjdk.jdk/Contents/Home/bin/java", candidates);
    }

    [Fact]
    public void Locate_PrefersJavaHomeResult_AndDeduplicatesRepeats()
    {
        var home = CreateJavaHome(Path.Combine("openjdk-21.jdk", "Contents", "Home"));
        var listing = $"""
Matching Java Virtual Machines (1):
    21.0.2 (arm64) "Vendor" - "OpenJDK 21.0.2" {home}
{home}
""";
        var locator = new MacJavaLocator(_ => listing, _root);

        var result = locator.LocateJavaExecutables();

        var expected = Path.Combine(home, "bin", "java");
        Assert.NotEmpty(result);
        Assert.Equal(expected, result[0]);
        Assert.Single(result, path => path == expected);
    }

    [Fact]
    public void Locate_FindsJdkBundlesOnDisk()
    {
        var home = CreateJavaHome(Path.Combine("jdk-17.jdk", "Contents", "Home"));
        var locator = new MacJavaLocator(_ => null, _root);

        var result = locator.LocateJavaExecutables();

        // 目录扫描是唯一入口时，.jdk 包内的 Contents/Home/bin/java 也必须被找到。
        Assert.Contains(Path.Combine(home, "bin", "java"), result);
    }

    [Fact]
    public void Locate_MatchesRealSystemJavaHome_OnMacOs()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var defaultHome = RunJavaHome(string.Empty);
        if (string.IsNullOrWhiteSpace(defaultHome))
        {
            // 机器上一个 JDK 都没装时无从验证。
            return;
        }

        var parsed = MacJavaLocator.ParseJavaHomeVerboseOutput(RunJavaHome("-V"));
        Assert.Contains(defaultHome.Trim(), parsed);

        var located = new MacJavaLocator().LocateJavaExecutables();
        Assert.Contains(Path.Combine(defaultHome.Trim(), "bin", "java"), located);
    }

    private static string? RunJavaHome(string arguments)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/libexec/java_home",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            process.WaitForExit(TimeSpan.FromSeconds(5));
            return output.Result;
        }
        catch
        {
            return null;
        }
    }
}
