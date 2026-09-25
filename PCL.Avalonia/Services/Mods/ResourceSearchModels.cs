namespace PCL.Avalonia.Services.Mods;

public sealed record ResourceProjectItem
{
    public required string ProjectId { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string AuthorText { get; init; }

    public required string DownloadsText { get; init; }

    public string CategoriesText { get; init; } = "";

    public required ResourceSource Source { get; init; }

    public required ResourceType Type { get; init; }
}

public sealed record ResourceFileItem
{
    public required ResourceSource Source { get; init; }

    public required string ProjectId { get; init; }

    public required string FileId { get; init; }

    public required string DisplayName { get; init; }

    public required string FileName { get; init; }

    public required string Url { get; init; }

    public long Size { get; init; }

    public string? Sha1 { get; init; }
}

public enum ResourceSource
{
    All,
    Modrinth,
    CurseForge,
}

public sealed record ResourceSearchRequest(
    ResourceType Type,
    string Query,
    string GameVersion,
    string Loader,
    string Tag,
    ResourceSource Source,
    int Page,
    int PageSize = 40);

public sealed record ResourceSearchResult(
    IReadOnlyList<ResourceProjectItem> Items,
    int TotalCount,
    string? ErrorMessage = null);

public sealed record ResourceTagOption(string Label, string Tag);

public sealed record ModrinthSearchPage(
    IReadOnlyList<ModrinthProject> Hits,
    int TotalHits);

public sealed record CurseForgeSearchPage(
    IReadOnlyList<CurseForgeProject> Projects,
    int TotalCount);
