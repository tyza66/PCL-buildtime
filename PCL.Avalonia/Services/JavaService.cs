namespace PCL.Avalonia.Services;

public sealed class JavaService : IJavaService
{
    private readonly Func<string, string?> _environmentVariableProvider;

    public JavaService(Func<string, string?>? environmentVariableProvider = null)
    {
        _environmentVariableProvider = environmentVariableProvider ?? Environment.GetEnvironmentVariable;
    }

    public string? ResolveJavaExecutable(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.JavaPath) && File.Exists(settings.JavaPath))
        {
            return settings.JavaPath;
        }

        var javaHome = _environmentVariableProvider("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var candidate = GetJavaExecutableInBin(javaHome);
            if (candidate is not null && File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = _environmentVariableProvider("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = GetJavaExecutable(folder.Trim(' ', '"'));
            if (candidate is not null && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? GetJavaExecutableInBin(string javaHome)
    {
        if (string.IsNullOrWhiteSpace(javaHome))
        {
            return null;
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(javaHome, "bin", "java.exe")
            : Path.Combine(javaHome, "bin", "java");
    }

    private static string? GetJavaExecutable(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        return OperatingSystem.IsWindows()
            ? Path.Combine(folder, "java.exe")
            : Path.Combine(folder, "java");
    }
}
