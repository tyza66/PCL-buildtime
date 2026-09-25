using System.Text.Json;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public sealed class CurseForgeApi : ICurseForgeApi
{
    private const string BaseUrl = "https://api.curseforge.com";
    private const int MinecraftGameId = 432;
    private const int ModClassId = 6;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Dictionary<string, int> LoaderTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["forge"] = 1,
        ["fabric"] = 4,
        ["quilt"] = 5,
        ["neoforge"] = 6,
        ["paper"] = 8,
        ["spigot"] = 9,
    };

    private readonly IDownloadClient _downloadClient;

    public CurseForgeApi(IDownloadClient downloadClient)
    {
        _downloadClient = downloadClient;
    }

    public async Task<IReadOnlyList<CurseForgeProject>> SearchProjectsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var search = new SearchRequest
        {
            GameId = MinecraftGameId,
            ClassId = ModClassId,
            SearchFilter = query.Trim(),
            PageSize = 30,
            SortField = 6,
        };
        var body = JsonSerializer.Serialize(search, JsonOptions);
        var json = await _downloadClient
            .PostJsonAsync([$"{BaseUrl}/v1/mods/search"], body, cancellationToken)
            .ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<Envelope<List<CurseForgeProject>>>(json, JsonOptions);
        return envelope?.Data ?? [];
    }

    public async Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
        int projectId,
        string gameVersion,
        string loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        var query = new List<string> { $"gameVersion={Uri.EscapeDataString(gameVersion.Trim())}" };
        var loaderType = MapLoaderType(loader);
        if (loaderType is not null)
        {
            query.Add($"modLoaderType={loaderType}");
        }

        var url = $"{BaseUrl}/v1/mods/{projectId}/files?{string.Join('&', query)}";
        var json = await _downloadClient.GetStringAsync([url], cancellationToken).ConfigureAwait(false);
        var envelope = JsonSerializer.Deserialize<Envelope<List<CurseForgeModFile>>>(json, JsonOptions);
        return envelope?.Data ?? [];
    }

    private static int? MapLoaderType(string loader)
    {
        if (string.IsNullOrWhiteSpace(loader))
        {
            return null;
        }

        return LoaderTypeMap.TryGetValue(loader.Trim(), out var value) ? value : null;
    }

    private sealed record Envelope<T>
    {
        public T Data { get; init; } = default!;
    }

    private sealed class SearchRequest
    {
        public int GameId { get; init; }

        public int ClassId { get; init; }

        public string SearchFilter { get; init; } = "";

        public int PageSize { get; init; }

        public int SortField { get; init; }
    }
}
