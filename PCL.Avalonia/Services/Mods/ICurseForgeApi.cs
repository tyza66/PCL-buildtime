namespace PCL.Avalonia.Services.Mods;

public interface ICurseForgeApi
{
    Task<IReadOnlyList<CurseForgeProject>> SearchProjectsAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
        int projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);
}
