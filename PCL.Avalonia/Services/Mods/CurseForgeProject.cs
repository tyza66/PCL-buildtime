namespace PCL.Avalonia.Services.Mods;

public sealed class CurseForgeProject
{
    public int Id { get; init; }

    public string Slug { get; init; } = "";

    public string Name { get; init; } = "";

    public string Summary { get; init; } = "";

    public string? LogoUrl { get; init; }

    public List<CurseForgeAuthor> Authors { get; init; } = [];

    public int DownloadCount { get; init; }

    public List<string> Categories { get; init; } = [];
}

public sealed class CurseForgeAuthor
{
    public string Name { get; init; } = "";
}
