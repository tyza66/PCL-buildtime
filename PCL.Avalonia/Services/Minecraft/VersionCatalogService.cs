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

            var json = TryParseJsonPath(jsonPath);
            if (json?.Id is { Length: > 0 })
            {
                var rawJson = TryReadText(jsonPath) ?? "";
                result.Add(VersionMetadataBuilder.WithMetadata(new MinecraftVersion
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
                }, json, rawJson));
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
        return jsonPath is null ? null : TryParseJsonPath(jsonPath);
    }

    private static string? FindJsonPath(string versionFolder, string expectedId)
    {
        var direct = Path.Combine(versionFolder, expectedId + ".json");
        if (File.Exists(direct) && TryParseJsonPath(direct) is not null)
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

            var parsed = TryParseJsonPath(candidate);
            if (parsed is not null && parsed.Id is { Length: > 0 })
            {
                return candidate;
            }
        }

        return null;
    }

    private static MinecraftVersionJson? TryParseJsonPath(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return TryParseJsonText(text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static MinecraftVersionJson? TryParseJsonText(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<MinecraftVersionJson>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
