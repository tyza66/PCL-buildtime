using System.Text.Json;
using System.Text.Json.Serialization;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class MinecraftVersionJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset? ReleaseTime { get; set; }

    [JsonPropertyName("mainClass")]
    public string? MainClass { get; set; }

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("jar")]
    public string? Jar { get; set; }

    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }

    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    [JsonPropertyName("patches")]
    public List<VersionPatchJson> Patches { get; set; } = [];

    [JsonPropertyName("assetIndex")]
    public AssetIndexJson? AssetIndex { get; set; }

    [JsonPropertyName("arguments")]
    public ArgumentsJson? Arguments { get; set; }

    [JsonPropertyName("libraries")]
    public List<LibraryJson> Libraries { get; set; } = [];

    [JsonPropertyName("downloads")]
    public VersionDownloadsJson? Downloads { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersionJson? JavaVersion { get; set; }
}

public sealed class JavaVersionJson
{
    [JsonPropertyName("component")]
    public string? Component { get; set; }

    [JsonPropertyName("majorVersion")]
    public int? MajorVersion { get; set; }
}

public sealed class VersionPatchJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

public sealed class AssetIndexJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

public sealed class ArgumentsJson
{
    [JsonPropertyName("jvm")]
    public List<JsonElement> Jvm { get; set; } = [];

    [JsonPropertyName("game")]
    public List<JsonElement> Game { get; set; } = [];
}

public sealed class ArgumentElementJson
{
    [JsonPropertyName("rules")]
    public List<RuleJson>? Rules { get; set; }

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

public sealed class LibraryJson
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("rules")]
    public List<RuleJson>? Rules { get; set; }

    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("downloads")]
    public LibraryDownloadsJson? Downloads { get; set; }

    [JsonPropertyName("extract")]
    public LibraryExtractJson? Extract { get; set; }
}

public sealed class LibraryDownloadsJson
{
    [JsonPropertyName("artifact")]
    public ArtifactJson? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, ArtifactJson>? Classifiers { get; set; }
}

public sealed class ArtifactJson
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

public sealed class VersionDownloadsJson
{
    [JsonPropertyName("client")]
    public ArtifactJson? Client { get; set; }

    [JsonPropertyName("server")]
    public ArtifactJson? Server { get; set; }

    [JsonPropertyName("client_mappings")]
    public ArtifactJson? ClientMappings { get; set; }

    [JsonPropertyName("server_mappings")]
    public ArtifactJson? ServerMappings { get; set; }
}

public sealed class LibraryExtractJson
{
    [JsonPropertyName("exclude")]
    public List<string> Exclude { get; set; } = [];
}

public sealed class RuleJson
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("os")]
    public RuleOsJson? Os { get; set; }

    [JsonPropertyName("features")]
    public Dictionary<string, bool>? Features { get; set; }
}

public sealed class RuleOsJson
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arch")]
    public string? Arch { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
