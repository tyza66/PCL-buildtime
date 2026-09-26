using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PCL.Avalonia.Services;

public sealed class JavaListService : IJavaListService
{
    private readonly List<JavaInfo> _cache = [];
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _runJavaVersion;

    public JavaListService()
        : this(File.Exists, RunJavaVersion)
    {
    }

    internal JavaListService(Func<string, bool> fileExists, Func<string, string?> runJavaVersion)
    {
        _fileExists = fileExists;
        _runJavaVersion = runJavaVersion;
        Refresh();
    }

    public IReadOnlyList<JavaInfo> Scan() => _cache.AsReadOnly();

    public JavaInfo? GetJava(string path)
    {
        var normalized = Normalize(path);
        return _cache.FirstOrDefault(j => Normalize(j.Path).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void Refresh()
    {
        _cache.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in EnumerateCandidates())
        {
            if (!seen.Add(Normalize(candidate)))
            {
                continue;
            }

            var info = Inspect(candidate);
            if (info is not null)
            {
                _cache.Add(info);
            }
        }
    }

    private IEnumerable<string> EnumerateCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ProbeKnownLocations())
        {
            if (seen.Add(Normalize(path)))
            {
                yield return path;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            yield break;
        }

        foreach (var folder in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var executable in EnumerateJavaExecutables(folder.Trim(' ', '"')))
            {
                if (seen.Add(Normalize(executable)))
                {
                    yield return executable;
                }
            }
        }
    }

    private IEnumerable<string> ProbeKnownLocations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { programFiles, localAppData })
            {
                foreach (var candidate in ProbeDirectory(root))
                {
                    yield return candidate;
                }
            }

            yield return Path.Combine(programFiles, "Eclipse Adoptium");
            yield return Path.Combine(programFiles, "Java");
            yield return Path.Combine(programFiles, "Zulu");
        }
        else if (OperatingSystem.IsMacOS())
        {
            // macOS 的 JDK 在 *.jdk/Contents/Home、Homebrew 在 libexec 下，直接按 Home 布局展开。
            foreach (var candidate in new MacJavaLocator().LocateJavaExecutables())
            {
                yield return candidate;
            }
        }
        else
        {
            foreach (var candidate in ProbeDirectory(Path.Combine(home, ".sdkman", "candidates", "java")))
            {
                yield return candidate;
            }

            foreach (var candidate in ProbeDirectory("/usr/lib/jvm"))
            {
                yield return candidate;
            }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            foreach (var executable in EnumerateJavaExecutables(javaHome))
            {
                yield return executable;
            }
        }
    }

    private IEnumerable<string> ProbeDirectory(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var javaHome in SafeEnumerateDirectories(root))
        {
            foreach (var executable in EnumerateJavaExecutables(javaHome))
            {
                yield return executable;
            }
        }
    }

    private IEnumerable<string> EnumerateJavaExecutables(string javaHome)
        => OperatingSystem.IsWindows()
            ? new[] { Path.Combine(javaHome, "bin", "java.exe"), Path.Combine(javaHome, "javaw.exe") }
            : new[] { Path.Combine(javaHome, "bin", "java") };

    private JavaInfo? Inspect(string executable)
    {
        if (!_fileExists(executable))
        {
            return null;
        }

        var versionOutput = _runJavaVersion(executable);
        if (versionOutput is null)
        {
            return null;
        }

        var version = ParseVersion(versionOutput);
        var architecture = ParseArchitecture(versionOutput);
        return new JavaInfo(executable, version, architecture, ParseMajorVersion(version), IsValid: true);
    }

    private static string? RunJavaVersion(string executable)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "-version",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.WaitForExit(TimeSpan.FromSeconds(5));
            return process.StandardError.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string ParseVersion(string output)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output, @"version\s+""([^""]+)""");
        return match.Success ? match.Groups[1].Value : output.Trim();
    }

    // Java 8 用 1.8.0_x，现代版本直接 17.0.x / 21 / 25，这里统一抽“大版本号”供按版本匹配 Java。
    private static int ParseMajorVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return 0;
        }

        var cleaned = version.Split('-', '+')[0];
        var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var legacyMajor))
        {
            return legacyMajor;
        }

        return parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 0;
    }

    private static string ParseArchitecture(string output)
    {
        if (output.Contains("64-Bit", StringComparison.OrdinalIgnoreCase)
            || output.Contains("amd64", StringComparison.OrdinalIgnoreCase)
            || output.Contains("x86_64", StringComparison.OrdinalIgnoreCase))
        {
            return "x64";
        }

        if (output.Contains("aarch64", StringComparison.OrdinalIgnoreCase)
            || output.Contains("arm64", StringComparison.OrdinalIgnoreCase))
        {
            return "arm64";
        }

        return "unknown";
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (IOException)
        {
            return Enumerable.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Enumerable.Empty<string>();
        }
    }

    private static string Normalize(string path)
        => OperatingSystem.IsWindows()
            ? path.Replace('/', '\\').TrimEnd('\\')
            : path;
}
