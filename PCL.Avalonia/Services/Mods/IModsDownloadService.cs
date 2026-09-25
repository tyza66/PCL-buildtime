using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public interface IModsDownloadService
{
    Task<string> InstallAsync(
        ModrinthProjectVersion version,
        string modsFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
