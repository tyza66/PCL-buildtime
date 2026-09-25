using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services.Downloads;

public interface IVersionInstaller
{
    Task<VersionInstallResult> InstallAsync(
        string versionId,
        VersionManifestEntry? entry,
        DownloadSource source,
        string minecraftFolder,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public enum InstallStage
{
    VersionJson,
    VersionJar,
    Libraries,
    AssetsIndex,
    Assets,
    Complete,
}

public sealed record InstallProgress(
    InstallStage Stage,
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

public sealed record VersionInstallResult(string VersionId, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
