using System.Text.Json.Serialization;

namespace PCL.Avalonia.Services.Mods;

public sealed record ModrinthProject
{
    public string ProjectId { get; init; } = "";

    public string Slug { get; init; } = "";

    public string Title { get; init; } = "";

    public string Description { get; init; } = "";

    public string Author { get; init; } = "";

    public int Downloads { get; init; }

    public int Follows { get; init; }

    public List<string> Categories { get; init; } = [];

    [JsonPropertyName("updated")]
    public DateTimeOffset? UpdatedAt { get; init; }
}
