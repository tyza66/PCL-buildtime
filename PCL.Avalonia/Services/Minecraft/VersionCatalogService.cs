using System.Text.Json;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class VersionCatalogService : IVersionCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder)
    {
        if (string.IsNullOrWhiteSpace(minecraftFolder))
        {
            return [];
        }

        var versionsRoot = Path.Combine(minecraftFolder, "versions");
        if (!Directory.Exists(versionsRoot))
        {
            return [];
        }

        var result = new List<MinecraftVersion>();
        foreach (var folder in Directory.EnumerateDirectories(versionsRoot))
        {
            var folderName = Path.GetFileName(folder);
            var jsonPath = FindJsonPath(folder, folderName);
            if (jsonPath is null)
            {
                continue;
            }

            var json = TryParseJson(jsonPath);
            if (json?.Id is { Length: > 0 })
            {
                result.Add(new MinecraftVersion
                {
                    Id = json.Id,
                    Folder = folder,
                    JsonPath = jsonPath,
                    Type = string.IsNullOrWhiteSpace(json.Type) ? "release" : json.Type,
                    ReleaseTime = json.ReleaseTime ?? DateTimeOffset.UnixEpoch,
                    InheritsFrom = json.InheritsFrom,
                    MainClass = json.MainClass,
                    Assets = json.Assets,
                    AssetIndexId = json.AssetIndex?.Id,
                    Jar = json.Jar,
                });
            }
        }

        return result
            .OrderByDescending(version => version.ReleaseTime)
            .ThenBy(version => version.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public MinecraftVersionJson? LoadJson(string minecraftFolder, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var folder = Path.Combine(minecraftFolder, "versions", id);
        var jsonPath = FindJsonPath(folder, id);
        return jsonPath is null ? null : TryParseJson(jsonPath);
    }

    private static string? FindJsonPath(string versionFolder, string expectedId)
    {
        var direct = Path.Combine(versionFolder, expectedId + ".json");
        if (File.Exists(direct) && TryParseJson(direct) is not null)
        {
            return direct;
        }

        if (!Directory.Exists(versionFolder))
        {
            return null;
        }

        foreach (var candidate in Directory.EnumerateFiles(versionFolder, "*.json"))
        {
            if (string.Equals(candidate, direct, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = TryParseJson(candidate);
            if (parsed is not null && parsed.Id is { Length: > 0 })
            {
                return candidate;
            }
        }

        return null;
    }

    private static MinecraftVersionJson? TryParseJson(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<MinecraftVersionJson>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
