namespace PCL.Avalonia.Services.Mods;

public sealed record ModrinthFile
{
    public string Url { get; init; } = "";

    public string Filename { get; init; } = "";

    public bool Primary { get; init; }

    public long Size { get; init; }

    public string? Sha1 { get; init; }
}
