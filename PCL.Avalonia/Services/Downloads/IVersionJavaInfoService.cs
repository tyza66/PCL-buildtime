namespace PCL.Avalonia.Services.Downloads;

public interface IVersionJavaInfoService
{
    /// <summary>
    /// 读取版本清单里某个版本要求的 Java 大版本号。版本 JSON 的 javaVersion.majorVersion 才是权威值，
    /// 下载页靠它提前告诉用户"这个版本要 Java 21"，别等装完启动才发现 Java 版本不对。
    /// 返回 null 表示清单没给这个信息（老版本 JSON 里就没有），调用方按"未知"处理。
    /// </summary>
    Task<int?> GetRequiredJavaMajorAsync(
        DownloadSource source,
        VersionManifestEntry? entry,
        string versionId,
        CancellationToken cancellationToken = default);
}
