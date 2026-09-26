using System.Text.Json;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class FabricLoaderService : IFabricLoaderService
{
    private const string BmclMetaBase = "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader";
    private const string OfficialMetaBase = "https://meta.fabricmc.net/v2/versions/loader";
    private const string BmclMavenBase = "https://bmclapi2.bangbang93.com/maven";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDownloadClient _downloadClient;
    private readonly IVersionInstaller _versionInstaller;

    public FabricLoaderService(
        IDownloadClient downloadClient,
        IVersionInstaller versionInstaller)
    {
        _downloadClient = downloadClient;
        _versionInstaller = versionInstaller;
    }

    public async Task<IReadOnlyList<FabricLoaderVersion>> GetVersionsAsync(
        string gameVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        var game = gameVersion.Trim();
        var json = await _downloadClient
            .GetStringAsync(
                [
                    $"{BmclMetaBase}/{Escape(game)}",
                    $"{OfficialMetaBase}/{Escape(game)}",
                ],
                cancellationToken)
            .ConfigureAwait(false);
        var entries = JsonSerializer.Deserialize<List<FabricMetaEntryJson>>(json, JsonOptions) ?? [];
        return entries
            .Where(entry => entry.Loader is not null && !string.IsNullOrWhiteSpace(entry.Loader.Version))
            .Select(entry => new FabricLoaderVersion(
                entry.Loader!.Version!,
                game,
                entry.Loader.Stable,
                entry.LauncherMeta?.MinJavaVersion ?? 0))
            .OrderByDescending(version => version.IsStable)
            .ThenByDescending(version => version.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<FabricInstallResult> InstallAsync(
        string gameVersion,
        string loaderVersion,
        string minecraftFolder,
        DownloadSource source,
        IProgress<FabricInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);

        var game = gameVersion.Trim();
        var loader = loaderVersion.Trim();
        var errors = new List<string>();

        progress?.Report(new FabricInstallProgress(FabricInstallStage.BaseVersion, game, 0, 1));
        var baseJsonPath = Path.Combine(minecraftFolder, "versions", game, game + ".json");
        if (!File.Exists(baseJsonPath))
        {
            var baseResult = await _versionInstaller
                .InstallAsync(game, null, source, minecraftFolder, null, cancellationToken)
                .ConfigureAwait(false);
            errors.AddRange(baseResult.Errors.Select(item => $"原版 {game} 安装失败：{item}"));
        }

        progress?.Report(new FabricInstallProgress(FabricInstallStage.BaseVersion, game, 1, 1));

        string profileJson;
        try
        {
            var profileUrls = GetProfileUrls(game, loader);
            progress?.Report(new FabricInstallProgress(FabricInstallStage.ProfileJson, loader, 0, 1));
            profileJson = await _downloadClient
                .GetStringAsync(profileUrls, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new FabricInstallProgress(FabricInstallStage.ProfileJson, loader, 1, 1));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"Fabric 版本资料下载失败：{ErrorMessageFormatter.Brief(ex)}");
            progress?.Report(new FabricInstallProgress(FabricInstallStage.Complete, loader, 1, 1));
            return new FabricInstallResult($"fabric-loader-{loader}-{game}", game, loader, errors);
        }

        if (string.IsNullOrWhiteSpace(profileJson))
        {
            errors.Add("Fabric 版本资料为空");
            progress?.Report(new FabricInstallProgress(FabricInstallStage.Complete, loader, 1, 1));
            return new FabricInstallResult($"fabric-loader-{loader}-{game}", game, loader, errors);
        }

        FabricProfileJson? profile;
        try
        {
            profile = JsonSerializer.Deserialize<FabricProfileJson>(profileJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add("Fabric 版本资料解析失败：" + ErrorMessageFormatter.Brief(ex));
            progress?.Report(new FabricInstallProgress(FabricInstallStage.Complete, loader, 1, 1));
            return new FabricInstallResult($"fabric-loader-{loader}-{game}", game, loader, errors);
        }

        if (profile is null || string.IsNullOrWhiteSpace(profile.Id))
        {
            errors.Add("Fabric 版本资料缺少版本 ID");
            progress?.Report(new FabricInstallProgress(FabricInstallStage.Complete, loader, 1, 1));
            return new FabricInstallResult($"fabric-loader-{loader}-{game}", game, loader, errors);
        }

        var versionId = profile.Id;
        var versionFolder = Path.Combine(minecraftFolder, "versions", versionId);
        Directory.CreateDirectory(versionFolder);
        var versionJsonPath = Path.Combine(versionFolder, versionId + ".json");
        File.WriteAllText(versionJsonPath, profileJson);

        var libraries = profile.Libraries ?? [];
        progress?.Report(new FabricInstallProgress(FabricInstallStage.Libraries, null, 0, libraries.Count));
        for (var index = 0; index < libraries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var library = libraries[index];
            var relativePath = ResolveLibraryRelativePath(library.Name);
            if (relativePath is null)
            {
                errors.Add($"支持库路径无效：{library.Name}");
                progress?.Report(new FabricInstallProgress(
                    FabricInstallStage.Libraries,
                    library.Name,
                    index + 1,
                    libraries.Count));
                continue;
            }

            var destination = Path.Combine(minecraftFolder, "libraries", relativePath);
            if (!IsWithinLibraries(minecraftFolder, destination))
            {
                errors.Add($"支持库路径无效：{library.Name}");
                progress?.Report(new FabricInstallProgress(
                    FabricInstallStage.Libraries,
                    library.Name,
                    index + 1,
                    libraries.Count));
                continue;
            }

            try
            {
                var urls = BuildLibraryUrls(source, library.Url, relativePath);
                var request = new DownloadRequest(
                    urls,
                    destination,
                    library.Name,
                    library.Size,
                    library.Sha1);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await _downloadClient.DownloadAsync(request, null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"支持库 {Path.GetFileName(destination)} 下载失败：{ErrorMessageFormatter.Brief(ex)}");
            }

            progress?.Report(new FabricInstallProgress(
                FabricInstallStage.Libraries,
                library.Name,
                index + 1,
                libraries.Count));
        }

        progress?.Report(new FabricInstallProgress(FabricInstallStage.Complete, versionId, 1, 1));
        return new FabricInstallResult(versionId, game, loader, errors);
    }

    private static IReadOnlyList<string> GetProfileUrls(string gameVersion, string loaderVersion)
    {
        var game = Escape(gameVersion);
        var loader = Escape(loaderVersion);
        return
        [
            $"{BmclMetaBase}/{game}/{loader}/profile/json",
            $"{OfficialMetaBase}/{game}/{loader}/profile/json",
        ];
    }

    private static IReadOnlyList<string> BuildLibraryUrls(
        DownloadSource source,
        string? baseUrl,
        string relativePath)
    {
        var original = JoinUrl(baseUrl, relativePath);
        if (source == DownloadSource.Bmclapi)
        {
            return string.IsNullOrWhiteSpace(original)
                ? [$"{BmclMavenBase}/{relativePath}"]
                : [$"{BmclMavenBase}/{relativePath}", original];
        }

        if (string.IsNullOrWhiteSpace(original))
        {
            throw new InvalidOperationException("Fabric 支持库缺少原版下载地址");
        }

        return [original];
    }

    private static string? JoinUrl(string? baseUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return baseUrl.TrimEnd('/') + "/" + relativePath;
    }

    private static string? ResolveLibraryRelativePath(string? name)
    {
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

    private static string Escape(string value)
    {
        return Uri.EscapeDataString(value);
    }

    private sealed record FabricMetaEntryJson(
        FabricMetaLoaderJson? Loader,
        FabricMetaLauncherJson? LauncherMeta);

    private sealed record FabricMetaLoaderJson(string? Version, bool Stable);

    private sealed record FabricMetaLauncherJson(
        [property: System.Text.Json.Serialization.JsonPropertyName("min_java_version")]
        int? MinJavaVersion);

    private sealed record FabricProfileJson(
        string? Id,
        string? InheritsFrom,
        string? Type,
        string? MainClass,
        IReadOnlyList<FabricProfileLibraryJson>? Libraries);

    private sealed record FabricProfileLibraryJson(
        string? Name,
        string? Url,
        string? Sha1,
        long? Size);
}
