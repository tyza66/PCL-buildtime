using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class JavaServiceTests : IDisposable
{
    private readonly string _directory;

    public JavaServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PCL2AvaloniaJava", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string JavaFileName => OperatingSystem.IsWindows() ? "java.exe" : "java";

    private sealed class StubJavaList : IJavaListService
    {
        private readonly List<JavaInfo> _infos;

        public StubJavaList(IEnumerable<JavaInfo> infos)
        {
            _infos = infos.ToList();
        }

        public IReadOnlyList<JavaInfo> Scan() => _infos;

        public JavaInfo? GetJava(string path)
            => _infos.FirstOrDefault(j => j.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

        public void Refresh()
        {
        }
    }

    [Fact]
    public void Resolve_PrefersConfiguredJavaPath()
    {
        var javaPath = Path.Combine(_directory, JavaFileName);
        File.WriteAllText(javaPath, "");
        var service = new JavaService();

        var resolved = service.ResolveJavaExecutable(new AppSettings { JavaPath = javaPath });

        Assert.Equal(javaPath, resolved);
    }

    [Fact]
    public void Resolve_UsesJavaHome_WhenConfiguredPathMissing()
    {
        var javaHome = Path.Combine(_directory, "jdk");
        Directory.CreateDirectory(Path.Combine(javaHome, "bin"));
        var javaPath = Path.Combine(javaHome, "bin", JavaFileName);
        File.WriteAllText(javaPath, "");
        Assert.True(File.Exists(javaPath));
        var service = new JavaService(name => name == "JAVA_HOME" ? javaHome : null);

        var resolved = service.ResolveJavaExecutable(new AppSettings());

        Assert.Equal(javaPath, resolved);
    }

    [Fact]
    public void Resolve_ByMajorVersion_PicksInstalledJava()
    {
        var service = new JavaService(_ => null, new StubJavaList(
        [
            new("/jdk/8/bin/java", "1.8.0_452", "arm64", 8, true),
            new("/jdk/17/bin/java", "17.0.9", "arm64", 17, true),
            new("/jdk/25/bin/java", "25.0.4.1", "arm64", 25, true),
        ]));

        // 版本点名 25（如 MC 26.3）就该用 Java 25，而不是默认落到 Java 8。
        Assert.Equal("/jdk/25/bin/java", service.ResolveJavaExecutable(new AppSettings(), requiredMajorVersion: 25));
        Assert.Equal("/jdk/17/bin/java", service.ResolveJavaExecutable(new AppSettings(), requiredMajorVersion: 17));
        // 没有恰好 21 时，取大于它的大版本里最小的一个（25）。
        Assert.Equal("/jdk/25/bin/java", service.ResolveJavaExecutable(new AppSettings(), requiredMajorVersion: 21));
    }

    [Fact]
    public void Resolve_ByMajorVersion_OverridesUserSelectionThatIsTooOld()
    {
        var oldJava = Path.Combine(_directory, JavaFileName);
        File.WriteAllText(oldJava, "");
        var service = new JavaService(_ => null, new StubJavaList(
        [
            new(oldJava, "1.8.0_452", "arm64", 8, true),
            new("/jdk/25/bin/java", "25.0.4.1", "arm64", 25, true),
        ]));

        // 用户选了 Java 8，但版本需要 25：宁可换一个匹配的，也不拿老 Java 去跑新版本。
        var resolved = service.ResolveJavaExecutable(new AppSettings { JavaPath = oldJava }, requiredMajorVersion: 25);

        Assert.Equal("/jdk/25/bin/java", resolved);
    }

    [Fact]
    public void Resolve_ByMajorVersion_MatchesRealInstalledJdk()
    {
        // 真扫本机已安装 JDK（逐个执行 java -version），挑最高大版本看能否解析回一个同版本的 Java。
        var list = new JavaListService();
        var installed = list.Scan().Where(java => java.IsValid && java.MajorVersion > 0).ToList();
        if (installed.Count == 0)
        {
            return;
        }

        var target = installed.Max(java => java.MajorVersion);
        var service = new JavaService();
        var resolved = service.ResolveJavaExecutable(new AppSettings(), requiredMajorVersion: target);

        Assert.NotNull(resolved);
        Assert.Equal(target, list.GetJava(resolved!)?.MajorVersion);
    }
}
