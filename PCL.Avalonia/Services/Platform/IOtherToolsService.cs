namespace PCL.Avalonia.Services.Platform;

public interface IOtherToolsService
{
    OtherEnvironmentInfo GetEnvironmentInfo(string minecraftFolder, string configDirectory);

    GarbageReport ScanGarbage(IReadOnlyList<string> roots);

    GarbageReport CleanGarbage(IReadOnlyList<string> roots);
}

public sealed record OtherEnvironmentInfo(
    string AppVersion,
    string Runtime,
    string OperatingSystem,
    string MinecraftFolder,
    string ConfigDirectory);

public sealed record GarbageReport(int FileCount, long Bytes);
