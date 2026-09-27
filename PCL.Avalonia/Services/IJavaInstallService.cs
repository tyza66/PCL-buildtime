namespace PCL.Avalonia.Services;

/// <summary>Adoptium 上一个适合本机下载的 JDK 发布。</summary>
public sealed record JavaRelease(
    string Version,
    int MajorVersion,
    string PackageUrl,
    long PackageSize,
    string Architecture,
    string OperatingSystem);

/// <summary>安装过程中的进度：下载按真实百分比，解压和收尾只报阶段。</summary>
public readonly record struct JavaInstallProgress(double Fraction, string StageText);

public interface IJavaInstallService
{
    /// <summary>查 Adoptium 上本机架构最新的指定大版本 JDK，没有合适包时抛中文异常。</summary>
    Task<JavaRelease> FetchLatestAsync(
        int majorVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 下载并解压指定大版本的 JDK 到启动器自己的目录，返回 java 可执行文件的完整路径。
    /// 同一版本已装过则跳过下载直接给路径，重复点按钮不会下一遍 180MB。
    /// </summary>
    Task<string> InstallAsync(
        int majorVersion,
        IProgress<JavaInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
