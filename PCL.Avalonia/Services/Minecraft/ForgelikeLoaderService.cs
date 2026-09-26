using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class ForgelikeLoaderService : IForgelikeLoaderService
{
    private const string ForgeListBmclBase = "https://bmclapi2.bangbang93.com/forge/minecraft";
    private const string ForgeListOfficialBase = "https://files.minecraftforge.net/maven/net/minecraftforge/forge";
    private const string NeoForgeListBmclBase = "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases";
    private const string NeoForgeListOfficialBase = "https://maven.neoforged.net/api/maven/versions/releases";
    private const string BmclMavenBase = "https://bmclapi2.bangbang93.com/maven";
    private const string ForgeMavenOfficialBase = "https://files.minecraftforge.net/maven";
    private const string NeoForgeMavenOfficialBase = "https://maven.neoforged.net/releases";
    private const string NeoForgeVersionRegex = "(?<=\")(1\\.20\\.1-)?\\d+\\.[^\\.]+\\.\\d+(\\.\\d+)?(-(beta|alpha)(\\.\\d+)?)?(\\+snapshot-\\d+)?(?=\")";

    private readonly IDownloadClient _downloadClient;
    private readonly IVersionInstaller _versionInstaller;
    private readonly IForgelikeInstallRunner _installRunner;

    public ForgelikeLoaderService(
        IDownloadClient downloadClient,
        IVersionInstaller versionInstaller,
        IForgelikeInstallRunner installRunner)
    {
        _downloadClient = downloadClient;
        _versionInstaller = versionInstaller;
        _installRunner = installRunner;
    }

    public async Task<IReadOnlyList<ForgelikeLoaderVersion>> GetForgeVersionsAsync(
        string gameVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        var game = gameVersion.Trim();
        var escaped = game.Replace('-', '_');
        var json = await _downloadClient
            .GetStringAsync(
                [
                    $"{ForgeListBmclBase}/{escaped}",
                    $"{ForgeListOfficialBase}/index_{escaped}.html",
                ],
                cancellationToken)
            .ConfigureAwait(false);

        var entries = JsonSerializer.Deserialize<List<ForgeVersionEntryJson>>(json, JsonOptions) ?? [];
        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Version))
            .Select(entry => ToForgeVersion(entry, game))
            .ToArray();
    }

    public async Task<IReadOnlyList<ForgelikeLoaderVersion>> GetNeoForgeVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        var latestJson = await _downloadClient
            .GetStringAsync(
                [
                    $"{NeoForgeListBmclBase}/net/neoforged/neoforge",
                    $"{NeoForgeListOfficialBase}/net/neoforged/neoforge",
                ],
                cancellationToken)
            .ConfigureAwait(false);
        var legacyJson = await _downloadClient
            .GetStringAsync(
                [
                    $"{NeoForgeListBmclBase}/net/neoforged/forge",
                    $"{NeoForgeListOfficialBase}/net/neoforged/forge",
                ],
                cancellationToken)
            .ConfigureAwait(false);

        var names = Regex
            .Matches(legacyJson + latestJson, NeoForgeVersionRegex, RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Where(name => name is not "47.1.82"
                and not "1.20.1-47.1.82"
                and not "20.4.29")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ToNeoForgeVersion)
            .OrderByDescending(version => version.SortVersion)
            .ThenByDescending(version => version.VersionName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return names;
    }

    public async Task<ForgelikeInstallResult> InstallAsync(
        ForgelikeLoaderVersion version,
        string minecraftFolder,
        DownloadSource source,
        IProgress<ForgelikeInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);

        var errors = new List<string>();
        var isNeoForge = version.Kind == ForgelikeKind.NeoForge;
        var loaderName = isNeoForge ? "neoforge" : "forge";
        var displayName = isNeoForge ? "NeoForge" : "Forge";
        var loaderVersion = version.VersionName;
        var inherit = version.GameVersion;
        var versionId = $"{loaderName}-{loaderVersion}";
        var versionFolder = Path.Combine(minecraftFolder, "versions", versionId);
        var isNewVersion = isNeoForge || IsNewForgeVersion(loaderVersion);

        if (isNewVersion)
        {
            progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.BaseVersion, inherit, 0, 1));
            var baseJsonPath = Path.Combine(minecraftFolder, "versions", inherit, inherit + ".json");
            if (!File.Exists(baseJsonPath))
            {
                var baseResult = await _versionInstaller
                    .InstallAsync(inherit, null, source, minecraftFolder, null, cancellationToken)
                    .ConfigureAwait(false);
                errors.AddRange(baseResult.Errors.Select(item => $"{displayName} 安装原版 {inherit} 失败：{item}"));
            }

            progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.BaseVersion, inherit, 1, 1));
        }

        var installerDestination = Path.Combine(minecraftFolder, "tmp", "forge_installer.jar");
        var installerUrls = BuildInstallerUrls(version, isNeoForge, loaderName);
        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Installer, loaderVersion, 0, 1));
        try
        {
            var request = new DownloadRequest(installerUrls, installerDestination, "forge_installer.jar");
            await _downloadClient.DownloadAsync(request, null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"{displayName} 安装器下载失败：{ErrorMessageFormatter.Brief(ex)}");
        }

        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Installer, loaderVersion, 1, 1));

        if (isNewVersion)
        {
            await InstallNewVersionAsync(
                    version,
                    minecraftFolder,
                    installerDestination,
                    source,
                    errors,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (File.Exists(installerDestination))
        {
            await InstallLegacyAsync(
                    version,
                    minecraftFolder,
                    installerDestination,
                    errors,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Complete, versionId, 1, 1));
        return new ForgelikeInstallResult(versionId, inherit, loaderVersion, errors);
    }

    private async Task InstallNewVersionAsync(
        ForgelikeLoaderVersion version,
        string minecraftFolder,
        string downloadedInstaller,
        DownloadSource source,
        List<string> errors,
        IProgress<ForgelikeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var isNeoForge = version.Kind == ForgelikeKind.NeoForge;
        var loaderName = isNeoForge ? "neoforge" : "forge";
        var displayName = isNeoForge ? "NeoForge" : "Forge";
        var versionId = $"{loaderName}-{version.VersionName}";
        var versionFolder = Path.Combine(minecraftFolder, "versions", versionId);

        string mergedJson;
        try
        {
            var sourcePath = PickInstallerSource(minecraftFolder, downloadedInstaller);
            if (string.IsNullOrEmpty(sourcePath))
            {
                errors.Add($"{displayName} 安装器文件不可用：{downloadedInstaller}");
                return;
            }

            using var archive = ZipFile.OpenRead(sourcePath);
            var profileEntry = archive.GetEntry("install_profile.json")
                ?? throw new InvalidDataException("安装器缺少 install_profile.json");
            var profileString = ReadEntry(profileEntry);
            var versionEntry = archive.GetEntry("version.json");
            var versionString = versionEntry is null ? null : ReadEntry(versionEntry);
            mergedJson = MergeJson(profileString, versionString);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"{displayName} 安装器解析失败：{ErrorMessageFormatter.Brief(ex)}");
            progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Injector, versionId, 0, 1));
            try
            {
                if (await RunInstallerAsync(minecraftFolder, downloadedInstaller, version.Kind, cancellationToken))
                {
                    CopyRunnerJsonAsync(minecraftFolder, versionId, errors);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex2)
            {
                errors.Add($"运行 {displayName} 安装器失败：{ex2.Message}");
            }

            progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Injector, versionId, 1, 1));
            return;
        }

        var profile = JsonSerializer.Deserialize<MinecraftVersionJson>(mergedJson, JsonOptions)
            ?? throw new InvalidOperationException("安装器版本数据解析结果为空");
        var libraries = profile.Libraries ?? [];
        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Libraries, null, 0, libraries.Count));
        for (var index = 0; index < libraries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var library = libraries[index];
            var libraryError = await DownloadLibraryAsync(
                    library,
                    minecraftFolder,
                    source,
                    loaderName,
                    version.GameVersion,
                    version.VersionName,
                    cancellationToken)
                .ConfigureAwait(false);
            if (libraryError is not null)
            {
                errors.Add(libraryError);
            }

            progress?.Report(new ForgelikeInstallProgress(
                ForgelikeInstallStage.Libraries,
                library.Name ?? Path.GetFileName(library.Downloads?.Artifact?.Path),
                index + 1,
                libraries.Count));
        }

        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Injector, versionId, 0, 1));
        try
        {
            if (await RunInstallerAsync(minecraftFolder, downloadedInstaller, version.Kind, cancellationToken))
            {
                CopyRunnerJsonAsync(minecraftFolder, versionId, errors);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"运行 {displayName} 安装器失败：{ErrorMessageFormatter.Brief(ex)}");
        }

        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Injector, versionId, 1, 1));
    }

    private async Task<bool> RunInstallerAsync(
        string minecraftFolder,
        string installerPath,
        ForgelikeKind kind,
        CancellationToken cancellationToken)
    {
        var existing = Directory.Exists(Path.Combine(minecraftFolder, "versions"))
            ? Directory.GetDirectories(Path.Combine(minecraftFolder, "versions"))
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        await _installRunner.RunAsync(minecraftFolder, installerPath, kind, cancellationToken).ConfigureAwait(false);
        var versionsRoot = Path.Combine(minecraftFolder, "versions");
        if (!Directory.Exists(versionsRoot))
        {
            return false;
        }

        var newFolders = Directory
            .GetDirectories(versionsRoot)
            .Where(path => !existing.Contains(Path.GetFileName(path)))
            .ToArray();
        return newFolders.Length > 0;
    }

    private static void CopyRunnerJsonAsync(
        string minecraftFolder,
        string versionId,
        List<string> errors)
    {
        var versionsRoot = Path.Combine(minecraftFolder, "versions");
        if (!Directory.Exists(versionsRoot))
        {
            errors.Add($"未找到版本 {versionId} 的安装产物目录");
            return;
        }

        var candidates = Directory
            .GetDirectories(versionsRoot)
            .Where(path => Path.GetFileName(path).Contains("forge", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Directory = path, Files = Directory.GetFiles(path, "*.json") })
            .Where(item => item.Files.Length > 0)
            .ToArray();
        var source = candidates.FirstOrDefault();
        if (source is null)
        {
            errors.Add($"未找到版本 {versionId} 的安装产物 JSON");
            return;
        }

        Directory.CreateDirectory(Path.Combine(minecraftFolder, "versions", versionId));
        var destination = Path.Combine(minecraftFolder, "versions", versionId, versionId + ".json");
        File.Copy(source.Files[0], destination, overwrite: true);
    }

    private async Task InstallLegacyAsync(
        ForgelikeLoaderVersion version,
        string minecraftFolder,
        string installerPath,
        List<string> errors,
        IProgress<ForgelikeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var displayName = version.Kind == ForgelikeKind.NeoForge ? "NeoForge" : "Forge";
        var loaderName = version.Kind == ForgelikeKind.NeoForge ? "neoforge" : "forge";
        var versionId = $"{loaderName}-{version.VersionName}";
        var versionFolder = Path.Combine(minecraftFolder, "versions", versionId);
        Directory.CreateDirectory(versionFolder);
        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Injector, versionId, 0, 1));

        try
        {
            var sourcePath = PickInstallerSource(minecraftFolder, installerPath);
            if (string.IsNullOrEmpty(sourcePath))
            {
                errors.Add($"旧版 {displayName} 安装器文件不可用：{installerPath}");
                progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Injector, versionId, 1, 1));
                return;
            }

            using var archive = ZipFile.OpenRead(sourcePath);
            var profileJson = ReadEntry(archive.GetEntry("install_profile.json")
                ?? throw new InvalidDataException("安装器缺少 install_profile.json"));
            var profile = JsonNode.Parse(profileJson) as JsonObject
                ?? throw new InvalidDataException("install_profile.json 解析失败");
            var install = profile["install"] as JsonObject;
            if (install is null)
            {
                var relativeJson = (profile["json"]?.GetValue<string>() ?? "").TrimStart('/');
                var versionEntry = archive.GetEntry(relativeJson)
                    ?? throw new InvalidDataException($"安装器缺少 {relativeJson}");
                var versionInfo = JsonNode.Parse(ReadEntry(versionEntry)) as JsonObject
                    ?? throw new InvalidDataException($"{relativeJson} 解析失败");
                versionInfo["id"] = versionId;

                Directory.CreateDirectory(versionFolder);
                await File.WriteAllTextAsync(
                        Path.Combine(versionFolder, versionId + ".json"),
                        versionInfo.ToJsonString(WriteOptions),
                        cancellationToken)
                    .ConfigureAwait(false);

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith("maven/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var relative = name["maven/".Length..];
                    var destination = Path.Combine(minecraftFolder, "libraries", relative);
                    if (!IsWithinLibraries(minecraftFolder, destination))
                    {
                        errors.Add($"支持库路径无效：{relative}");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var input = entry.Open();
                    await using var output = File.Create(destination);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                var path = install["path"]?.GetValue<string>()
                    ?? throw new InvalidDataException("install.path 缺失");
                var filePath = install["filePath"]?.GetValue<string>()
                    ?? throw new InvalidDataException("install.filePath 缺失");
                var destination = Path.Combine(minecraftFolder, "libraries", path);
                if (!IsWithinLibraries(minecraftFolder, destination))
                {
                    errors.Add($"支持库路径无效：{path}");
                }
                else
                {
                    var entry = archive.GetEntry(filePath)
                        ?? throw new InvalidDataException($"安装器缺少 {filePath}");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using var input = entry.Open();
                    await using var output = File.Create(destination);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                var versionInfo = profile["versionInfo"] as JsonObject
                    ?? throw new InvalidDataException("版本信息缺失");
                versionInfo["id"] = versionId;
                if (versionInfo["inheritsFrom"] is null)
                {
                    versionInfo["inheritsFrom"] = version.GameVersion;
                }

                Directory.CreateDirectory(versionFolder);
                await File.WriteAllTextAsync(
                        Path.Combine(versionFolder, versionId + ".json"),
                        versionInfo.ToJsonString(WriteOptions),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"旧版 {displayName} 安装失败：{ErrorMessageFormatter.Brief(ex)}");
        }

        progress?.Report(new ForgelikeInstallProgress(ForgelikeInstallStage.Injector, versionId, 1, 1));
    }

    private async Task<string?> DownloadLibraryAsync(
        LibraryJson library,
        string minecraftFolder,
        DownloadSource source,
        string loaderName,
        string inherit,
        string loaderVersion,
        CancellationToken cancellationToken)
    {
        if (!MinecraftRules.RulesMatch(library.Rules))
        {
            return null;
        }

        var artifact = library.Downloads?.Artifact;
        var relativePath = ResolveLibraryRelativePath(library.Name, artifact?.Path);
        if (relativePath is null)
        {
            return $"支持库路径无效:{library.Name}";
        }

        if (IsTargetJar(relativePath, loaderName, inherit, loaderVersion))
        {
            return null;
        }

        var destination = Path.Combine(minecraftFolder, "libraries", relativePath);
        if (!IsWithinLibraries(minecraftFolder, destination))
        {
            return $"支持库路径无效:{relativePath}";
        }

        try
        {
            var urls = BuildLibraryUrls(source, artifact?.Url, relativePath);
            if (urls.Count == 0)
            {
                return $"支持库 {Path.GetFileName(destination)} 下载地址缺失";
            }

            var request = new DownloadRequest(
                urls,
                destination,
                library.Name ?? Path.GetFileName(destination),
                artifact?.Size,
                artifact?.Sha1);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await _downloadClient.DownloadAsync(request, null, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"支持库 {Path.GetFileName(destination)} 下载失败：{ErrorMessageFormatter.Brief(ex)}";
        }
    }

    private static bool IsTargetJar(string path, string loaderName, string inherit, string loaderVersion)
    {
        var baseName = $"{loaderName}-{inherit}-{loaderVersion}.jar";
        return path.EndsWith(baseName, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith($"{loaderName}-{inherit}-{loaderVersion}-client.jar", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveLibraryRelativePath(string? name, string? artifactPath)
    {
        if (!string.IsNullOrWhiteSpace(artifactPath))
        {
            return artifactPath.TrimStart('/').Replace('\\', '/');
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var parts = name.Split(':');
        if (parts.Length is < 3 or > 4 || parts.Any(HasUnsafeSegment))
        {
            return null;
        }

        var group = parts[0];
        var artifact = parts[1];
        var version = parts[2];
        var fileVersion = parts.Length == 4 ? $"{version}-{parts[3]}" : version;
        var groupPath = group.Replace('.', '/');
        return $"{groupPath}/{artifact}/{version}/{artifact}-{fileVersion}.jar";
    }

    private static IReadOnlyList<string> BuildLibraryUrls(
        DownloadSource source,
        string? originalUrl,
        string relativePath)
    {
        var original = string.IsNullOrWhiteSpace(originalUrl)
            ? null
            : originalUrl.TrimEnd('/') + "/" + relativePath;
        if (source == DownloadSource.Bmclapi)
        {
            return string.IsNullOrWhiteSpace(original)
                ? [$"{BmclMavenBase}/{relativePath}"]
                : [$"{BmclMavenBase}/{relativePath}", original];
        }

        return string.IsNullOrWhiteSpace(original)
            ? []
            : [original];
    }

    private static IReadOnlyList<string> BuildInstallerUrls(
        ForgelikeLoaderVersion version,
        bool isNeoForge,
        string loaderName)
    {
        if (isNeoForge)
        {
            var package = version.GameVersion == "1.20.1" ? "forge" : "neoforge";
            var apiName = version.ApiName ?? version.VersionName;
            var fileName = $"{package}-{apiName}-installer.jar";
            var official = $"{NeoForgeMavenOfficialBase}/net/neoforged/{package}/{apiName}/{fileName}";
            return [$"{BmclMavenBase}/net/neoforged/{package}/{apiName}/{fileName}", official];
        }

        var escapedInherit = version.GameVersion.Replace('-', '_');
        var fileVersion = version.FileVersion ?? version.VersionName;
        var category = version.Category ?? "installer";
        var extension = category == "installer" ? "jar" : "zip";
        var fileNamePart = $"{escapedInherit}-{fileVersion}/forge-{escapedInherit}-{fileVersion}-{category}.{extension}";
        return
        [
            $"{BmclMavenBase}/net/minecraftforge/forge/{fileNamePart}",
            $"{ForgeMavenOfficialBase}/net/minecraftforge/forge/{fileNamePart}",
        ];
    }

    private static string? PickInstallerSource(string minecraftFolder, string downloadedInstaller)
    {
        if (File.Exists(downloadedInstaller) && IsZip(downloadedInstaller))
        {
            return downloadedInstaller;
        }

        var installerFolder = Path.Combine(minecraftFolder, "installer");
        if (!Directory.Exists(installerFolder))
        {
            return null;
        }

        return Directory
            .GetFiles(installerFolder, "*.jar")
            .FirstOrDefault(IsZip);
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string MergeJson(string installProfile, string? versionJson)
    {
        var profile = JsonNode.Parse(installProfile) as JsonObject;
        if (profile is null)
        {
            throw new InvalidDataException("install_profile.json 解析失败");
        }

        if (string.IsNullOrWhiteSpace(versionJson))
        {
            return profile.ToJsonString();
        }

        var version = JsonNode.Parse(versionJson) as JsonObject;
        if (version is null)
        {
            throw new InvalidDataException("version.json 解析失败");
        }

        Merge(profile, version);
        return profile.ToJsonString();
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            if (property.Value is JsonObject sourceObject
                && target[property.Key] is JsonObject targetObject)
            {
                Merge(targetObject, sourceObject);
            }
            else
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private static ForgelikeLoaderVersion ToForgeVersion(ForgeVersionEntryJson entry, string game)
    {
        var versionName = entry.Version!.Trim();
        var branch = entry.Branch;
        if (versionName is "11.15.1.2318" or "11.15.1.1902" or "11.15.1.1890")
        {
            branch = "1.8.9";
        }
        else if (string.IsNullOrWhiteSpace(branch)
            && game == "1.7.10"
            && Version.TryParse(versionName, out var parsed)
            && parsed.Revision >= 1300)
        {
            branch = "1.7.10";
        }

        string? category = null;
        string? hash = null;
        var priority = -1;
        foreach (var file in entry.Files ?? [])
        {
            var fileCategory = file.Category?.ToLowerInvariant();
            var format = file.Format?.ToLowerInvariant();
            if (fileCategory == "installer" && format == "jar" && priority < 2)
            {
                category = "installer";
                hash = file.Hash;
                priority = 2;
            }
            else if (fileCategory == "universal" && format == "zip" && priority <= 1)
            {
                category = "universal";
                hash = file.Hash;
                priority = 1;
            }
            else if (fileCategory == "client" && format == "zip" && priority <= 0)
            {
                category = "client";
                hash = file.Hash;
                priority = 0;
            }
        }

        var fileVersion = string.IsNullOrWhiteSpace(branch)
            ? versionName
            : $"{versionName}-{branch}";
        Version.TryParse(versionName, out var sortVersion);
        return new ForgelikeLoaderVersion(
            ForgelikeKind.Forge,
            versionName,
            game,
            false,
            sortVersion ?? new Version(0, 0, 0, 0),
            FileVersion: fileVersion,
            Category: category,
            Hash: hash);
    }

    private static ForgelikeLoaderVersion ToNeoForgeVersion(string apiName)
    {
        var isBeta = apiName.Contains("beta", StringComparison.OrdinalIgnoreCase)
            || apiName.Contains("alpha", StringComparison.OrdinalIgnoreCase);
        if (apiName.Contains("1.20.1", StringComparison.Ordinal))
        {
            var versionName = apiName.Replace("1.20.1-", "", StringComparison.Ordinal);
            Version.TryParse("19." + versionName, out var sortVersion);
            return new ForgelikeLoaderVersion(
                ForgelikeKind.NeoForge,
                versionName,
                "1.20.1",
                isBeta,
                sortVersion ?? new Version(19, 0, 0, 0),
                ApiName: apiName);
        }

        if (apiName.StartsWith("0.", StringComparison.Ordinal))
        {
            var segments = apiName.Split('-', 2)[0].Split('.');
            var lastSegment = segments.Length == 0 ? "0" : segments[^1];
            int.TryParse(lastSegment, out var build);
            var gameName = segments.Length > 1 ? segments[1] : "";
            return new ForgelikeLoaderVersion(
                ForgelikeKind.NeoForge,
                apiName,
                gameName,
                isBeta,
                new Version(0, 0, build),
                ApiName: apiName);
        }

        var beforeDash = apiName.Split('-', 2)[0];
        Version.TryParse(beforeDash, out var parsed);
        parsed ??= new Version(0, 0, 0, 0);
        string inherit;
        if (parsed.Major >= 24)
        {
            inherit = $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";
            if (inherit.EndsWith(".0", StringComparison.Ordinal))
            {
                inherit = inherit[..^2];
            }
        }
        else
        {
            inherit = $"1.{parsed.Major}.{parsed.Minor}";
        }

        var plusIndex = apiName.IndexOf('+');
        if (plusIndex >= 0)
        {
            inherit += "-" + apiName[(plusIndex + 1)..];
        }

        return new ForgelikeLoaderVersion(
            ForgelikeKind.NeoForge,
            apiName,
            inherit,
            isBeta,
            parsed,
            ApiName: apiName);
    }

    private static bool IsNewForgeVersion(string loaderVersion)
    {
        var firstSegment = loaderVersion.Split('.', 2)[0];
        return int.TryParse(firstSegment, out var major) && major >= 20;
    }

    private static bool HasUnsafeSegment(string segment)
    {
        return string.IsNullOrWhiteSpace(segment)
            || segment is "." or ".."
            || segment.Contains('/', StringComparison.Ordinal)
            || segment.Contains('\\', StringComparison.Ordinal)
            || segment.Contains("..", StringComparison.Ordinal);
    }

    private static bool IsWithinLibraries(string minecraftFolder, string destination)
    {
        var root = Path.GetFullPath(Path.Combine(minecraftFolder, "libraries"));
        var full = Path.GetFullPath(destination);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private sealed record ForgeVersionEntryJson(
        string? Version,
        string? Branch,
        string? Modified,
        IReadOnlyList<ForgeFileJson>? Files);

    private sealed record ForgeFileJson(string? Category, string? Format, string? Hash);
}
