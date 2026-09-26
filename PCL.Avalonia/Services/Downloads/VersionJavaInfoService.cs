using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services.Downloads;

/// <summary>
/// 从版本 JSON 里读 javaVersion.majorVersion。同一个版本在用户来回切换选择时会问很多次，
/// 用内存字典挡掉重复请求；失败不缓存，下一次选择还能重试。
/// </summary>
public sealed class VersionJavaInfoService : IVersionJavaInfoService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDownloadClient _downloadClient;
    private readonly DownloadUrlResolver _urlResolver;
    private readonly Dictionary<string, int?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public VersionJavaInfoService(
        IDownloadClient downloadClient,
        DownloadUrlResolver? urlResolver = null)
    {
        _downloadClient = downloadClient;
        _urlResolver = urlResolver ?? new DownloadUrlResolver();
    }

    public async Task<int?> GetRequiredJavaMajorAsync(
        DownloadSource source,
        VersionManifestEntry? entry,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return null;
        }

        if (_cache.TryGetValue(versionId, out var cached))
        {
            return cached;
        }

        int? major = null;
        try
        {
            var urls = _urlResolver.GetVersionJsonUrls(source, entry, versionId);
            var json = await _downloadClient.GetStringAsync(urls, cancellationToken).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<MinecraftVersionJson>(json, JsonOptions);
            major = parsed?.JavaVersion?.MajorVersion;
            _cache[versionId] = major;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 读不到 Java 要求不该拦住下载：留 null 让界面提示"未提供"，失败也不缓存以便重试。
        }

        return major;
    }
}
