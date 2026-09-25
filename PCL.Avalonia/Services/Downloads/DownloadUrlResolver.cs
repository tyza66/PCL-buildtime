using PCL.Avalonia.Services;

namespace PCL.Avalonia.Services.Downloads;

public sealed class DownloadUrlResolver
{
    public const string MojangManifestUrl =
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public const string BmclapiManifestUrl =
        "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";

    public const string BmclapiRoot = "https://bmclapi2.bangbang93.com";

    public const string MojangLibrariesRoot = "https://libraries.minecraft.net";

    public IReadOnlyList<string> GetVersionManifestUrls(DownloadSource source)
    {
        return source == DownloadSource.Bmclapi
            ? [BmclapiManifestUrl, MojangManifestUrl]
            : [MojangManifestUrl];
    }

    public IReadOnlyList<string> GetVersionJsonUrls(
        DownloadSource source,
        VersionManifestEntry? entry,
        string versionId)
    {
        if (source == DownloadSource.Bmclapi)
        {
            var mirror = $"{BmclapiRoot}/version/{Escape(versionId)}/json";
            return string.IsNullOrWhiteSpace(entry?.Url)
                ? [mirror]
                : [mirror, entry!.Url!];
        }

        if (string.IsNullOrWhiteSpace(entry?.Url))
        {
            throw new InvalidOperationException($"版本清单缺少版本 {versionId} 的 JSON 地址");
        }

        return [entry!.Url!];
    }

    public IReadOnlyList<string> GetClientJarUrls(
        DownloadSource source,
        string versionId,
        string? originalUrl)
    {
        if (source == DownloadSource.Bmclapi)
        {
            var mirror = $"{BmclapiRoot}/version/{Escape(versionId)}/jar";
            return string.IsNullOrWhiteSpace(originalUrl)
                ? [mirror]
                : [mirror, originalUrl];
        }

        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            throw new InvalidOperationException($"版本 {versionId} 缺少客户端 jar 下载地址");
        }

        return [originalUrl];
    }

    public IReadOnlyList<string> GetLibraryUrls(
        DownloadSource source,
        string? originalUrl,
        string libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            throw new ArgumentException("支持库路径不能为空", nameof(libraryPath));
        }

        if (source == DownloadSource.Bmclapi)
        {
            var mirror = $"{BmclapiRoot}/libraries/{libraryPath}";
            return string.IsNullOrWhiteSpace(originalUrl)
                ? [mirror]
                : [mirror, originalUrl];
        }

        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            return [$"{MojangLibrariesRoot}/{libraryPath}"];
        }

        return [originalUrl];
    }

    public IReadOnlyList<string> GetAssetIndexUrls(
        DownloadSource source,
        string? originalUrl,
        string? sha1)
    {
        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            throw new InvalidOperationException("版本 JSON 缺少资源索引下载地址");
        }

        return [originalUrl];
    }

    public IReadOnlyList<string> GetAssetUrls(DownloadSource source, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length < 2)
        {
            throw new ArgumentException("资源哈希无效", nameof(hash));
        }

        var prefix = hash[..2];
        var official = $"https://resources.download.minecraft.net/{prefix}/{hash}";
        if (source == DownloadSource.Bmclapi)
        {
            return [$"{BmclapiRoot}/assets/{prefix}/{hash}", official];
        }

        return [official];
    }

    private static string Escape(string value)
    {
        return Uri.EscapeDataString(value);
    }
}
