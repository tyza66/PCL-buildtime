using PCL.Avalonia.Services;

namespace PCL.Avalonia.Services.Downloads;

public interface IVersionManifestService
{
    Task<VersionManifest> GetManifestAsync(
        DownloadSource source,
        CancellationToken cancellationToken = default);
}
