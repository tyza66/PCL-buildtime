using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PCL.Avalonia.Services;

/// <summary>
/// macOS 的 Java 安装位置很分散：系统 JDK 在 *.jdk/Contents/Home 下，Homebrew 又藏在 libexec 目录里，
/// java_home -V 的清单打在 stderr，而 Finder 启动的 GUI 应用拿不到 shell 里的 JAVA_HOME。
/// 这里把这些入口统一成一组按系统注册顺序排好、已去重的 java 可执行文件路径。
/// </summary>
public sealed class MacJavaLocator
{
    private static readonly Regex JavaHomePathRegex = new(
        @"(/(?:[^\s""]+))",
        RegexOptions.Compiled);

    private readonly Func<string, string?> _javaHomeQuery;
    private readonly string[] _extraRoots;

    public MacJavaLocator()
        : this(QueryJavaHome)
    {
    }

    internal MacJavaLocator(Func<string, string?> javaHomeQuery)
        : this(javaHomeQuery, Array.Empty<string>())
    {
    }

    internal MacJavaLocator(Func<string, string?> javaHomeQuery, params string[] extraRoots)
    {
        _javaHomeQuery = javaHomeQuery;
        _extraRoots = extraRoots ?? Array.Empty<string>();
    }

    /// <summary>
    /// 按优先级返回本机存在的 java 可执行文件，重复安装（符号链接、java_home 与目录扫描同时命中）只保留第一个。
    /// </summary>
    public IReadOnlyList<string> LocateJavaExecutables()
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in EnumerateCandidates())
        {
            if (!File.Exists(candidate) || !seen.Add(RealPath(candidate)))
            {
                continue;
            }

            results.Add(candidate);
        }

        return results;
    }

    private IEnumerable<string> EnumerateCandidates()
    {
        // java_home -V 是系统认定已注册的 JDK 清单，顺序也最接近用户预期，放在最前。
        foreach (var home in ParseJavaHomeVerboseOutput(_javaHomeQuery("-V")))
        {
            yield return JavaExecutable(home);
        }

        // java_home 不带参数时返回用户默认的 JDK。
        var defaultHome = _javaHomeQuery(string.Empty);
        if (!string.IsNullOrWhiteSpace(defaultHome))
        {
            yield return JavaExecutable(defaultHome.Trim());
        }

        foreach (var path in DirectJavaHomes())
        {
            yield return path;
        }

        foreach (var root in _extraRoots.Concat(KnownRoots()))
        {
            foreach (var executable in ExpandRoot(root))
            {
                yield return executable;
            }
        }
    }

    internal static IReadOnlyList<string> ParseJavaHomeVerboseOutput(string? output)
    {
        var homes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(output))
        {
            return homes;
        }

        foreach (var line in output.Split('\n'))
        {
            var matches = JavaHomePathRegex.Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            // 每行形如 `    21.0.2 (arm64) "Vendor" - "Name" /path/Contents/Home`，
            // 裸路径行直接就是路径，都取行尾最后一个绝对路径。
            var home = matches[^1].Groups[1].Value.Trim();
            if (seen.Add(home))
            {
                homes.Add(home);
            }
        }

        return homes;
    }

    /// <summary>
    /// 把一个目录按 macOS 常见的三种 Java Home 布局展开成 java 路径：
    /// 目录本身、*.jdk/Contents/Home、Homebrew 的 libexec/openjdk.jdk/Contents/Home。
    /// </summary>
    internal static IEnumerable<string> JavaExecutablesUnderDirectory(string directory)
    {
        yield return JavaExecutable(directory);
        yield return JavaExecutable(Path.Combine(directory, "Contents", "Home"));
        yield return JavaExecutable(
            Path.Combine(directory, "libexec", "openjdk.jdk", "Contents", "Home"));
    }

    private static IEnumerable<string> ExpandRoot(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var executable in JavaExecutablesUnderDirectory(root))
        {
            yield return executable;
        }

        // 一层子目录覆盖 /Library/Java/JavaVirtualMachines 下的 *.jdk、/opt/homebrew/opt 下的 openjdk*、
        // sdkman/asdf 的版本目录；再深一层只走 /usr/local/Cellar/openjdk/<version>/libexec。
        foreach (var child in SafeEnumerateDirectories(root))
        {
            foreach (var executable in JavaExecutablesUnderDirectory(child))
            {
                yield return executable;
            }

            foreach (var grandChild in SafeEnumerateDirectories(child))
            {
                foreach (var executable in JavaExecutablesUnderDirectory(grandChild))
                {
                    yield return executable;
                }
            }
        }
    }

    private static IEnumerable<string> KnownRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return "/Library/Java/JavaVirtualMachines";
        yield return "/System/Library/Java/JavaVirtualMachines";
        yield return Path.Combine(home, "Library", "Java", "JavaVirtualMachines");
        yield return "/opt/homebrew/opt";
        yield return "/usr/local/opt";
        yield return "/usr/local/Cellar";
        yield return Path.Combine(home, ".sdkman", "candidates", "java");
        yield return Path.Combine(home, ".asdf", "installs", "java");
        yield return Path.Combine(home, ".jenv", "versions");
    }

    private static IEnumerable<string> DirectJavaHomes()
    {
        yield return JavaExecutable("/Library/Internet Plug-Ins/JavaAppletPlugin.plugin/Contents/Home");
    }

    private static string JavaExecutable(string javaHome)
    {
        return Path.Combine(javaHome, "bin", "java");
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string RealPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return File.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static string? QueryJavaHome(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/libexec/java_home",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit(TimeSpan.FromSeconds(5));
            Task.WaitAll(output, error);

            // -V 的清单写在 stderr，stdout 只放默认 JDK。
            return arguments.Contains("-V", StringComparison.Ordinal)
                ? error.Result
                : string.IsNullOrWhiteSpace(output.Result)
                    ? error.Result
                    : output.Result;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
