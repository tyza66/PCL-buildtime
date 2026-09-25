namespace PCL.Avalonia.Services.Mods;

public interface IModrinthApi
{
    Task<ModrinthSearchPage> SearchProjectsAsync(
        string query,
        string gameVersion,
        string loader,
        string projectType = "mod",
        int offset = 0,
        int limit = 40,
        string tag = "",
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModrinthProjectVersion>> GetVersionsAsync(
        string projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default);
}
