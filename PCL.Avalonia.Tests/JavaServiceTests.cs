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
}
