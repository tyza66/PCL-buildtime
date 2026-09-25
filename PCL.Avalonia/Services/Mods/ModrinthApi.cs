using System.Text.Json;
using System.Text.Json.Serialization;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public sealed class ModrinthApi : IModrinthApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
    private const string BaseUrl = "https://api.modrinth.com/v2";
    private readonly IDownloadClient _downloadClient;

    public ModrinthApi(IDownloadClient downloadClient)
    {
        _downloadClient = downloadClient;
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchProjectsAsync(
        string query,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        var facets = new List<string[]>
        {
            new[] { $"versions:{gameVersion}" },
        };
        if (!string.IsNullOrWhiteSpace(loader))
        {
            facets.Add(new[] { $"categories:{loader}" });
        }

        var queryPart = Uri.EscapeDataString(query.Trim());
        var facetsPart = Uri.EscapeDataString(JsonSerializer.Serialize(facets));
        var url = $"{BaseUrl}/search?query={queryPart}&limit=30&facets={facetsPart}";
        var json = await _downloadClient.GetStringAsync([url], cancellationToken).ConfigureAwait(false);
        var response = JsonSerializer.Deserialize<SearchResponse>(json, JsonOptions);
        return response?.Hits ?? [];
    }

    public async Task<IReadOnlyList<ModrinthProjectVersion>> GetVersionsAsync(
        string projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        var gameVersionsPart = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }));
        var loadersPart = string.IsNullOrWhiteSpace(loader)
            ? ""
            : $"&loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}";
        var url = $"{BaseUrl}/project/{projectId}/version?game_versions={gameVersionsPart}{loadersPart}";
        var json = await _downloadClient.GetStringAsync([url], cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<ModrinthProjectVersion>>(json, JsonOptions) ?? [];
    }

    private sealed record SearchResponse
    {
        public List<ModrinthProject> Hits { get; init; } = [];
    }
}
