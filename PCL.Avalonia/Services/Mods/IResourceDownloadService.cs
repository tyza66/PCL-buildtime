using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public interface IResourceDownloadService
{
    Task<string> InstallAsync(
        ResourceType type,
        ResourceFileItem file,
        string minecraftFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
