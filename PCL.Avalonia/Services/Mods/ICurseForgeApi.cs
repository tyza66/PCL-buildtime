namespace PCL.Avalonia.Services.Mods;

public interface ICurseForgeApi
{
    Task<IReadOnlyList<CurseForgeProject>> SearchProjectsAsync(
        string query,
        int classId = 6,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
        int projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurseForgeModFile>> GetModpackFilesAsync(
        int projectId,
        string gameVersion,
        CancellationToken cancellationToken = default);

    Task<CurseForgeModFile?> GetFileAsync(
        int projectId,
        int fileId,
        CancellationToken cancellationToken = default);
}
