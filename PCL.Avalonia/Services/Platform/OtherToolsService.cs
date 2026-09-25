using System.Runtime.InteropServices;

namespace PCL.Avalonia.Services.Platform;

public sealed class OtherToolsService : IOtherToolsService
{
    private static readonly string[] GarbageExtensions = [".tmp", ".part", ".download"];

    public OtherEnvironmentInfo GetEnvironmentInfo(string minecraftFolder, string configDirectory)
    {
        var version = typeof(OtherToolsService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        return new OtherEnvironmentInfo(
            version,
            RuntimeInformation.FrameworkDescription,
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})",
            minecraftFolder,
            configDirectory);
    }

    public GarbageReport ScanGarbage(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return ProcessRoots(roots, delete: false);
    }

    public GarbageReport CleanGarbage(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        return ProcessRoots(roots, delete: true);
    }

    private static GarbageReport ProcessRoots(IReadOnlyList<string> roots, bool delete)
    {
        var count = 0;
        var bytes = 0L;
        foreach (var root in roots.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!GarbageExtensions.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var file = new FileInfo(filePath);
                    if (!file.Exists)
                    {
                        continue;
                    }

                    bytes += file.Length;
                    if (delete)
                    {
                        file.Delete();
                    }

                    count++;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return new GarbageReport(count, bytes);
    }
}
