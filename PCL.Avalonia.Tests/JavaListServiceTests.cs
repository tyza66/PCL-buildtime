using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class JavaListServiceTests
{
    [Fact(Skip = "需要 Linux 环境的 Java 安装")]
    public void Scan_DetectsJavaFromKnownLocations()
    {
        var fileExists = new Func<string, bool>(path =>
            path == "/usr/lib/jvm/java-17/bin/java");

        var runJava = new Func<string, string?>(path =>
            path == "/usr/lib/jvm/java-17/bin/java"
                ? "openjdk version \"17.0.6\" 2023-01-17\nOpenJDK Runtime Environment (build 17.0.6+10)\nOpenJDK 64-Bit Server VM (build 17.0.6+10, mixed mode, sharing)"
                : null);

        var service = new JavaListService(fileExists, runJava);
        var result = service.Scan();

        Assert.NotEmpty(result);
        var java = result[0];
        Assert.Equal("/usr/lib/jvm/java-17/bin/java", java.Path);
        Assert.Equal("17.0.6", java.Version);
        Assert.Equal("x64", java.Architecture);
        Assert.True(java.IsValid);
    }

    [Fact]
    public void Scan_FiltersOutInvalidFiles()
    {
        var service = new JavaListService(_ => false, _ => null);
        var result = service.Scan();

        Assert.All(result, j => Assert.False(j.IsValid && j.Version == string.Empty));
    }

    [Fact(Skip = "需要 Linux 环境的 Java 安装")]
    public void GetJava_FindsMatchingEntry()
    {
        var fileExists = new Func<string, bool>(path => path == "/opt/java/bin/java");
        var runJava = new Func<string, string?>(_ => "openjdk version \"11.0.0\" 64-Bit");
        var service = new JavaListService(fileExists, runJava);

        var java = service.GetJava("/opt/java/bin/java");

        Assert.NotNull(java);
        Assert.Equal("/opt/java/bin/java", java!.Path);
    }

    [Fact]
    public void GetJava_ReturnsNullWhenMissing()
    {
        var service = new JavaListService(_ => false, _ => null);

        var java = service.GetJava("/nonexistent/java");

        Assert.Null(java);
    }

    [Fact]
    public void Refresh_ReplacesCachedResults()
    {
        var callCount = 0;
        var fileExists = new Func<string, bool>(_ =>
        {
            callCount++;
            return false;
        });
        var runJava = new Func<string, string?>(_ => null);

        var service = new JavaListService(fileExists, runJava);
        var firstCount = callCount;

        service.Refresh();
        var secondCount = callCount;

        Assert.True(secondCount > firstCount);
    }

    [Fact]
    public void ParseArchitecture_DetectsArm64()
    {
        var fileExists = new Func<string, bool>(_ => true);
        var runJava = new Func<string, string?>(_ => "openjdk version \"17.0.0\" aarch64");
        var service = new JavaListService(fileExists, runJava);

        var java = service.Scan().FirstOrDefault();

        Assert.NotNull(java);
        Assert.Equal("arm64", java!.Architecture);
    }
}
