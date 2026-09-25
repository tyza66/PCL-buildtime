using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class GameLauncher : IGameLauncher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IVersionCatalogService _catalog;
    private readonly string _launcherVersion;

    public GameLauncher(IVersionCatalogService catalog)
    {
        _catalog = catalog;
        _launcherVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    }

    public LaunchPlan BuildLaunchPlan(MinecraftVersion version, AppSettings settings, string javaExecutable)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(javaExecutable);
        if (string.IsNullOrWhiteSpace(settings.MinecraftFolder))
        {
            throw new InvalidOperationException("未设置游戏目录");
        }

        var minecraftFolder = settings.MinecraftFolder.Trim();
        var chain = LoadVersionChain(minecraftFolder, version);
        var root = chain[^1];
        var rootId = root.Id ?? version.Id;
        var rootFolder = Path.Combine(minecraftFolder, "versions", rootId);
        var librariesRoot = Path.Combine(minecraftFolder, "libraries");
        var nativesDirectory = Path.Combine(version.Folder, version.Id + "-natives");
        var assetsRoot = Path.Combine(minecraftFolder, "assets");
        var assetsIndexName = ResolveAssetsIndexName(chain, version.Id);
        var mainClass = chain
            .Select(json => json.MainClass)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidOperationException("版本 JSON 中未找到 mainClass");

        var libraries = ResolveLibraries(chain, librariesRoot);
        var mainJar = Path.Combine(rootFolder, (root.Jar ?? rootId) + ".jar");
        var classPathEntries = libraries
            .Where(library => !library.IsNatives && File.Exists(library.Path))
            .Select(library => library.Path)
            .ToList();
        if (File.Exists(mainJar))
        {
            classPathEntries.Add(mainJar);
        }

        ExtractNatives(libraries.Where(library => library.IsNatives), nativesDirectory);

        var userName = string.IsNullOrWhiteSpace(settings.UserName) ? "Player" : settings.UserName.Trim();
        var offlineUuid = CreateOfflineUuid(userName);
        var jvmArgs = BuildJvmArguments(chain, settings);
        var gameArgs = BuildGameArguments(chain, version, userName, offlineUuid, assetsIndexName);
        var classPath = string.Join(Path.PathSeparator, classPathEntries);
        var replacements = BuildReplacements(
            version,
            settings,
            minecraftFolder,
            librariesRoot,
            nativesDirectory,
            assetsRoot,
            assetsIndexName,
            offlineUuid,
            userName,
            mainJar,
            version.Type,
            classPath);

        var jvmArguments = DeduplicateArguments(jvmArgs)
            .Select(argument => ReplaceMarkers(argument, replacements))
            .ToList();
        var gameArguments = DeduplicateArguments(gameArgs)
            .Select(argument => ReplaceMarkers(argument, replacements))
            .ToList();
        if (!HasClassPathFlag(jvmArguments))
        {
            jvmArguments.Add("-cp");
            jvmArguments.Add(classPath);
        }

        var arguments = jvmArguments
            .Concat([mainClass])
            .Concat(gameArguments)
            .ToList();

        return new LaunchPlan
        {
            JavaExecutable = javaExecutable,
            WorkingDirectory = minecraftFolder,
            NativesDirectory = nativesDirectory,
            ClassPath = string.Join(Path.PathSeparator, classPathEntries),
            MainClass = mainClass,
            Arguments = arguments,
            Version = version,
        };
    }

    public GameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!File.Exists(plan.JavaExecutable))
        {
            throw new FileNotFoundException("未找到 Java 可执行文件", plan.JavaExecutable);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = plan.JavaExecutable,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = output is not null,
            RedirectStandardError = output is not null,
            CreateNoWindow = true,
        };
        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Java 进程启动失败");
        if (output is not null)
        {
            _ = Task.Run(() => ForwardLines(process.StandardOutput, output));
            _ = Task.Run(() => ForwardLines(process.StandardError, output));
        }

        return new GameLaunch(process);
    }

    private List<MinecraftVersionJson> LoadVersionChain(string minecraftFolder, MinecraftVersion version)
    {
        var result = new List<MinecraftVersionJson>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var id = version.Id;

        while (!string.IsNullOrWhiteSpace(id) && visited.Add(id))
        {
            var json = _catalog.LoadJson(minecraftFolder, id)
                ?? throw new FileNotFoundException($"未找到版本 JSON：{id}", Path.Combine(minecraftFolder, "versions", id));
            result.Add(json);
            id = json.InheritsFrom;
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("版本 JSON 为空");
        }

        return result;
    }

    private static string ResolveAssetsIndexName(
        IReadOnlyList<MinecraftVersionJson> chain,
        string fallbackId)
    {
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var assetIndexId = chain[index].AssetIndex?.Id;
            if (!string.IsNullOrWhiteSpace(assetIndexId))
            {
                return assetIndexId;
            }
        }

        for (var index = chain.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(chain[index].Assets))
            {
                return chain[index].Assets ?? fallbackId;
            }
        }

        return fallbackId;
    }

    private sealed record ResolvedLibrary(
        string Name,
        string Path,
        bool IsNatives,
        IReadOnlyList<string> Exclude);

    private static List<ResolvedLibrary> ResolveLibraries(
        IReadOnlyList<MinecraftVersionJson> chain,
        string librariesRoot)
    {
        var byKey = new Dictionary<string, ResolvedLibrary>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in chain)
        {
            foreach (var library in json.Libraries)
            {
                if (!MinecraftRules.RulesMatch(library.Rules))
                {
                    continue;
                }

                var nativeTemplate = MinecraftRules.ResolveNativeTemplate(library);
                var isNatives = nativeTemplate is not null;
                var classifier = nativeTemplate is null
                    ? null
                    : MinecraftRules.ResolveNativeClassifier(nativeTemplate);
                var artifact = isNatives
                    ? library.Downloads?.Classifiers?.GetValueOrDefault(classifier ?? "")
                    : library.Downloads?.Artifact;
                var path = ResolveLibraryPath(librariesRoot, library, classifier, artifact?.Path);
                var resolved = new ResolvedLibrary(
                    library.Name ?? path,
                    path,
                    isNatives,
                    library.Extract?.Exclude ?? []);

                var key = resolved.Name + "|" + resolved.IsNatives;
                if (!byKey.TryGetValue(key, out var existing))
                {
                    byKey[key] = resolved;
                    continue;
                }

                if (CompareVersion(GetVersionPart(resolved.Path), GetVersionPart(existing.Path)) >= 0)
                {
                    byKey[key] = resolved;
                }
            }
        }

        return [.. byKey.Values];
    }

    private static string ResolveLibraryPath(
        string librariesRoot,
        LibraryJson library,
        string? classifier,
        string? artifactPath)
    {
        if (!string.IsNullOrWhiteSpace(artifactPath))
        {
            return Path.Combine(librariesRoot, artifactPath.Replace('/', Path.DirectorySeparatorChar));
        }

        var name = library.Name ?? throw new InvalidOperationException("支持库缺少 name 字段");
        var parts = name.Split(':');
        if (parts.Length < 3)
        {
            throw new InvalidOperationException($"无法解析支持库坐标：{name}");
        }

        var group = parts[0];
        var artifact = parts[1];
        var version = parts[2];
        var classifierPart = classifier ?? (parts.Length > 3 ? parts[3] : null);
        var fileName = artifact + "-" + version
            + (string.IsNullOrEmpty(classifierPart) ? "" : "-" + classifierPart)
            + ".jar";
        return Path.Combine(
            librariesRoot,
            group.Replace('.', Path.DirectorySeparatorChar),
            artifact,
            version,
            fileName);
    }

    private static string GetVersionPart(string libraryPath)
    {
        return Path.GetFileName(Path.GetDirectoryName(libraryPath) ?? "") ?? "";
    }

    private static int CompareVersion(string left, string right)
    {
        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static void ExtractNatives(IEnumerable<ResolvedLibrary> natives, string target)
    {
        Directory.CreateDirectory(target);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var native in natives)
        {
            if (!File.Exists(native.Path))
            {
                continue;
            }

            using var archive = ZipFile.OpenRead(native.Path);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/"))
                {
                    continue;
                }

                if (entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (native.Exclude.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.Combine(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                expected.Add(destination);
                if (File.Exists(destination) && new FileInfo(destination).Length == entry.Length)
                {
                    continue;
                }

                using var source = entry.Open();
                using var output = File.Create(destination);
                source.CopyTo(output);
            }
        }

        foreach (var stale in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
        {
            if (expected.Contains(stale))
            {
                continue;
            }

            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static List<string> BuildJvmArguments(
        IReadOnlyList<MinecraftVersionJson> chain,
        AppSettings settings)
    {
        var result = new List<string>();
        var hasNewArguments = chain.Any(json => json.Arguments?.Jvm is { Count: > 0 });
        if (hasNewArguments)
        {
            foreach (var json in chain)
            {
                AppendArgumentElements(json.Arguments?.Jvm, result);
            }
        }
        else
        {
            result.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump");
            result.Add("-Djava.library.path=${natives_directory}");
            result.Add("-cp");
            result.Add("${classpath}");
        }

        result.Add($"-Xmx{Math.Max(256, settings.MaxMemoryMb)}M");
        result.Add("-Dlog4j2.formatMsgNoLookups=true");
        return result;
    }

    private static List<string> BuildGameArguments(
        IReadOnlyList<MinecraftVersionJson> chain,
        MinecraftVersion version,
        string userName,
        string offlineUuid,
        string assetsIndexName)
    {
        var result = new List<string>();
        var hasNewArguments = chain.Any(json => json.Arguments?.Game is { Count: > 0 });
        if (hasNewArguments)
        {
            foreach (var json in chain)
            {
                AppendArgumentElements(json.Arguments?.Game, result);
            }
        }
        else
        {
            foreach (var json in chain)
            {
                if (!string.IsNullOrWhiteSpace(json.MinecraftArguments))
                {
                    result.AddRange(SplitArguments(json.MinecraftArguments));
                }
            }
        }

        result.Add("--username");
        result.Add(userName);
        result.Add("--version");
        result.Add(version.Id);
        result.Add("--gameDir");
        result.Add("${game_directory}");
        result.Add("--assetsDir");
        result.Add("${assets_root}");
        result.Add("--assetIndex");
        result.Add(assetsIndexName);
        result.Add("--uuid");
        result.Add(offlineUuid);
        result.Add("--accessToken");
        result.Add("0");
        result.Add("--userType");
        result.Add("legacy");
        result.Add("--versionType");
        result.Add(string.IsNullOrWhiteSpace(version.Type) ? "release" : version.Type);
        result.Add("--userProperties");
        result.Add("${user_properties}");
        return result;
    }

    private static void AppendArgumentElements(List<JsonElement>? elements, List<string> target)
    {
        if (elements is null)
        {
            return;
        }

        foreach (var element in elements)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                target.Add(element.GetString() ?? "");
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            ArgumentElementJson? argument = null;
            try
            {
                argument = element.Deserialize<ArgumentElementJson>(JsonOptions);
            }
            catch (JsonException)
            {
            }

            if (argument is null || (argument.Rules is not null && !MinecraftRules.RulesMatch(argument.Rules)))
            {
                continue;
            }

            if (argument.Value.ValueKind == JsonValueKind.String)
            {
                target.Add(argument.Value.GetString() ?? "");
            }
            else if (argument.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in argument.Value.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        target.Add(value.GetString() ?? "");
                    }
                }
            }
        }
    }

    private IReadOnlyDictionary<string, string> BuildReplacements(
        MinecraftVersion version,
        AppSettings settings,
        string minecraftFolder,
        string librariesRoot,
        string nativesDirectory,
        string assetsRoot,
        string assetsIndexName,
        string offlineUuid,
        string userName,
        string mainJar,
        string versionType,
        string classPath)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${classpath}"] = classPath,
            ["${classpath_separator}"] = Path.PathSeparator.ToString(),
            ["${natives_directory}"] = nativesDirectory,
            ["${library_directory}"] = librariesRoot,
            ["${libraries_directory}"] = librariesRoot,
            ["${pure_directory}"] = nativesDirectory,
            ["${launcher_name}"] = "PCL2.Avalonia",
            ["${launcher_version}"] = _launcherVersion,
            ["${version_name}"] = version.Id,
            ["${game_directory}"] = minecraftFolder,
            ["${assets_root}"] = assetsRoot,
            ["${assets_index_name}"] = assetsIndexName,
            ["${user_properties}"] = "{}",
            ["${auth_player_name}"] = userName,
            ["${auth_uuid}"] = offlineUuid,
            ["${auth_access_token}"] = "0",
            ["${access_token}"] = "0",
            ["${auth_session}"] = "0",
            ["${user_type}"] = "legacy",
            ["${primary_jar}"] = mainJar,
            ["${game_assets}"] = Path.Combine(assetsRoot, "virtual", "legacy"),
            ["${resolution_width}"] = "854",
            ["${resolution_height}"] = "480",
            ["${version_type}"] = versionType,
        };
    }

    private static bool HasClassPathFlag(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], "-cp", StringComparison.Ordinal)
                || string.Equals(arguments[index], "-classpath", StringComparison.Ordinal)
                || string.Equals(arguments[index], "--class-path", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReplaceMarkers(string argument, IReadOnlyDictionary<string, string> replacements)
    {
        return replacements.Aggregate(argument, (current, replacement) =>
            current.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal));
    }

    private static List<string> DeduplicateArguments(List<string> arguments)
    {
        var result = new List<string>();
        var index = 0;
        while (index < arguments.Count)
        {
            var key = arguments[index];
            if (IsKeyValuePair(key, arguments, index, out var valueIndex))
            {
                var replaced = false;
                for (var j = 0; j + 1 < result.Count; j++)
                {
                    if (result[j] == key && key != "--tweakClass")
                    {
                        result[j + 1] = arguments[valueIndex];
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    result.Add(key);
                    result.Add(arguments[valueIndex]);
                }

                index = valueIndex + 1;
                continue;
            }

            if (!result.Contains(key))
            {
                result.Add(key);
            }

            index++;
        }

        return result;
    }

    private static bool IsKeyValuePair(string key, IReadOnlyList<string> arguments, int index, out int valueIndex)
    {
        valueIndex = index + 1;
        if (!key.StartsWith("-", StringComparison.Ordinal) || key.Contains('='))
        {
            return false;
        }

        return valueIndex < arguments.Count && !arguments[valueIndex].StartsWith("-", StringComparison.Ordinal);
    }

    private static List<string> SplitArguments(string input)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var character in input)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                current.Append(character);
            }
            else if (character == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string CreateOfflineUuid(string userName)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + userName));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes).ToString();
    }

    private static void ForwardLines(StreamReader reader, IProgress<string> output)
    {
        try
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                output.Report(line);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
