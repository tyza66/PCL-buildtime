namespace PCL.Avalonia.Services;

public sealed record AppSettings
{
    public bool UseDarkTheme { get; init; } = true;

    public DownloadSource DownloadSource { get; init; } = DownloadSource.Bmclapi;

    public string MinecraftFolder { get; init; } = "";

    public string JavaPath { get; init; } = "";

    public string UserName { get; init; } = "";

    public int MaxMemoryMb { get; init; } = 4096;
}
