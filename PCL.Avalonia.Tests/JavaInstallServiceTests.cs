using System.IO.Compression;
using System.Runtime.InteropServices;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Tests;

/// <summary>
/// 一键安装 Java 的服务层：从 Adoptium 查询、下载、解压、定位 java 可执行文件。
/// 下载和解压都走注入的替身，不碰网络也不真解 180MB。
/// </summary>
public sealed class JavaInstallServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pcl-java-install-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响用例结论。
        }
    }

    [Fact]
    public async Task FetchLatestAsync_ParsesAdoptiumPackage()
    {
        var client = new FakeDownloadClient(SampleJson);
        var service = CreateService(client, Architecture.Arm64);

        var release = await service.FetchLatestAsync(21);

        Assert.Equal(21, release.MajorVersion);
        Assert.Equal("jdk-21.0.5+11", release.Version);
        Assert.Contains(HotspotFileName(21, "jdk-21.0.5+11", "aarch64"), release.PackageUrl);
        Assert.Equal(123456, release.PackageSize);
        Assert.Contains("aarch64", client.RequestedUrls[0]);
        Assert.Contains($"os={OsToken}", client.RequestedUrls[0]);
    }

    [Fact]
    public async Task FetchLatestAsync_AsksForMachineArchitecture()
    {
        var client = new FakeDownloadClient(X86HostSampleJson);
        var service = CreateService(client, Architecture.X86);

        var release = await service.FetchLatestAsync(17);

        Assert.Contains("architecture=x86", client.RequestedUrls[0]);
        Assert.Equal(17, release.MajorVersion);
        Assert.Contains("x86", release.Architecture);
    }

    [Fact]
    public async Task FetchLatestAsync_ExplainsMissingPackage_InChinese()
    {
        var service = CreateService(new FakeDownloadClient("[]"), Architecture.Arm64);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchLatestAsync(99));

        Assert.Contains("没有提供适合本机", error.Message);
        Assert.Contains("adoptium.net", error.Message);
    }

    [Fact]
    public async Task FetchLatestAsync_ExplainsNetworkFailure_InChinese()
    {
        var service = CreateService(new ThrowingDownloadClient(), Architecture.Arm64);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchLatestAsync(21));

        Assert.Contains("检查网络", error.Message);
    }

    [Fact]
    public async Task InstallAsync_SkipsDownload_WhenSameVersionAlreadyInstalled()
    {
        var existing = Path.Combine(_root, "jdk-21.0.5+11", "bin", JavaExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        await File.WriteAllTextAsync(existing, "#!/bin/sh");

        var client = new FakeDownloadClient(SampleJson);
        var service = CreateService(client, Architecture.Arm64);
        var java = await service.InstallAsync(21);

        Assert.Equal(Path.GetFullPath(existing), Path.GetFullPath(java));
        Assert.Empty(client.DownloadedFiles);
    }

    [Fact]
    public async Task InstallAsync_ExtractsAndLocatesJava_WithProgress()
    {
        var client = new FakeDownloadClient(SampleJson);
        // Progress<T> 在无同步上下文时会把回调丢到线程池，断言时就拿不到，用同步替身。
        var stages = new List<string>();
        IProgress<JavaInstallProgress> progress = new SynchronousProgress(value => stages.Add(value.StageText));
        var extractorCalls = 0;
        var service = CreateService(client, Architecture.Arm64, extractor: (_, target) =>
        {
            extractorCalls++;
            // 真实 tar/zip 会多套一层 jdk-21.0.5+11 目录，替身照抄这个布局。
            var bin = Path.Combine(target, "jdk-21.0.5+11", "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, JavaExecutableName), "#!/bin/sh");
            return Task.CompletedTask;
        });

        var java = await service.InstallAsync(21, progress);

        Assert.EndsWith($"jdk-21.0.5+11/bin/{JavaExecutableName}", java.Replace('\\', '/'));
        Assert.True(File.Exists(java), "返回的 java 路径应真实存在");
        Assert.Equal(1, extractorCalls);
        Assert.Contains(stages, text => text.Contains("正在下载"));
        Assert.Contains(stages, text => text.Contains("正在解压"));
        Assert.Contains("安装完成", stages);
    }

    [Fact]
    public async Task InstallAsync_ExplainsExtractFailure_InChinese()
    {
        var service = CreateService(new FakeDownloadClient(SampleJson), Architecture.Arm64,
            extractor: (_, _) => throw new IOException("disk full"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(21));

        Assert.Contains("解压 Java 21 安装包失败", error.Message);
        Assert.Contains("磁盘剩余空间", error.Message);
    }

    [Fact]
    public void FindJavaExecutable_FindsNestedHome()
    {
        var nested = Path.Combine(_root, "jdk-17", "bin", JavaExecutableName);
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "#!/bin/sh");

        Assert.Equal(Path.GetFullPath(nested), Path.GetFullPath(JavaInstallService.FindJavaExecutable(_root)!));
        Assert.Null(JavaInstallService.FindJavaExecutable(Path.Combine(_root, "missing")));
    }

    [Fact]
    public async Task FetchLatestAsync_FallbackQuery_KeepsOsAndArchitecture()
    {
        // 兜底查询只去掉厂商限制：os 和架构必须留住，少了它们接口会按默认系统返回别的包。
        var client = new PrimaryFailsDownloadClient(SampleJson);
        var service = CreateService(client, Architecture.Arm64);

        var release = await service.FetchLatestAsync(21);

        Assert.Equal(21, release.MajorVersion);
        var fallback = client.RequestedUrls[1];
        Assert.Contains($"os={OsToken}", fallback);
        Assert.Contains("architecture=aarch64", fallback);
        Assert.DoesNotContain("vendor=eclipse", fallback);
    }

    [Fact]
    public async Task FetchLatestAsync_SkipsAssetsForOtherPlatforms()
    {
        var service = CreateService(new FakeDownloadClient(MixedPlatformJson), Architecture.Arm64);

        var release = await service.FetchLatestAsync(21);

        Assert.Contains($"aarch64_{OsToken}", release.PackageUrl);
    }

    [Fact]
    public async Task FetchLatestAsync_RejectsAssetsForWrongPlatform()
    {
        var service = CreateService(new FakeDownloadClient(WindowsOnlyJson), Architecture.Arm64);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchLatestAsync(21));

        Assert.Contains("没有提供适合本机", error.Message);
    }

    [Fact]
    public async Task InstallAsync_PrefersTsinghuaMirror_WhenDownloadSourceIsMirror()
    {
        // 默认下载源是国内镜像：清华源排在 GitHub 前面，国内用户不用盯着几十 KB/s 干等。
        var client = new FakeDownloadClient(SampleJson);
        var service = CreateService(client, Architecture.Arm64, downloadSource: DownloadSource.Bmclapi);

        await service.InstallAsync(21);

        var request = client.DownloadedRequests[0];
        Assert.Equal(
            $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/21/jdk/aarch64/{OsToken}/"
            + HotspotFileName(21, "jdk-21.0.5+11", "aarch64"),
            request.Urls[0]);
        Assert.Contains(request.Urls, url => url.StartsWith("https://github.com/adoptium/"));
        // 接口给了包大小就校验：下到一半被截断的 180MB 比直接失败更难查。
        Assert.Equal(123456, request.ExpectedSize);
    }

    [Fact]
    public async Task InstallAsync_KeepsMirrorAsFallback_WhenDownloadSourceIsOfficial()
    {
        var client = new FakeDownloadClient(SampleJson);
        var service = CreateService(client, Architecture.Arm64, downloadSource: DownloadSource.Mojang);

        await service.InstallAsync(21);

        var urls = client.DownloadedRequests[0].Urls;
        Assert.StartsWith("https://github.com/adoptium/", urls[0]);
        Assert.Contains(urls, url => url.StartsWith("https://mirrors.tuna.tsinghua.edu.cn/"));
    }

    [Fact]
    public async Task InstallAsync_WithPublicConstructor_UnpacksUnderLauncherJavaFolder()
    {
        // 公共构造函数 + 真 zip + 真解压：安装根、下载源、解压、定位 java 一条龙走一遍。
        var configDirectory = Path.Combine(_root, "config");
        var fixture = Path.Combine(_root, "fixture");
        Directory.CreateDirectory(fixture);
        ZipFile.CreateFromDirectory(CreateFakeJdkHome(fixture), Path.Combine(_root, "fixture.zip"));
        var client = new FileCopyDownloadClient(ZipSampleJson, Path.Combine(_root, "fixture.zip"));
        var service = new JavaInstallService(
            client,
            new StubPlatformService(configDirectory),
            new StubSettingsService(new AppSettings { DownloadSource = DownloadSource.Bmclapi }));

        try
        {
            var java = await service.InstallAsync(21);

            // 装的位置必须就是 Java 列表扫描的根目录，否则设置页列表里看不到刚装的 Java。
            Assert.StartsWith(Path.Combine(configDirectory, "java"), java);
            Assert.True(File.Exists(java), "返回的 java 路径应真实存在");
            Assert.Contains("jdk-21.0.5+11", java.Replace('\\', '/'));
            Assert.Contains("mirrors.tuna.tsinghua.edu.cn", client.DownloadedRequests[0].Urls[0]);
        }
        finally
        {
            try
            {
                Directory.Delete(configDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // 安装中途失败时目录可能压根没建出来，清理失败不该盖住用例结论。
            }
        }
    }

    private JavaInstallService CreateService(
        IDownloadClient client,
        Architecture architecture,
        Func<string, string, Task>? extractor = null,
        DownloadSource downloadSource = DownloadSource.Mojang)
        => new(
            client,
            () => _root,
            () => architecture,
            extractor ?? ((_, target) =>
            {
                // 默认替身照抄真实 tar/zip 解压后的布局：多套一层 jdk-21.0.5+11 目录，
                // 这样安装流程能一路走完，镜像顺序用例断言的就是最终发出的下载请求。
                var bin = Path.Combine(target, "jdk-21.0.5+11", "bin");
                Directory.CreateDirectory(bin);
                File.WriteAllText(Path.Combine(bin, JavaExecutableName), "#!/bin/sh");
                return Task.CompletedTask;
            }),
            () => downloadSource);

    private static string CreateFakeJdkHome(string root)
    {
        var home = Path.Combine(root, "jdk-21.0.5+11");
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", JavaExecutableName), "#!/bin/sh\n");
        return home;
    }

    // 夹具跟着运行平台走：服务按本机 os/架构过滤条目，写死 mac 在 Windows/Linux runner 上必然不匹配。
    private static string OsToken
        => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "mac";

    private static string JavaExecutableName => OperatingSystem.IsWindows() ? "java.exe" : "java";

    // 公共构造函数不带替身架构，走真实 RuntimeInformation，夹具必须按本机架构生成。
    private static string HostArchToken => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "aarch64",
        Architecture.X86 => "x86",
        _ => "x64",
    };

    /// <summary>Adoptium 热点包文件名，os 片段用本机的：windows / mac / linux。</summary>
    private static string HotspotFileName(int major, string release, string arch, string extension = "tar.gz")
        => $"OpenJDK{major}U-jdk_{arch}_{OsToken}_hotspot_{release.Replace("jdk-", "").Replace('+', '_')}.{extension}";

    private static string AssetJson(int major, string release, string os, string arch, long size, string extension = "tar.gz")
        => $$"""
        {
          "release_name": "{{release}}",
          "version": { "major": {{major}} },
          "binary": {
            "os": "{{os}}",
            "architecture": "{{arch}}",
            "image_type": "jdk",
            "package": {
              "name": "{{HotspotFileName(major, release, arch, extension)}}",
              "link": "https://github.com/adoptium/temurin{{major}}-binaries/releases/download/{{Uri.EscapeDataString(release)}}/{{HotspotFileName(major, release, arch, extension)}}",
              "size": {{size}}
            }
          }
        }
        """;

    private static string JsonAssets(params string[] assets)
        => "[" + string.Join(",", assets) + "]";

    private static string SampleJson => JsonAssets(AssetJson(21, "jdk-21.0.5+11", OsToken, "aarch64", 123456));

    private static string MixedPlatformJson => JsonAssets(
        AssetJson(21, "jdk-21.0.5+11", "windows", "x64", 1),
        AssetJson(21, "jdk-21.0.5+11", OsToken, "aarch64", 2));

    private static string WindowsOnlyJson => JsonAssets(AssetJson(21, "jdk-21.0.5+11", "windows", "x64", 1));

    private static string ZipSampleJson => JsonAssets(AssetJson(21, "jdk-21.0.5+11", OsToken, HostArchToken, 3, "zip"));

    private static string X86HostSampleJson => JsonAssets(AssetJson(17, "jdk-17.0.13+11", OsToken, "x86", 999));

    private sealed class FakeDownloadClient : IDownloadClient
    {
        private readonly string _json;

        public FakeDownloadClient(string json) => _json = json;

        public List<string> RequestedUrls { get; } = [];

        public List<string> DownloadedFiles { get; } = [];

        public List<DownloadRequest> DownloadedRequests { get; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadedFiles.Add(request.DestinationPath);
            DownloadedRequests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
            File.WriteAllText(request.DestinationPath, "fake package");
            progress?.Report(new DownloadProgress(100, 100));
            return Task.CompletedTask;
        }

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            RequestedUrls.Add(urls[0]);
            return Task.FromResult(_json);
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => Task.FromResult("{}");
    }

    /// <summary>
    /// 直接在同线程执行进度回调。框架自带的 <see cref="Progress{T}"/> 在没有同步上下文时把回调
    /// 丢到线程池，用例断言时可能一次都没跑完，阶段文本就收不齐。
    /// </summary>
    private sealed class SynchronousProgress(Action<JavaInstallProgress> handler) : IProgress<JavaInstallProgress>
    {
        public void Report(JavaInstallProgress value) => handler(value);
    }

    private sealed class ThrowingDownloadClient : IDownloadClient
    {
        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("boom");

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("boom");

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => throw new HttpRequestException("boom");
    }

    /// <summary>主查询失败、兜底查询成功：验证兜底 URL 里 os 和架构没有丢。</summary>
    private sealed class PrimaryFailsDownloadClient(string json) : IDownloadClient
    {
        public List<string> RequestedUrls { get; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
        {
            // 照抄真客户端的契约：地址按顺序逐个试，第一个成功的返回，另一个记进请求历史。
            foreach (var url in urls)
            {
                RequestedUrls.Add(url);
                if (!url.Contains("vendor=eclipse", StringComparison.Ordinal))
                {
                    return Task.FromResult(json);
                }
            }

            throw new HttpRequestException("primary source down");
        }

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => Task.FromResult("{}");
    }

    /// <summary>把预先做好的安装包文件写到目标路径，让真解压路径跑起来。</summary>
    private sealed class FileCopyDownloadClient(string json, string sourceFile) : IDownloadClient
    {
        public List<DownloadRequest> DownloadedRequests { get; } = [];

        public Task DownloadAsync(
            DownloadRequest request,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            DownloadedRequests.Add(request);
            var directory = Path.GetDirectoryName(request.DestinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourceFile, request.DestinationPath, overwrite: true);
            return Task.CompletedTask;
        }

        public Task<string> GetStringAsync(
            IReadOnlyList<string> urls,
            CancellationToken cancellationToken = default)
            => Task.FromResult(json);

        public Task<string> PostJsonAsync(
            IReadOnlyList<string> urls,
            string json,
            CancellationToken cancellationToken = default)
            => Task.FromResult("{}");
    }

    private sealed class StubPlatformService(string configDirectory) : IPlatformService
    {
        public string GetConfigDirectory() => configDirectory;

        public string GetDefaultMinecraftFolder() => configDirectory;
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Load() => settings;

        public void Save(AppSettings settings)
        {
        }
    }
}
