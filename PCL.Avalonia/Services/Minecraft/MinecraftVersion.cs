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
}
