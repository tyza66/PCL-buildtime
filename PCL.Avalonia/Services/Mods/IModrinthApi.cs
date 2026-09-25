namespace PCL.Avalonia.Services.Mods;

public interface IModrinthApi
{
    Task<IReadOnlyList<ModrinthProject>> SearchProjectsAsync(
        string query,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModrinthProjectVersion>> GetVersionsAsync(
        string projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);
}
