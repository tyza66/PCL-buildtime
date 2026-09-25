using System.Text.Json;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services.Downloads;

public sealed class VersionInstaller : IVersionInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDownloadClient _downloadClient;
    private readonly IVersionCatalogService _catalog;
    private readonly DownloadUrlResolver _urlResolver;

    public VersionInstaller(
        IDownloadClient downloadClient,
        IVersionCatalogService catalog,
        DownloadUrlResolver? urlResolver = null)
    {
        _downloadClient = downloadClient;
        _catalog = catalog;
        _urlResolver = urlResolver ?? new DownloadUrlResolver();
    }

    public async Task<VersionInstallResult> InstallAsync(
        string versionId,
        VersionManifestEntry? entry,
        DownloadSource source,
        string minecraftFolder,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);

        var errors = new List<string>();
        var chain = await LoadChainAsync(
                versionId,
                entry,
                source,
                minecraftFolder,
                errors,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (chain.Count > 0)
        {
            await DownloadRootJarAsync(
                    chain[^1],
                    source,
                    minecraftFolder,
                    errors,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            var libraries = ResolveLibraries(chain, minecraftFolder);
            await DownloadLibrariesAsync(
                    libraries,
                    source,
                    minecraftFolder,
                    errors,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            var assetIndexOwner = ResolveAssetIndexOwner(chain);
            if (assetIndexOwner is not null)
            {
                await DownloadAssetsAsync(
                        versionId,
                        assetIndexOwner,
                        source,
                        minecraftFolder,
                        errors,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        progress?.Report(new InstallProgress(InstallStage.Complete, versionId, 1, 1, 0, null));
        return new VersionInstallResult(versionId, errors);
    }

    private async Task<List<ChainEntry>> LoadChainAsync(
        string versionId,
        VersionManifestEntry? entry,
        DownloadSource source,
        string minecraftFolder,
        List<string> errors,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new List<ChainEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var id = versionId;
        var currentEntry = entry;

        while (!string.IsNullOrWhiteSpace(id) && visited.Add(id))
        {
            var json = _catalog.LoadJson(minecraftFolder, id);
            if (json is null)
            {
                try
                {
                    var urls = _urlResolver.GetVersionJsonUrls(source, currentEntry, id);
                    var destination = Path.Combine(minecraftFolder, "versions", id, id + ".json");
                    var request = new DownloadRequest(
                        urls,
                        destination,
                        id + ".json",
                        expectedSha1: currentEntry?.Sha1);
                    progress?.Report(new InstallProgress(InstallStage.VersionJson, id, 0, 1, 0, null));
                    await DownloadFileAsync(request, InstallStage.VersionJson, progress, cancellationToken)
                        .ConfigureAwait(false);
                    progress?.Report(new InstallProgress(InstallStage.VersionJson, id, 1, 1, 0, null));
                    json = _catalog.LoadJson(minecraftFolder, id);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add($"版本 {id} JSON 下载失败：{ex.Message}");
                    break;
                }
            }

            if (json is null)
            {
                errors.Add($"版本 {id} 的版本数据不可用");
                break;
            }

            result.Add(new ChainEntry(id, json));
            id = json.InheritsFrom;
            currentEntry = null;
        }

        return result;
    }

    private async Task DownloadRootJarAsync(
        ChainEntry root,
        DownloadSource source,
        string minecraftFolder,
        List<string> errors,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rootId = root.Json.Id ?? root.VersionId;
        var jarId = root.Json.Jar ?? rootId;
        var destination = Path.Combine(minecraftFolder, "versions", rootId, jarId + ".jar");
        var artifact = root.Json.Downloads?.Client;
        if (FileIsValid(destination, artifact?.Size, artifact?.Sha1))
        {
            return;
        }

        try
        {
            var urls = _urlResolver.GetClientJarUrls(source, rootId, artifact?.Url);
            var request = new DownloadRequest(
                urls,
                destination,
                jarId + ".jar",
                artifact?.Size,
                artifact?.Sha1);
            progress?.Report(new InstallProgress(InstallStage.VersionJar, jarId, 0, 1, 0, null));
            await DownloadFileAsync(request, InstallStage.VersionJar, progress, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new InstallProgress(InstallStage.VersionJar, jarId, 1, 1, 0, null));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"客户端 {jarId} 下载失败：{ex.Message}");
        }
    }

    private static IReadOnlyList<LibraryDownloadEntry> ResolveLibraries(
        IReadOnlyList<ChainEntry> chain,
        string minecraftFolder)
    {
        var byPath = new Dictionary<string, LibraryDownloadEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in chain)
        {
            foreach (var library in entry.Json.Libraries)
            {
                if (!MinecraftRules.RulesMatch(library.Rules))
                {
                    continue;
                }

                var nativeTemplate = MinecraftRules.ResolveNativeTemplate(library);
                var classifier = nativeTemplate is null
                    ? null
                    : MinecraftRules.ResolveNativeClassifier(nativeTemplate);
                var artifact = nativeTemplate is null
                    ? library.Downloads?.Artifact
                    : library.Downloads?.Classifiers?.GetValueOrDefault(classifier ?? "");
                var path = ResolveLibraryPath(minecraftFolder, library, classifier, artifact?.Path);
                if (byPath.ContainsKey(path))
                {
                    continue;
                }

                byPath[path] = new LibraryDownloadEntry(
                    library.Name ?? Path.GetFileName(path),
                    path,
                    artifact?.Url,
                    artifact?.Size,
                    artifact?.Sha1);
            }
        }

        return [.. byPath.Values];
    }

    private async Task DownloadLibrariesAsync(
        IReadOnlyList<LibraryDownloadEntry> libraries,
        DownloadSource source,
        string minecraftFolder,
        List<string> errors,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var total = libraries.Count;
        progress?.Report(new InstallProgress(InstallStage.Libraries, null, 0, total, 0, null));
        for (var index = 0; index < libraries.Count; index++)
        {
            var library = libraries[index];
            cancellationToken.ThrowIfCancellationRequested();
            if (FileIsValid(library.Path, library.ExpectedSize, library.ExpectedSha1))
            {
                progress?.Report(new InstallProgress(InstallStage.Libraries, library.Name, index + 1, total, 0, null));
                continue;
            }

            try
            {
                var relativePath = Path.GetRelativePath(minecraftFolder, library.Path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                var artifactPath = relativePath.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase)
                    ? relativePath["libraries/".Length..]
                    : relativePath;
                var urls = _urlResolver.GetLibraryUrls(source, library.OriginalUrl, artifactPath);
                var request = new DownloadRequest(
                    urls,
                    library.Path,
                    library.Name,
                    library.ExpectedSize,
                    library.ExpectedSha1);
                await DownloadFileAsync(request, InstallStage.Libraries, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"支持库 {library.Name} 下载失败：{ex.Message}");
            }

            progress?.Report(new InstallProgress(InstallStage.Libraries, library.Name, index + 1, total, 0, null));
        }
    }

    private static ChainEntry? ResolveAssetIndexOwner(IReadOnlyList<ChainEntry> chain)
    {
        for (var index = chain.Count - 1; index >= 0; index--)
        {
            var assetIndex = chain[index].Json.AssetIndex;
            if (assetIndex is not null && !string.IsNullOrWhiteSpace(assetIndex.Url))
            {
                return chain[index];
            }
        }

        for (var index = chain.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(chain[index].Json.Assets))
            {
                return chain[index];
            }
        }

        return null;
    }

    private async Task DownloadAssetsAsync(
        string fallbackVersionId,
        ChainEntry owner,
        DownloadSource source,
        string minecraftFolder,
        List<string> errors,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var assetIndex = owner.Json.AssetIndex;
        if (assetIndex is null || string.IsNullOrWhiteSpace(assetIndex.Url))
        {
            return;
        }

        var indexName = assetIndex.Id
            ?? owner.Json.Assets
            ?? owner.Json.Id
            ?? fallbackVersionId;
        var indexDestination = Path.Combine(minecraftFolder, "assets", "indexes", indexName + ".json");
        if (!FileIsValid(indexDestination, assetIndex.Size, assetIndex.Sha1))
        {
            try
            {
                var urls = _urlResolver.GetAssetIndexUrls(source, assetIndex.Url, assetIndex.Sha1);
                var request = new DownloadRequest(
                    urls,
                    indexDestination,
                    indexName + ".json",
                    assetIndex.Size,
                    assetIndex.Sha1);
                progress?.Report(new InstallProgress(InstallStage.AssetsIndex, indexName, 0, 1, 0, null));
                await DownloadFileAsync(request, InstallStage.AssetsIndex, progress, cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(new InstallProgress(InstallStage.AssetsIndex, indexName, 1, 1, 0, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"资源索引 {indexName} 下载失败：{ex.Message}");
                return;
            }
        }

        AssetsIndexJson? indexJson;
        try
        {
            indexJson = JsonSerializer.Deserialize<AssetsIndexJson>(
                File.ReadAllText(indexDestination),
                JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            errors.Add($"资源索引 {indexName} 解析失败：{ex.Message}");
            return;
        }

        if (indexJson is null)
        {
            errors.Add($"资源索引 {indexName} 为空");
            return;
        }

        var objects = indexJson.Objects
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Hash))
            .Select(pair => new AssetDownloadEntry(pair.Key, pair.Value.Hash!, pair.Value.Size))
            .ToList();
        var total = objects.Count;
        progress?.Report(new InstallProgress(InstallStage.Assets, null, 0, total, 0, null));
        for (var index = 0; index < objects.Count; index++)
        {
            var asset = objects[index];
            cancellationToken.ThrowIfCancellationRequested();
            var hash = asset.Hash;
            var destination = Path.Combine(
                minecraftFolder,
                "assets",
                "objects",
                hash[..2],
                hash);
            if (FileIsValid(destination, asset.Size, asset.Hash))
            {
                progress?.Report(new InstallProgress(InstallStage.Assets, asset.Name, index + 1, total, 0, null));
                continue;
            }

            try
            {
                var urls = _urlResolver.GetAssetUrls(source, hash);
                var request = new DownloadRequest(
                    urls,
                    destination,
                    asset.Name,
                    asset.Size,
                    asset.Hash);
                await DownloadFileAsync(request, InstallStage.Assets, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"资源 {asset.Name} 下载失败：{ex.Message}");
            }

            progress?.Report(new InstallProgress(InstallStage.Assets, asset.Name, index + 1, total, 0, null));
        }
    }

    private async Task DownloadFileAsync(
        DownloadRequest request,
        InstallStage stage,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        IProgress<DownloadProgress>? downloadProgress = progress is null
            ? null
            : new Progress<DownloadProgress>(value =>
            {
                progress.Report(new InstallProgress(
                    stage,
                    request.Name,
                    0,
                    0,
                    value.Received,
                    value.TotalLength));
            });
        await _downloadClient
            .DownloadAsync(request, downloadProgress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolveLibraryPath(
        string minecraftFolder,
        LibraryJson library,
        string? classifier,
        string? artifactPath)
    {
        var librariesRoot = Path.Combine(minecraftFolder, "libraries");
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

    private static bool FileIsValid(string path, long? expectedSize, string? expectedSha1)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedSha1))
        {
            return string.Equals(ComputeSha1(path), expectedSha1, StringComparison.OrdinalIgnoreCase);
        }

        return expectedSize is null || new FileInfo(path).Length == expectedSize.Value;
    }

    private static string ComputeSha1(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        return Convert.ToHexString(sha1.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed record ChainEntry(string VersionId, MinecraftVersionJson Json);

    private sealed record LibraryDownloadEntry(
        string Name,
        string Path,
        string? OriginalUrl,
        long? ExpectedSize,
        string? ExpectedSha1);

    private sealed record AssetDownloadEntry(string Name, string Hash, long? Size);
}
