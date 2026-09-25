using System.Text.Json.Serialization;

namespace PCL.Avalonia.Services.Downloads;

public sealed class VersionManifest
{
    [JsonPropertyName("latest")]
    public VersionManifestLatestJson? Latest { get; set; }

    [JsonPropertyName("versions")]
    public List<VersionManifestEntry> Versions { get; set; } = [];
}

public sealed class VersionManifestLatestJson
{
    [JsonPropertyName("release")]
    public string? Release { get; set; }

    [JsonPropertyName("snapshot")]
    public string? Snapshot { get; set; }
}

public sealed class VersionManifestEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset? ReleaseTime { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }
}
