namespace PCL.Avalonia.Services.Minecraft;

public sealed record MinecraftVersion
{
    public required string Id { get; init; }

    public required string Folder { get; init; }

    public required string JsonPath { get; init; }

    public string Type { get; init; } = "release";

    public DateTimeOffset ReleaseTime { get; init; } = DateTimeOffset.UnixEpoch;

    public string? InheritsFrom { get; init; }

    public string? MainClass { get; init; }

    public string? Assets { get; init; }

    public string? AssetIndexId { get; init; }

    public string? Jar { get; init; }

    public string ReleaseTimeText => ReleaseTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string? VanillaName { get; init; }

    public bool Reliable { get; init; } = true;

    public int Drop { get; init; }

    public InstanceState State { get; init; } = InstanceState.Original;

    public LoaderKind Loader { get; init; } = LoaderKind.None;

    public string? LoaderVersion { get; init; }

    public string? RawJson { get; init; }
}

public enum InstanceState
{
    Error,
    Original,
    Snapshot,
    Fool,
    OptiFine,
    Old,
    Forge,
    NeoForge,
    LiteLoader,
    Fabric,
}

public enum LoaderKind
{
    None,
    OptiFine,
    Forge,
    NeoForge,
    Fabric,
    LiteLoader,
}
