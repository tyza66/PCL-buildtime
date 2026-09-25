using System.Text.Json.Serialization;

namespace PCL.Avalonia.Services.Mods;

public sealed class ModpackManifest
{
    public string Name { get; set; } = "";

    public string Author { get; set; } = "";

    public string Version { get; set; } = "";

    [JsonPropertyName("minecraft")]
    public ModpackMinecraft Minecraft { get; set; } = new();

    [JsonPropertyName("files")]
    public List<ModpackManifestFile> Files { get; set; } = [];

    [JsonPropertyName("overrides")]
    public string Overrides { get; set; } = "overrides";
}

public sealed class ModpackMinecraft
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("modLoaders")]
    public List<ModpackModLoader> ModLoaders { get; set; } = [];
}

public sealed class ModpackModLoader
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

public sealed class ModpackManifestFile
{
    [JsonPropertyName("projectID")]
    public int ProjectId { get; set; }

    [JsonPropertyName("fileID")]
    public int FileId { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;
}
