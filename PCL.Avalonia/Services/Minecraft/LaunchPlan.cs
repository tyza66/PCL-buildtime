namespace PCL.Avalonia.Services.Minecraft;

public sealed record LaunchPlan
{
    public required string JavaExecutable { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string NativesDirectory { get; init; }

    public required string ClassPath { get; init; }

    public required string MainClass { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public required MinecraftVersion Version { get; init; }

    public string CommandLine => $"{JavaExecutable} {string.Join(" ", Arguments)}";
}
