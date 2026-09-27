namespace PCL.Avalonia.Services.Minecraft;

public sealed record VersionSettings
{
    public bool IsFavorite { get; init; }

    public bool IsHidden { get; init; }

    public string Description { get; init; } = "";

    public InstanceDisplayType DisplayType { get; init; } = InstanceDisplayType.Auto;

    public int? MaxMemoryMb { get; init; }

    public string? JavaPath { get; init; }

    public string? JvmArguments { get; init; }

    public string? GameArguments { get; init; }

    /// <summary>版本隔离：null 为未决定（按全局默认与 mods/saves 自动判断），true/false 为手动开关。</summary>
    public bool? Independent { get; init; }
}

public enum InstanceDisplayType
{
    Star = -1,
    Auto = 0,
    Hidden = 1,
    Api = 2,
    Original = 3,
    Rubbish = 4,
    Fool = 5,
}
