namespace PCL.Avalonia.Services.Mods;

public sealed record ModInfo
{
    public required string FileName { get; init; }

    public required string DisplayName { get; init; }

    public required string FilePath { get; init; }

    public bool IsEnabled { get; init; } = true;

    public long SizeBytes { get; init; }

    public DateTimeOffset LastModifiedUtc { get; init; }
}
