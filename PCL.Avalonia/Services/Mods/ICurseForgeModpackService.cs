using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public interface ICurseForgeModpackService
{
    Task<IReadOnlyList<CurseForgeProject>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<string> InstallAsync(
        CurseForgeProject project,
        string gameVersion,
        string minecraftFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
