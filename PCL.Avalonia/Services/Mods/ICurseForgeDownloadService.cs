using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public interface ICurseForgeDownloadService
{
    Task<string> InstallAsync(
        CurseForgeModFile file,
        string modsFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
