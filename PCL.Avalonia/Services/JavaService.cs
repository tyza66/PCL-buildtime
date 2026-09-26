namespace PCL.Avalonia.Services;

public sealed class JavaService : IJavaService
{
    private readonly Func<string, string?> _environmentVariableProvider;
    private IJavaListService? _javaListService;
    private MacJavaLocator? _macJavaLocator;

    private IJavaListService JavaList => _javaListService ??= new JavaListService();

    public JavaService(
        Func<string, string?>? environmentVariableProvider = null,
        IJavaListService? javaListService = null)
    {
        _environmentVariableProvider = environmentVariableProvider ?? Environment.GetEnvironmentVariable;
        _javaListService = javaListService;
    }

    public string? ResolveJavaExecutable(AppSettings settings)
        => ResolveJavaExecutable(settings, requiredMajorVersion: null);

    public string? ResolveJavaExecutable(AppSettings settings, int? requiredMajorVersion)
    {
        if (requiredMajorVersion is null)
        {
            return ResolveDefaultJava(settings);
        }

        // 用户显式指定且大版本够用就尊重，否则版本清单说了算，避免拿 Java 8/17 去跑需要 25 的新版本。
        if (!string.IsNullOrWhiteSpace(settings.JavaPath) && File.Exists(settings.JavaPath))
        {
            var selectedMajor = JavaList.GetJava(settings.JavaPath)?.MajorVersion;
            if (selectedMajor is null or 0 || selectedMajor >= requiredMajorVersion.Value)
            {
                return settings.JavaPath;
            }
        }

        return SelectJavaByMajor(requiredMajorVersion.Value) ?? ResolveDefaultJava(settings);
    }

    private string? ResolveDefaultJava(AppSettings settings)
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

        // macOS 的 GUI 应用拿不到 shell 的 JAVA_HOME，系统 JDK 又在 .jdk/Contents/Home 里，单独查一次。
        if (OperatingSystem.IsMacOS())
        {
            _macJavaLocator ??= new MacJavaLocator();
            var macJava = _macJavaLocator.LocateJavaExecutables().FirstOrDefault();
            if (macJava is not null)
            {
                return macJava;
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

    // 只在“版本清单点名要某个大版本”时走到这：先精确匹配，再取大于它的大版本里最小的一个，最后兜底最高版本。
    private string? SelectJavaByMajor(int requiredMajor)
    {
        var installed = JavaList.Scan()
            .Where(java => java.IsValid && java.MajorVersion > 0)
            .ToList();
        if (installed.Count == 0)
        {
            return null;
        }

        return installed.FirstOrDefault(java => java.MajorVersion == requiredMajor)?.Path
            ?? installed.Where(java => java.MajorVersion > requiredMajor)
                .OrderBy(java => java.MajorVersion)
                .FirstOrDefault()?.Path
            ?? installed.OrderBy(java => java.MajorVersion).Last().Path;
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
