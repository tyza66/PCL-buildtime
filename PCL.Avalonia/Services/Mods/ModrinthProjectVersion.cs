namespace PCL.Avalonia.Services.Mods;

public sealed record ModrinthProjectVersion
{
    public string Id { get; init; } = "";

    public string ProjectId { get; init; } = "";

    public string Name { get; init; } = "";

    public string VersionNumber { get; init; } = "";

    public string Changelog { get; init; } = "";

    public DateTimeOffset? DatePublished { get; init; }

    public List<string> GameVersions { get; init; } = [];

    public List<string> Loaders { get; init; } = [];

    public List<ModrinthFile> Files { get; init; } = [];
}
