using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Minecraft;

public interface IFabricLoaderService
{
    Task<IReadOnlyList<FabricLoaderVersion>> GetVersionsAsync(
        string gameVersion,
        CancellationToken cancellationToken = default);

    Task<FabricInstallResult> InstallAsync(
        string gameVersion,
        string loaderVersion,
        string minecraftFolder,
        DownloadSource source,
        IProgress<FabricInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record FabricLoaderVersion(
    string Version,
    string MinecraftVersion,
    bool IsStable,
    int MinJavaVersion);

public enum FabricInstallStage
{
    BaseVersion,
    ProfileJson,
    Libraries,
    Complete,
}

public sealed record FabricInstallProgress(
    FabricInstallStage Stage,
    string? ItemName,
    int CompletedItems,
    int TotalItems);

public sealed record FabricInstallResult(
    string VersionId,
    string GameVersion,
    string LoaderVersion,
    IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}
