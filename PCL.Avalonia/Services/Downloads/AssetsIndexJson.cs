using System.Text.Json.Serialization;

namespace PCL.Avalonia.Services.Downloads;

public sealed class AssetsIndexJson
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObjectJson> Objects { get; set; } = [];
}

public sealed class AssetObjectJson
{
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}
