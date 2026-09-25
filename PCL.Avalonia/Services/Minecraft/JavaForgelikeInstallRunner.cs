using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using PCL.Avalonia.Services;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class JavaForgelikeInstallRunner : IForgelikeInstallRunner
{
    private const string InjectorFileName = "forge-installer.jar";
    private const string WrapperFileName = "JavaWrapper.jar";

    private readonly ISettingsService _settingsService;
    private readonly IJavaService _javaService;
    private readonly JavaProcessLauncher _processLauncher;
    private readonly Func<string, string> _extractResource;

    public JavaForgelikeInstallRunner(
        ISettingsService settingsService,
        IJavaService javaService)
        : this(settingsService, javaService, RunJavaProcessAsync, ExtractEmbeddedResource)
    {
    }

    internal JavaForgelikeInstallRunner(
        ISettingsService settingsService,
        IJavaService javaService,
        JavaProcessLauncher processLauncher,
        Func<string, string> extractResource)
    {
        _settingsService = settingsService;
        _javaService = javaService;
        _processLauncher = processLauncher;
        _extractResource = extractResource;
    }

    public async Task RunAsync(
        string minecraftFolder,
        string installerPath,
        ForgelikeKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        var settings = _settingsService.Load();
        var java = _javaService.ResolveJavaExecutable(settings);
        if (java is null)
        {
            throw new InvalidOperationException("未找到 Java，请先在设置页配置 Java 路径");
        }

        var versionResult = await _processLauncher(
                new JavaRunRequest(java, minecraftFolder, ["-version"]),
                cancellationToken)
            .ConfigureAwait(false);
        var javaMajor = ParseJavaMajorVersion(string.Join(Environment.NewLine, versionResult.Output))
            ?? throw new InvalidOperationException("无法识别 Java 版本");
        if (javaMajor < 8)
        {
            throw new InvalidOperationException($"Forge 安装需要 Java 8 或更高版本，当前为 {javaMajor}");
        }

        var cacheFolder = Path.Combine(minecraftFolder, "tmp", "Cache");
        var injectorJar = _extractResource(Path.Combine(cacheFolder, InjectorFileName));
        var wrapperJar = _extractResource(Path.Combine(cacheFolder, WrapperFileName));
        var target = Path.GetFullPath(installerPath);
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var displayName = kind == ForgelikeKind.NeoForge ? "NeoForge" : "Forge";

        var useWrapper = true;
        while (true)
        {
            try
            {
                var arguments = BuildArguments(
                    javaMajor,
                    minecraftFolder,
                    target,
                    injectorJar,
                    wrapperJar,
                    separator,
                    useWrapper);
                var result = await _processLauncher(
                        new JavaRunRequest(java, minecraftFolder, arguments),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (ReportsSuccess(result.Output))
                {
                    return;
                }

                if (useWrapper)
                {
                    useWrapper = false;
                    continue;
                }

                var lastLines = string.Join(Environment.NewLine, result.Output.TakeLast(5));
                throw new InvalidOperationException(
                    $"{displayName} 安装器出错，日志结束部分为：{lastLines}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (!useWrapper)
                {
                    throw;
                }

                useWrapper = false;
            }
        }
    }

    internal static int? ParseJavaMajorVersion(string output)
    {
        var match = Regex.Match(
            output,
            @"(?:version\s*""|(?:openjdk|java)\s+)(?<v>\d+(?:\.\d+)*)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var parts = match.Groups["v"].Value.Split('.');
        var first = int.Parse(parts[0], CultureInfo.InvariantCulture);
        return parts.Length > 1 && first == 1
            ? int.Parse(parts[1], CultureInfo.InvariantCulture)
            : first;
    }

    internal static IReadOnlyList<string> BuildArguments(
        int javaMajor,
        string minecraftFolder,
        string targetInstaller,
        string injectorJar,
        string wrapperJar,
        char separator,
        bool useWrapper)
    {
        var arguments = new List<string>();
        if (javaMajor >= 9)
        {
            arguments.Add("--add-exports");
            arguments.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED");
        }

        if (useWrapper)
        {
            var tmp = Path.Combine(minecraftFolder, "tmp").TrimEnd(Path.DirectorySeparatorChar);
            arguments.Add($"-Doolloo.jlw.tmpdir={tmp}");
        }

        arguments.Add("-cp");
        arguments.Add($"{injectorJar}{separator}{targetInstaller}");
        if (useWrapper)
        {
            arguments.Add("-jar");
            arguments.Add(wrapperJar);
        }

        arguments.Add("com.bangbang93.ForgeInstaller");
        arguments.Add(minecraftFolder);
        return arguments;
    }

    internal static bool ReportsSuccess(IReadOnlyList<string> output)
    {
        return output
            .TakeLast(5)
            .Any(line => line.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<JavaRunResult> RunJavaProcessAsync(
        JavaRunRequest request,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.JavaExecutable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var lines = new ConcurrentQueue<string>();
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult();
            }
            else
            {
                lines.Enqueue(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult();
            }
            else
            {
                lines.Enqueue(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Java 进程启动失败");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task
                .WhenAll(stdoutDone.Task, stderrDone.Task)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            return new JavaRunResult(process.ExitCode, lines.ToArray());
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private static string ExtractEmbeddedResource(string destination)
    {
        var resourceName = "Assets." + Path.GetFileName(destination);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"缺少内嵌资源 {resourceName}");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var output = File.Create(destination);
        stream.CopyTo(output);
        return destination;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 进程可能已经退出
        }
    }
}

internal sealed record JavaRunRequest(
    string JavaExecutable,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

internal sealed record JavaRunResult(int ExitCode, IReadOnlyList<string> Output);

internal delegate Task<JavaRunResult> JavaProcessLauncher(
    JavaRunRequest request,
    CancellationToken cancellationToken);
