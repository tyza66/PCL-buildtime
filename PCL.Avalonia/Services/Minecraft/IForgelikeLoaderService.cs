using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Minecraft;

public interface IForgelikeLoaderService
{
    Task<IReadOnlyList<ForgelikeLoaderVersion>> GetForgeVersionsAsync(
        string gameVersion,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ForgelikeLoaderVersion>> GetNeoForgeVersionsAsync(
        CancellationToken cancellationToken = default);

    Task<ForgelikeInstallResult> InstallAsync(
        ForgelikeLoaderVersion version,
        string minecraftFolder,
        DownloadSource source,
        IProgress<ForgelikeInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public enum ForgelikeKind
{
    Forge,
    NeoForge,
}

public sealed record ForgelikeLoaderVersion(
    ForgelikeKind Kind,
    string VersionName,
    string GameVersion,
    bool IsBeta,
    Version SortVersion,
    string? FileVersion = null,
    string? Category = null,
    string? Hash = null,
    string? ApiName = null);

public enum ForgelikeInstallStage
{
    BaseVersion,
    Installer,
    Libraries,
    Injector,
    Complete,
}

public sealed record ForgelikeInstallProgress(
    ForgelikeInstallStage Stage,
    string? ItemName,
    int CompletedItems,
    int TotalItems);

public sealed record ForgelikeInstallResult(
    string VersionId,
    string GameVersion,
    string LoaderVersion,
    IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
