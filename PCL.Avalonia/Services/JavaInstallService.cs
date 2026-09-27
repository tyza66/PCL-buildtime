using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services;

/// <summary>
/// 从 Adoptium 下载并解压 JDK：设置页提示"去装 Java"之后，用户不用再自己开浏览器、
/// 找下载页、解压、填路径——一条命令全包。只落在启动器自己的目录，不动系统里已有的 Java。
/// </summary>
public sealed class JavaInstallService : IJavaInstallService
{
    private const string AdoptiumAssetsUrl = "https://api.adoptium.net/v3/assets/latest";

    private readonly IDownloadClient _downloadClient;
    private readonly Func<string> _installRootProvider;
    private readonly Func<Architecture> _architectureProvider;
    private readonly Func<string, string, Task> _extractor;
    private readonly Func<DownloadSource> _downloadSourceProvider;

    // 下载源设置缺省按官方源： ?? 右边不能直接写 lambda（没有目标类型，编译不过），先固定成字段。
    private static readonly Func<DownloadSource> DefaultDownloadSourceProvider = () => DownloadSource.Mojang;

    public JavaInstallService(
        IDownloadClient downloadClient,
        IPlatformService platformService,
        ISettingsService settingsService)
        : this(
            downloadClient,
            () => LauncherJavaPaths.Root(platformService),
            () => RuntimeInformation.OSArchitecture,
            ExtractPackageAsync,
            () => settingsService.Load().DownloadSource)
    {
    }

    internal JavaInstallService(
        IDownloadClient downloadClient,
        Func<string> installRootProvider,
        Func<Architecture> architectureProvider,
        Func<string, string, Task> extractor,
        Func<DownloadSource>? downloadSourceProvider = null)
    {
        _downloadClient = downloadClient;
        _installRootProvider = installRootProvider;
        _architectureProvider = architectureProvider;
        _extractor = extractor;
        _downloadSourceProvider = downloadSourceProvider ?? DefaultDownloadSourceProvider;
    }

    public async Task<JavaRelease> FetchLatestAsync(
        int majorVersion,
        CancellationToken cancellationToken = default)
    {
        var architecture = DescribeArchitecture(_architectureProvider());
        var operatingSystem = DescribeOperatingSystem();

        string json;
        try
        {
            // 兜底查询只去掉厂商限制：微软、Azul 等也发布 macOS 包。os 和架构必须留住，
            // 少了它们接口会按默认系统返回，解压出来的 java 在本机根本跑不起来。
            json = await _downloadClient.GetStringAsync(
                [
                    $"{AdoptiumAssetsUrl}/{majorVersion}/hotspot?vendor=eclipse&image_type=jdk"
                        + $"&os={operatingSystem}&architecture={architecture}",
                    $"{AdoptiumAssetsUrl}/{majorVersion}/hotspot?image_type=jdk"
                        + $"&os={operatingSystem}&architecture={architecture}",
                ],
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"查询 Java {majorVersion} 安装包失败：{ex.Message}。可尝试：检查网络连接后重试，"
                + "或到 adoptium.net 手动下载对应架构的 JDK", ex);
        }

        AdoptiumAsset[]? assets;
        try
        {
            assets = JsonSerializer.Deserialize(json, JavaInstallJsonContext.Default.AdoptiumAssetArray);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "下载源返回的 Java 版本信息无法解析。可尝试：稍后重试，或到 adoptium.net 手动下载", ex);
        }

        // 挑条目时按本机实际情况过滤：接口在某些查询下会混进别的系统或架构的安装包。
        var asset = assets?.FirstOrDefault(item => MatchesMachine(item, operatingSystem, architecture));
        // Link 直接在这里收住：下面拼下载地址时它不是 null，运行时才发现就晚了。
        if (asset?.Binary?.Package is not { Link: { Length: > 0 } link } package
            || asset.Version is not { } version)
        {
            throw new InvalidOperationException(
                $"下载源没有提供适合本机（{operatingSystem} / {architecture}）的 Java {majorVersion}。"
                + $"可尝试：到 adoptium.net 手动下载 {architecture} 架构的 JDK {majorVersion}，"
                + "解压后在设置页填到 Java 路径");
        }

        return new JavaRelease(
            asset.ReleaseName ?? $"jdk-{majorVersion}",
            version.Major,
            link,
            package.Size,
            asset.Binary.Architecture ?? architecture,
            asset.Binary.Os ?? operatingSystem);
    }

    public async Task<string> InstallAsync(
        int majorVersion,
        IProgress<JavaInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var release = await FetchLatestAsync(majorVersion, cancellationToken).ConfigureAwait(false);
        var root = _installRootProvider();
        var versionDirectory = Path.Combine(root, SanitizeDirectoryName(release.Version));
        var existing = FindJavaExecutable(versionDirectory);
        if (existing is not null)
        {
            progress?.Report(new JavaInstallProgress(1, "已就绪"));
            return existing;
        }

        var packageName = Path.GetFileName(new Uri(release.PackageUrl).AbsolutePath);
        var packagePath = Path.Combine(root, ".download", packageName);
        progress?.Report(new JavaInstallProgress(0, $"开始下载 Java {release.MajorVersion}"));
        await _downloadClient.DownloadAsync(
            new DownloadRequest(
                BuildPackageUrls(release),
                packagePath,
                $"Java {release.MajorVersion} 安装包",
                // 接口给了包大小就顺手校验：下到一半被截断的 180MB 比直接失败更难查。
                release.PackageSize),
            // 下载进度要按真实百分比走：180MB 一个数都不跳，用户只会以为界面卡死了。
            new DownloadProgressForwarder(progress, release.MajorVersion),
            cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report(new JavaInstallProgress(0.99, "正在解压安装包"));
            Directory.CreateDirectory(versionDirectory);
            await _extractor(packagePath, versionDirectory).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDeleteDirectory(versionDirectory);
            throw new InvalidOperationException(
                $"解压 Java {release.MajorVersion} 安装包失败：{ex.Message}。"
                + "可尝试：确认磁盘剩余空间充足后重试，或删除启动器数据目录下的 java 文件夹后重试", ex);
        }

        var java = FindJavaExecutable(versionDirectory)
            ?? throw new InvalidOperationException(
                $"安装包解压后没有找到 java 可执行文件。可尝试：删除安装目录后重试，"
                + "或到 adoptium.net 手动下载并解压后在设置页指定路径");

        progress?.Report(new JavaInstallProgress(1, "安装完成"));
        return java;
    }

    /// <summary>
    /// 安装包的下载顺序跟着用户的下载源设置走：默认走国内镜像的用户先试清华源，选官方源的先试 GitHub。
    /// GitHub 直连在海外快、在国内经常只有几十 KB/s，顺序摆错就是让用户干等。
    /// </summary>
    private IReadOnlyList<string> BuildPackageUrls(JavaRelease release)
    {
        var urls = new List<string> { release.PackageUrl };
        var mirror = TsinghuaMirrorUrl(release);
        if (mirror is null)
        {
            return urls;
        }

        if (_downloadSourceProvider() == DownloadSource.Bmclapi)
        {
            urls.Insert(0, mirror);
        }
        else
        {
            urls.Add(mirror);
        }

        return urls;
    }

    /// <summary>清华源只镜像 Adoptium 官方 GitHub release，别的厂商（Azul、微软）的包不在里面，别硬拼。</summary>
    private static string? TsinghuaMirrorUrl(JavaRelease release)
    {
        if (!release.PackageUrl.StartsWith("https://github.com/adoptium/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileName = Path.GetFileName(new Uri(release.PackageUrl).AbsolutePath);
        if (fileName.Length == 0)
        {
            return null;
        }

        return $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{release.MajorVersion}/jdk/"
            + $"{release.Architecture}/{release.OperatingSystem}/{fileName}";
    }

    /// <summary>接口偶尔会混进别的系统或架构的条目，挑的时候必须按本机实际情况过滤。</summary>
    private static bool MatchesMachine(AdoptiumAsset asset, string operatingSystem, string architecture)
        => asset.Binary?.Package?.Link is { Length: > 0 }
            && SameToken(asset.Binary.Os, operatingSystem)
            && SameToken(asset.Binary.Architecture, architecture);

    private static bool SameToken(string? actual, string expected)
        => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>解压根可能多套一层目录（jdk-21.0.5+11/bin/java），按深度优先找最浅的那个。</summary>
    internal static string? FindJavaExecutable(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var queue = new Queue<string>();
        queue.Enqueue(directory);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var executable = OperatingSystem.IsWindows()
                ? Path.Combine(current, "bin", "java.exe")
                : Path.Combine(current, "bin", "java");
            if (File.Exists(executable))
            {
                return executable;
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }

    private static async Task ExtractPackageAsync(string packagePath, string targetDirectory)
    {
        if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(packagePath, targetDirectory, overwriteFiles: true);
            return;
        }

        if (!packagePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            && !packagePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"不认识的安装包格式：{Path.GetFileName(packagePath)}");
        }

        // .NET 没有内置 tar.gz，macOS / Linux 都自带 tar，直接调系统命令，别为了这个引第三方库。
        var startInfo = new ProcessStartInfo("tar")
        {
            ArgumentList = { "-xzf", packagePath, "-C", targetDirectory },
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 tar 解压安装包");
        var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"tar 解压失败（退出码 {process.ExitCode}）：{FirstLine(error)}");
        }
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "无详细输出";
        }

        var index = text.IndexOfAny(['\r', '\n']);
        var line = index >= 0 ? text[..index] : text;
        return line.Length > 120 ? line[..120] + "..." : line.Trim();
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 解压失败时清场失败不致命：下次 FindJavaExecutable 找不到 java 会重新解压。
        }
    }

    private static string SanitizeDirectoryName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return cleaned.Length == 0 ? "jdk" : cleaned;
    }

    /// <summary>
    /// 把下载客户端的进度直接翻译成安装进度。不用框架自带的 <see cref="Progress{T}"/>：
    /// 它在没有同步上下文时把回调丢到线程池，下载中的百分比就会时有时无。
    /// </summary>
    private sealed class DownloadProgressForwarder(IProgress<JavaInstallProgress>? inner, int majorVersion)
        : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => inner?.Report(new JavaInstallProgress(
            0.99 * value.Fraction,
            $"正在下载 Java {majorVersion}（{value.Fraction * 100:0}%）"));
    }

    private static string DescribeOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        return OperatingSystem.IsMacOS() ? "mac" : "linux";
    }

    private static string DescribeArchitecture(Architecture architecture)
        => architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "x86",
            _ => throw new InvalidOperationException(
                $"本机架构（{architecture}）暂不支持一键安装 Java。可尝试：到 adoptium.net 手动下载后在设置页指定路径"),
        };
}

[JsonSerializable(typeof(AdoptiumAsset[]))]
internal partial class JavaInstallJsonContext : JsonSerializerContext;

internal sealed class AdoptiumAsset
{
    [JsonPropertyName("release_name")]
    public string? ReleaseName { get; set; }

    [JsonPropertyName("version")]
    public AdoptiumVersion? Version { get; set; }

    [JsonPropertyName("binary")]
    public AdoptiumBinary? Binary { get; set; }
}

internal sealed class AdoptiumVersion
{
    [JsonPropertyName("major")]
    public int Major { get; set; }
}

internal sealed class AdoptiumBinary
{
    [JsonPropertyName("os")]
    public string? Os { get; set; }

    [JsonPropertyName("architecture")]
    public string? Architecture { get; set; }

    [JsonPropertyName("package")]
    public AdoptiumPackage? Package { get; set; }
}

internal sealed class AdoptiumPackage
{
    [JsonPropertyName("link")]
    public string? Link { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
