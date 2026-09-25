using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace PCL.Avalonia.Services.Minecraft;

internal static partial class VersionMetadataBuilder
{
    private static readonly Regex VanillaPattern = new(
        "(([1-9][0-9]w[0-9]{2}[a-g])|((1|[2-9][0-9])\\.[0-9]+(\\.[0-9]+)?(-(pre|rc|snapshot-?)[1-9]*| Pre-Release( [1-9])?)?))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DownloadPattern = new(
        "(?<=launcher.mojang.com/mc/game/)[^/]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ForgeLibraryPattern = new(
        "(?<=net.minecraftforge:forge:)[0-9]{1,2}.[0-9+.]+",
        RegexOptions.Compiled);

    private static readonly Regex ForgeLoaderPattern = new(
        "(?<=net.minecraftforge:fmlloader:)[0-9]{1,2}.[0-9+.]+",
        RegexOptions.Compiled);

    private static readonly Regex OptiFineLibraryPattern = new(
        "(?<=optifine:OptiFine:)[0-9]{1,2}.[0-9+.]+",
        RegexOptions.Compiled);

    private static readonly Regex IntermediaryPattern = new(
        "(?<=((fabricmc)|(quiltmc)):intermediary:)[^\"]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OptiFineVersionPattern = new(
        "(?<=HD_U_)[^\":/]+",
        RegexOptions.Compiled);

    private static readonly Regex NeoForgeVersionPattern = new(
        @"(?<=orgeVersion"",[^""]*?"")[^""]+(?="",)",
        RegexOptions.Compiled);

    private static readonly Regex ForgeLoaderVersionPattern = new(
        "(?<=forge:[0-9\\.]+(_pre[0-9]*)?\\-)[0-9\\.]+",
        RegexOptions.Compiled);

    private static readonly Regex FabricLoaderPattern = new(
        "(?<=(net.fabricmc:fabric-loader:)|(org.quiltmc:quilt-loader:))[0-9\\.]+(\\+build.[0-9]+)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MinecraftForgeVersionPattern = new(
        "(?<=net\\.minecraftforge:minecraftforge:)[0-9\\.]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ForgeFmlLoaderVersionPattern = new(
        "(?<=net\\.minecraftforge:fmlloader:[0-9\\.]+-)[0-9\\.]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MinecraftVersion WithMetadata(
        MinecraftVersion version,
        MinecraftVersionJson json,
        string rawJson)
    {
        var (vanillaName, reliable) = ResolveVanillaName(version, json, rawJson);
        var state = ResolveState(version, json, vanillaName, rawJson);
        var loader = ResolveLoader(json, rawJson, out var loaderVersion);
        var (major, minor) = ParseVersion(vanillaName);
        var drop = major >= 1000 ? 209 : major * 10 + minor;
        return version with
        {
            VanillaName = vanillaName,
            Reliable = reliable,
            Drop = drop,
            State = state,
            Loader = loader,
            LoaderVersion = loaderVersion,
            RawJson = rawJson,
        };
    }

    public static string? GetMcFoolName(string name)
    {
        name = name.ToLowerInvariant();
        if (name.StartsWith("2.0", StringComparison.Ordinal))
        {
            return "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！";
        }

        if (name == "15w14a")
        {
            return "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。";
        }

        if (name == "1.rv-pre1")
        {
            return "2016 | 是时候将现代科技带入 Minecraft 了！";
        }

        if (name == "3d shareware v1.34")
        {
            return "2019 | 我们从地下室的废墟里找到了这个开发于 1994 年的杰作！";
        }

        if (name.StartsWith("20w14inf", StringComparison.Ordinal) || name == "20w14∞")
        {
            return "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！";
        }

        if (name == "22w13oneblockatatime")
        {
            return "2022 | 一次一个方块更新！迎接全新的挖掘、合成与骑乘玩法吧！";
        }

        if (name == "23w13a_or_b")
        {
            return "2023 | 研究表明：玩家喜欢作出选择——越多越好！";
        }

        if (name == "24w14potato")
        {
            return "2024 | 毒马铃薯一直都被大家忽视和低估，于是我们超级加强了它！";
        }

        if (name == "25w14craftmine")
        {
            return "2025 | 你可以合成任何东西——包括合成你的世界！";
        }

        if (name == "26w14a")
        {
            return "2026 | 为什么需要物品栏？让方块们跟着你走吧！";
        }

        return null;
    }

    public static bool IsFormatFit(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        if (version.StartsWith("1.", StringComparison.Ordinal)
            && version.Length >= 3
            && char.IsDigit(version[2]))
        {
            return true;
        }

        var match = Regex.Match(version, "^[2-9][0-9]\\.[0-9]+", RegexOptions.None);
        if (match.Success && int.TryParse(version.AsSpan(0, 2), out var major))
        {
            return major >= 26;
        }

        return false;
    }

    private static (string Name, bool Reliable) ResolveVanillaName(
        MinecraftVersion version,
        MinecraftVersionJson json,
        string rawJson)
    {
        var releaseYear = version.ReleaseTime.Year;
        if (releaseYear > 2000 && releaseYear < 2013)
        {
            return ("Old", true);
        }

        if (string.Equals(json.Type, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return ("pending", true);
        }

        if (!string.IsNullOrWhiteSpace(json.ClientVersion))
        {
            return (NormalizeVanillaName(json.ClientVersion), true);
        }

        foreach (var patch in json.Patches)
        {
            if (string.Equals(patch.Id, "game", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(patch.Version))
            {
                return (NormalizeVanillaName(patch.Version), true);
            }
        }

        var gameArguments = json.Arguments?.Game ?? [];
        foreach (var argument in gameArguments)
        {
            if (argument.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                continue;
            }

            if (argument.GetString() == "--fml.mcVersion")
            {
                var index = gameArguments.IndexOf(argument);
                if (index + 1 < gameArguments.Count
                    && gameArguments[index + 1].ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return (NormalizeVanillaName(gameArguments[index + 1].GetString()), true);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(json.InheritsFrom))
        {
            return (NormalizeVanillaName(json.Jar ?? json.InheritsFrom), true);
        }

        var downloadMatch = DownloadPattern.Match(json.Downloads?.Client?.Url ?? "");
        if (downloadMatch.Success)
        {
            return (NormalizeVanillaName(downloadMatch.Value), true);
        }

        var librariesText = JsonSerializerText(json);
        var forgeMatch = ForgeLibraryPattern.Match(librariesText);
        if (!forgeMatch.Success)
        {
            forgeMatch = ForgeLoaderPattern.Match(librariesText);
        }

        if (forgeMatch.Success)
        {
            return (NormalizeVanillaName(forgeMatch.Value), true);
        }

        var optiFineMatch = OptiFineLibraryPattern.Match(librariesText);
        if (optiFineMatch.Success)
        {
            return (NormalizeVanillaName(optiFineMatch.Value), true);
        }

        var intermediaryMatch = IntermediaryPattern.Match(librariesText);
        if (intermediaryMatch.Success)
        {
            return (NormalizeVanillaName(intermediaryMatch.Value), true);
        }

        if (!string.IsNullOrWhiteSpace(json.Jar))
        {
            return (NormalizeVanillaName(json.Jar), true);
        }

        var jarVersionName = TryReadJarVersionName(version);
        if (!string.IsNullOrWhiteSpace(jarVersionName))
        {
            return (NormalizeVanillaName(jarVersionName), true);
        }

        var idMatch = VanillaPattern.Match(json.Id ?? "");
        if (idMatch.Success)
        {
            return (NormalizeVanillaName(idMatch.Value), true);
        }

        idMatch = VanillaPattern.Match(version.Id);
        if (idMatch.Success)
        {
            return (NormalizeVanillaName(idMatch.Value), true);
        }

        var rawMatch = VanillaPattern.Match(JsonWithoutLibraries(rawJson));
        if (rawMatch.Success)
        {
            return (NormalizeVanillaName(rawMatch.Value), false);
        }

        return ("Unknown", false);
    }

    private static InstanceState ResolveState(
        MinecraftVersion version,
        MinecraftVersionJson json,
        string vanillaName,
        string rawJson)
    {
        if (string.IsNullOrWhiteSpace(json.MainClass))
        {
            return InstanceState.Error;
        }

        if (!string.IsNullOrWhiteSpace(json.InheritsFrom)
            && !IsDependencyInstalled(version, json.InheritsFrom))
        {
            return InstanceState.Error;
        }

        if (string.IsNullOrEmpty(vanillaName) || string.Equals(vanillaName, "Unknown", StringComparison.Ordinal))
        {
            return InstanceState.Error;
        }

        if (string.Equals(vanillaName, "Old", StringComparison.Ordinal))
        {
            return InstanceState.Old;
        }

        var fixedReleaseTime = version.ReleaseTime.ToUniversalTime().AddHours(2);
        if (string.Equals(json.Type, "fool", StringComparison.OrdinalIgnoreCase)
            || (fixedReleaseTime.Month == 4
                && fixedReleaseTime.Day == 1
                && string.Equals(json.Type, "snapshot", StringComparison.OrdinalIgnoreCase))
            || GetMcFoolName(vanillaName) is not null)
        {
            return InstanceState.Fool;
        }

        if (IsSnapshot(version, json, vanillaName))
        {
            return InstanceState.Snapshot;
        }

        if (rawJson.Contains("optifine", StringComparison.OrdinalIgnoreCase))
        {
            return InstanceState.OptiFine;
        }

        if (rawJson.Contains("liteloader", StringComparison.OrdinalIgnoreCase))
        {
            return InstanceState.LiteLoader;
        }

        if (rawJson.Contains("net.fabricmc:fabric-loader", StringComparison.OrdinalIgnoreCase)
            || rawJson.Contains("org.quiltmc:quilt-loader", StringComparison.OrdinalIgnoreCase))
        {
            return InstanceState.Fabric;
        }

        if (rawJson.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase)
            && !rawJson.Contains("net.neoforge", StringComparison.OrdinalIgnoreCase))
        {
            return InstanceState.Forge;
        }

        if (rawJson.Contains("net.neoforge", StringComparison.OrdinalIgnoreCase))
        {
            return InstanceState.NeoForge;
        }

        return InstanceState.Original;
    }

    private static bool IsSnapshot(MinecraftVersion version, MinecraftVersionJson json, string vanillaName)
    {
        var jsonType = json.Type ?? "";
        return ContainsIgnoreCase(vanillaName, "w")
            || ContainsIgnoreCase(vanillaName, "snapshot")
            || ContainsIgnoreCase(vanillaName, "rc")
            || ContainsIgnoreCase(vanillaName, "pre")
            || ContainsIgnoreCase(vanillaName, "experimental")
            || vanillaName.Contains('-')
            || ContainsIgnoreCase(version.Id, "combat")
            || jsonType == "snapshot"
            || jsonType == "pending"
            || jsonType == "fool";
    }

    private static LoaderKind ResolveLoader(
        MinecraftVersionJson json,
        string rawJson,
        out string? loaderVersion)
    {
        loaderVersion = null;
        if (rawJson.Contains("optifine", StringComparison.OrdinalIgnoreCase))
        {
            var match = OptiFineVersionPattern.Match(rawJson);
            loaderVersion = match.Success ? match.Value : "未知版本";
            return LoaderKind.OptiFine;
        }

        if (rawJson.Contains("liteloader", StringComparison.OrdinalIgnoreCase))
        {
            return LoaderKind.LiteLoader;
        }

        if (rawJson.Contains("net.fabricmc:fabric-loader", StringComparison.OrdinalIgnoreCase)
            || rawJson.Contains("org.quiltmc:quilt-loader", StringComparison.OrdinalIgnoreCase))
        {
            var match = FabricLoaderPattern.Match(rawJson);
            loaderVersion = match.Success ? match.Value.Replace("+build", "", StringComparison.Ordinal) : "未知版本";
            return LoaderKind.Fabric;
        }

        if (rawJson.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase)
            && !rawJson.Contains("net.neoforge", StringComparison.OrdinalIgnoreCase))
        {
            var match = ForgeLoaderVersionPattern.Match(rawJson);
            if (!match.Success)
            {
                match = MinecraftForgeVersionPattern.Match(rawJson);
            }

            if (!match.Success)
            {
                match = ForgeFmlLoaderVersionPattern.Match(rawJson);
            }

            loaderVersion = match.Success ? match.Value : "未知版本";
            return LoaderKind.Forge;
        }

        if (rawJson.Contains("net.neoforge", StringComparison.OrdinalIgnoreCase))
        {
            var match = NeoForgeVersionPattern.Match(rawJson);
            loaderVersion = match.Success ? match.Value : "未知版本";
            return LoaderKind.NeoForge;
        }

        return LoaderKind.None;
    }

    private static string NormalizeVanillaName(string? value)
    {
        if (value is null)
        {
            return "";
        }

        return value
            .Replace("_unobfuscated", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Unobfuscated", "", StringComparison.OrdinalIgnoreCase);
    }

    private static string JsonSerializerText(MinecraftVersionJson json)
    {
        var librariesText = "";
        foreach (var library in json.Libraries)
        {
            librariesText += library.Name ?? "";
        }

        return librariesText;
    }

    private static string JsonWithoutLibraries(string rawJson)
    {
        try
        {
            if (JsonNode.Parse(rawJson) is JsonObject root)
            {
                root.Remove("libraries");
                return root.ToJsonString();
            }
        }
        catch (JsonException)
        {
        }

        return rawJson;
    }

    private static bool IsDependencyInstalled(MinecraftVersion version, string parentId)
    {
        var versionsRoot = Path.GetDirectoryName(version.Folder);
        if (string.IsNullOrWhiteSpace(versionsRoot))
        {
            return false;
        }

        return File.Exists(Path.Combine(versionsRoot, parentId, parentId + ".json"));
    }

    private static string? TryReadJarVersionName(MinecraftVersion version)
    {
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(version.Folder));
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        var jarPath = Path.Combine(version.Folder, folderName + ".jar");
        if (!File.Exists(jarPath))
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var entry = archive.GetEntry("version.json");
            if (entry is null)
            {
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            if (JsonNode.Parse(reader.ReadToEnd()) is not JsonObject root)
            {
                return null;
            }

            var name = root["name"]?.GetValue<string>();
            return name is { Length: < 32 } ? name : null;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or NotSupportedException
            or JsonException)
        {
            return null;
        }
    }

    private static (int Major, int Minor) ParseVersion(string? name)
    {
        if (name is null)
        {
            return (9999, 0);
        }

        name = name.ToLowerInvariant().Replace("_unobfuscated", "").Replace(" Unobfuscated", "");
        if (name.StartsWith("2.0", StringComparison.Ordinal))
        {
            return ParseVersion("1.5.1");
        }

        if (name == "15w14a")
        {
            return ParseVersion("1.8.3");
        }

        if (name.Contains(".rv-pre", StringComparison.Ordinal))
        {
            return ParseVersion("1.9.2");
        }

        if (name.Contains("shareware", StringComparison.Ordinal))
        {
            return ParseVersion("1.13.2");
        }

        if (name.StartsWith("20w14", StringComparison.Ordinal) && name != "20w14a")
        {
            return ParseVersion("1.15.2");
        }

        if (name.Contains("oneblockatatime", StringComparison.Ordinal))
        {
            return ParseVersion("1.18.2");
        }

        if (name.Contains("23w13a", StringComparison.Ordinal) && name != "20w13a")
        {
            return ParseVersion("1.19.4");
        }

        if (name == "24w14potato")
        {
            return ParseVersion("1.20.4");
        }

        if (name == "25w14craftmine")
        {
            return ParseVersion("1.21.4");
        }

        if (name == "26w14a")
        {
            return ParseVersion("26.1.1");
        }

        var segments = name.Split(' ', '_', '-', '.');
        if (name.StartsWith("1.", StringComparison.Ordinal) && segments.Length >= 2)
        {
            return (ParseInt(segments[1]), 0);
        }

        var twoDigitMatch = Regex.Match(name, "^[2-9][0-9]\\.");
        if (twoDigitMatch.Success && segments.Length >= 2)
        {
            return (ParseInt(segments[0]), ParseInt(segments[1]));
        }

        return (9999, 0);
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static bool ContainsIgnoreCase(string text, string value)
    {
        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
