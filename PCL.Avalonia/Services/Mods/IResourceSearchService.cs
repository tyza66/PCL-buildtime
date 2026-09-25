namespace PCL.Avalonia.Services.Mods;

public interface IResourceSearchService
{
    Task<ResourceSearchResult> SearchAsync(
        ResourceType type,
        string query,
        string gameVersion,
        string loader,
        string tag,
        ResourceSource source,
        int page = 0,
        CancellationToken cancellationToken = default);
}
