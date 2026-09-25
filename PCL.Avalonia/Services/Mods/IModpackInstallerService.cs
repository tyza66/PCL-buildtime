using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public interface IModpackInstallerService
{
    Task<ModpackInstallResult> InstallAsync(
        string modpackZipPath,
        string minecraftFolder,
        DownloadSource source,
        IProgress<ModpackInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public enum ModpackInstallStage
{
    ReadingManifest,
    BaseVersion,
    Mods,
    Overrides,
    Complete,
}

public sealed record ModpackInstallProgress(
    ModpackInstallStage Stage,
    string? ItemName,
    int CompletedItems,
    int TotalItems,
    long Received,
    long? TotalLength)
{
    public double DownloadFraction => TotalLength is > 0
        ? Math.Min(1, (double)Received / TotalLength.Value)
        : 0;
}

public sealed record ModpackInstallResult(
    string Name,
    string MinecraftVersion,
    IReadOnlyList<string> InstalledMods,
    IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
