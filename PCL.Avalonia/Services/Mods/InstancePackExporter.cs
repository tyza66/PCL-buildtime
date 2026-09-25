using System.IO.Compression;
using System.Text;

namespace PCL.Avalonia.Services.Mods;

public sealed class InstancePackExporter : IInstancePackExporter
{
    private static readonly string[] IncludedIndieFolders =
    [
        "mods", "config", "resourcepacks", "texturepacks", "shaderpacks", "saves",
        "options", "screenshots", "logs", "journeymap", "XaeroWaypoints", "XaeroWorldMap",
    ];

    public string Export(
        string minecraftFolder,
        string versionId,
        string displayName,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var versionFolder = Path.Combine(minecraftFolder, "versions", versionId);
        if (!Directory.Exists(versionFolder))
        {
            throw new DirectoryNotFoundException("未找到版本文件夹：" + versionFolder);
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(outputPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        var root = SanitizeEntryName(displayName);

        var versionJson = FindVersionJson(versionFolder, versionId);
        if (versionJson is not null)
        {
            archive.CreateEntryFromFile(versionJson, $"{root}/{versionId}.json", CompressionLevel.Optimal);
        }

        foreach (var folder in IncludedIndieFolders)
        {
            var source = Path.Combine(minecraftFolder, folder);
            if (!Directory.Exists(source))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                archive.CreateEntryFromFile(file, $"{root}/{folder}/{relative}", CompressionLevel.Optimal);
            }
        }

        var manifest = "{\"displayName\":\""
            + displayName.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
            + "\",\"versionId\":\"" + versionId + "\"}";
        var entry = archive.CreateEntry($"{root}/modpack.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(manifest);
        }

        return outputPath;
    }

    private static string? FindVersionJson(string versionFolder, string versionId)
    {
        var direct = Path.Combine(versionFolder, versionId + ".json");
        if (File.Exists(direct))
        {
            return direct;
        }

        return Directory.EnumerateFiles(versionFolder, "*.json").FirstOrDefault();
    }

    private static string SanitizeEntryName(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(character);
            }
        }

        var result = builder.ToString().Trim('.');
        return string.IsNullOrEmpty(result) ? "instance" : result;
    }
}
