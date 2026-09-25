using System.Text;
using System.Text.Json.Nodes;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class VersionManagerService : IVersionManagerService
{
    private const string SettingsFile = "Setup.ini";
    private const string KeyFavorite = "IsStar";
    private const string KeyDisplayType = "DisplayType";
    private const string KeyDescription = "CustomInfo";
    private const string KeyMemory = "VersionRamCustom";
    private const string KeyJavaPath = "JavaDir";
    private const string KeyJvmArguments = "VersionJvmArgs";
    private const string KeyGameArguments = "VersionGameArgs";

    public VersionSettings LoadSettings(string minecraftFolder, string versionId)
    {
        var path = GetSettingsPath(minecraftFolder, versionId);
        if (!File.Exists(path))
        {
            return new VersionSettings();
        }

        var values = ReadIni(path);
        return new VersionSettings
        {
            IsFavorite = ParseBoolean(values.GetValueOrDefault(KeyFavorite)),
            IsHidden = ParseDisplayType(values.GetValueOrDefault(KeyDisplayType)),
            DisplayType = ParseDisplayTypeValue(values.GetValueOrDefault(KeyDisplayType)),
            Description = values.GetValueOrDefault(KeyDescription) ?? "",
            MaxMemoryMb = ParseNullableInt(values.GetValueOrDefault(KeyMemory)),
            JavaPath = values.GetValueOrDefault(KeyJavaPath),
            JvmArguments = values.GetValueOrDefault(KeyJvmArguments),
            GameArguments = values.GetValueOrDefault(KeyGameArguments),
        };
    }

    public void SetFavorite(string minecraftFolder, string versionId, bool isFavorite)
    {
        UpdateSettings(minecraftFolder, versionId, new Dictionary<string, string>
        {
            [KeyFavorite] = isFavorite ? "True" : "False",
        });
    }

    public void SetHidden(string minecraftFolder, string versionId, bool isHidden)
    {
        SetDisplayType(
            minecraftFolder,
            versionId,
            isHidden ? InstanceDisplayType.Hidden : InstanceDisplayType.Auto);
    }

    public void SetDisplayType(string minecraftFolder, string versionId, InstanceDisplayType displayType)
    {
        UpdateSettings(minecraftFolder, versionId, new Dictionary<string, string>
        {
            [KeyDisplayType] = ((int)displayType).ToString(),
        });
    }

    public void SetDescription(string minecraftFolder, string versionId, string description)
    {
        UpdateSettings(minecraftFolder, versionId, new Dictionary<string, string>
        {
            [KeyDescription] = description ?? "",
        });
    }

    public void SetInstanceLaunchSettings(
        string minecraftFolder,
        string versionId,
        int? maxMemoryMb,
        string? javaPath,
        string? jvmArguments,
        string? gameArguments)
    {
        if (maxMemoryMb is not null && maxMemoryMb != 0 && maxMemoryMb < 256)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMemoryMb), "手动内存不能小于 256 MB");
        }

        UpdateSettings(minecraftFolder, versionId, new Dictionary<string, string>
        {
            [KeyMemory] = maxMemoryMb?.ToString() ?? "",
            [KeyJavaPath] = javaPath ?? "",
            [KeyJvmArguments] = jvmArguments ?? "",
            [KeyGameArguments] = gameArguments ?? "",
        });
    }

    public string Rename(string minecraftFolder, string versionId, string newName)
    {
        ValidateVersionId(versionId);
        ValidateVersionId(newName);
        var versionsRoot = Path.Combine(minecraftFolder, "versions");
        var oldFolder = Path.Combine(versionsRoot, versionId);
        var newFolder = Path.Combine(versionsRoot, newName);
        if (!Directory.Exists(oldFolder))
        {
            throw new DirectoryNotFoundException($"未找到版本文件夹：{oldFolder}");
        }

        if (Directory.Exists(newFolder) || File.Exists(newFolder))
        {
            throw new InvalidOperationException($"目标版本已存在：{newName}");
        }

        if (string.Equals(versionId, newName, StringComparison.Ordinal))
        {
            return newName;
        }

        Directory.Move(oldFolder, newFolder);
        RenameSupportFiles(newFolder, versionId, newName);
        RewriteVersionJson(newFolder, newName);
        UpdateSettingsPath(minecraftFolder, newFolder, versionId, newName);
        return newName;
    }

    public void Delete(string minecraftFolder, string versionId)
    {
        ValidateVersionId(versionId);
        var folder = Path.Combine(minecraftFolder, "versions", versionId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string GetSettingsPath(string minecraftFolder, string versionId)
    {
        ValidateVersionId(versionId);
        return Path.Combine(minecraftFolder, "versions", versionId, "PCL", "Setup.ini");
    }

    private static void UpdateSettings(
        string minecraftFolder,
        string versionId,
        IReadOnlyDictionary<string, string> updates)
    {
        ValidateVersionId(versionId);
        var versionFolder = Path.Combine(minecraftFolder, "versions", versionId);
        var pclFolder = Path.Combine(versionFolder, "PCL");
        Directory.CreateDirectory(pclFolder);
        var path = Path.Combine(pclFolder, SettingsFile);
        var values = File.Exists(path)
            ? ReadIni(path)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in updates)
        {
            values[pair.Key] = pair.Value;
        }

        WriteIni(path, values);
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)
                || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return result;
    }

    private static void WriteIni(string path, IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder();
        foreach (var pair in values)
        {
            builder.Append(pair.Key).Append(':').Append(pair.Value).Append("\r\n");
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static void RenameSupportFiles(string folder, string oldId, string newId)
    {
        var prefix = oldId + "-";
        var suffix = oldId + ".";
        foreach (var directory in Directory.EnumerateDirectories(folder).ToList())
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(directory, Path.Combine(folder, newId + name[oldId.Length..]));
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder).ToList())
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(file, Path.Combine(folder, newId + name[oldId.Length..]));
            }
            else if (name.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(file, Path.Combine(folder, newId + name[oldId.Length..]));
            }
        }
    }

    private static void RewriteVersionJson(string versionFolder, string newId)
    {
        var jsonPath = Path.Combine(versionFolder, newId + ".json");
        if (!File.Exists(jsonPath))
        {
            return;
        }

        if (JsonNode.Parse(File.ReadAllText(jsonPath)) is not JsonObject root)
        {
            return;
        }

        root["id"] = newId;
        File.WriteAllText(jsonPath, root.ToJsonString());
    }

    private static void UpdateSettingsPath(
        string minecraftFolder,
        string versionFolder,
        string oldId,
        string newId)
    {
        var path = Path.Combine(versionFolder, "PCL", SettingsFile);
        if (!File.Exists(path))
        {
            return;
        }

        var text = File.ReadAllText(path);
        var oldFolder = Path.Combine(minecraftFolder, "versions", oldId);
        var newFolder = Path.Combine(minecraftFolder, "versions", newId);
        text = text.Replace(oldFolder, newFolder, StringComparison.Ordinal);
        text = text.Replace(Path.Combine("versions", oldId), Path.Combine("versions", newId), StringComparison.Ordinal);
        text = text.Replace("versions/" + oldId, "versions/" + newId, StringComparison.Ordinal);
        text = text.Replace("versions\\" + oldId, "versions\\" + newId, StringComparison.Ordinal);
        File.WriteAllText(path, text);
    }

    private static bool ParseBoolean(string? value)
    {
        return bool.TryParse(value, out var result) && result;
    }

    private static bool ParseDisplayType(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
    }

    private static InstanceDisplayType ParseDisplayTypeValue(string? value)
    {
        if (int.TryParse(value, out var numeric)
            && Enum.IsDefined(typeof(InstanceDisplayType), numeric))
        {
            return (InstanceDisplayType)numeric;
        }

        return InstanceDisplayType.Auto;
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value, out var numeric) ? numeric : null;
    }

    private static void ValidateVersionId(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("版本 ID 不能为空", nameof(versionId));
        }

        if (versionId is "." or ".."
            || versionId.Contains('/')
            || versionId.Contains('\\'))
        {
            throw new ArgumentException("版本 ID 包含非法路径字符", nameof(versionId));
        }

        foreach (var character in Path.GetInvalidFileNameChars())
        {
            if (versionId.Contains(character))
            {
                throw new ArgumentException("版本 ID 包含非法字符", nameof(versionId));
            }
        }
    }
}
