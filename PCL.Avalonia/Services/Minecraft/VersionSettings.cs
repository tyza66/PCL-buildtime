namespace PCL.Avalonia.Services.Minecraft;

public sealed record VersionSettings
{
    public bool IsFavorite { get; init; }

    public bool IsHidden { get; init; }

    public string Description { get; init; } = "";
}
