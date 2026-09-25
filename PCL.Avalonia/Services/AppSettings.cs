namespace PCL.Avalonia.Services;

public sealed record AppSettings
{
    public bool UseDarkTheme { get; init; } = true;

    public DownloadSource DownloadSource { get; init; } = DownloadSource.Bmclapi;

    public string MinecraftFolder { get; init; } = "";

    public string JavaPath { get; init; } = "";

    public string UserName { get; init; } = "";

    public int MaxMemoryMb { get; init; } = 4096;

    public string JvmArguments { get; init; } = "";

    public string GameArguments { get; init; } = "";

    public int DownloadThreads { get; init; } = 64;

    public int DownloadSpeedLimitKbps { get; init; }

    public bool OptimizeMemoryBeforeLaunch { get; init; } = true;

    public LinkLatencyMode LinkLatencyMode { get; init; } = LinkLatencyMode.PreferredDirect;

    public string LinkCustomPeer { get; init; } = "";
}
