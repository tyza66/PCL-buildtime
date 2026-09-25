using System.Text.Json;
using PCL.Avalonia.Services;

namespace PCL.Avalonia.Services.Downloads;

public sealed class VersionManifestService : IVersionManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDownloadClient _downloadClient;
    private readonly DownloadUrlResolver _urlResolver;

    public VersionManifestService(
        IDownloadClient downloadClient,
        DownloadUrlResolver? urlResolver = null)
    {
        _downloadClient = downloadClient;
        _urlResolver = urlResolver ?? new DownloadUrlResolver();
    }

    public async Task<VersionManifest> GetManifestAsync(
        DownloadSource source,
        CancellationToken cancellationToken = default)
    {
        var urls = _urlResolver.GetVersionManifestUrls(source);
        var json = await _downloadClient
            .GetStringAsync(urls, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions)
               ?? new VersionManifest();
    }
}
