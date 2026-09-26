using System.IO.Compression;
using System.Text.Json;
using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public sealed class ModpackInstallerService : IModpackInstallerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly ICurseForgeApi _api;
    private readonly IDownloadClient _downloadClient;
    private readonly IVersionInstaller _versionInstaller;

    public ModpackInstallerService(
        ICurseForgeApi api,
        IDownloadClient downloadClient,
        IVersionInstaller versionInstaller)
    {
        _api = api;
        _downloadClient = downloadClient;
        _versionInstaller = versionInstaller;
    }

    public async Task<ModpackInstallResult> InstallAsync(
        string modpackZipPath,
        string minecraftFolder,
        DownloadSource source,
        IProgress<ModpackInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modpackZipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);
        if (!File.Exists(modpackZipPath))
        {
            throw new FileNotFoundException("整合包文件不存在", modpackZipPath);
        }

        progress?.Report(new ModpackInstallProgress(ModpackInstallStage.ReadingManifest, null, 0, 1, 0, null));
        ModpackManifest manifest;
        using (var archive = ZipFile.OpenRead(modpackZipPath))
        {
            manifest = ReadManifest(archive);
        }

        progress?.Report(new ModpackInstallProgress(ModpackInstallStage.ReadingManifest, manifest.Name, 1, 1, 0, null));
        var errors = new List<string>();
        var baseVersion = manifest.Minecraft.Version;
        if (string.IsNullOrWhiteSpace(baseVersion))
        {
            throw new InvalidOperationException("整合包 manifest 缺少 Minecraft 版本");
        }

        var baseJsonPath = Path.Combine(minecraftFolder, "versions", baseVersion, baseVersion + ".json");
        if (!File.Exists(baseJsonPath))
        {
            progress?.Report(new ModpackInstallProgress(ModpackInstallStage.BaseVersion, baseVersion, 0, 1, 0, null));
            var baseResult = await _versionInstaller.InstallAsync(
                    baseVersion,
                    null,
                    source,
                    minecraftFolder,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            errors.AddRange(baseResult.Errors.Select(item => $"原版 {baseVersion} 安装失败：{item}"));
            progress?.Report(new ModpackInstallProgress(ModpackInstallStage.BaseVersion, baseVersion, 1, 1, 0, null));
        }

        var requiredFiles = manifest.Files
            .Where(item => item.Required && item.ProjectId > 0 && item.FileId > 0)
            .ToList();
        var installedMods = new List<string>();
        var modsFolder = Path.Combine(minecraftFolder, "mods");
        for (var index = 0; index < requiredFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestFile = requiredFiles[index];
            progress?.Report(new ModpackInstallProgress(ModpackInstallStage.Mods, null, index, requiredFiles.Count, 0, null));
            try
            {
                var file = await _api.GetFileAsync(manifestFile.ProjectId, manifestFile.FileId, cancellationToken)
                    .ConfigureAwait(false);
                if (file is null || string.IsNullOrWhiteSpace(file.DownloadUrl) || string.IsNullOrWhiteSpace(file.FileName))
                {
                    errors.Add($"Mod {manifestFile.ProjectId}/{manifestFile.FileId} 下载地址不可用");
                    continue;
                }

                var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
                if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
                {
                    errors.Add($"Mod {file.Id} 文件名无效");
                    continue;
                }

                Directory.CreateDirectory(modsFolder);
                var destination = Path.Combine(modsFolder, fileName);
                if (!File.Exists(destination))
                {
                    var request = new DownloadRequest(
                        file.DownloadUrl,
                        destination,
                        fileName,
                        file.FileLength is 0 ? null : file.FileLength,
                        file.Sha1);
                    await _downloadClient.DownloadAsync(request, null, cancellationToken).ConfigureAwait(false);
                }

                installedMods.Add(fileName);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"Mod {manifestFile.ProjectId}/{manifestFile.FileId} 下载失败：{ErrorMessageFormatter.Brief(ex)}");
            }

            progress?.Report(new ModpackInstallProgress(ModpackInstallStage.Mods, null, index + 1, requiredFiles.Count, 0, null));
        }

        var overridesFolder = string.IsNullOrWhiteSpace(manifest.Overrides) ? "overrides" : manifest.Overrides;
        var extracted = 0;
        using (var archive = ZipFile.OpenRead(modpackZipPath))
        {
            var entries = archive.Entries
                .Where(entry => IsOverrideEntry(entry.FullName, overridesFolder))
                .ToList();
            progress?.Report(new ModpackInstallProgress(ModpackInstallStage.Overrides, null, 0, entries.Count, 0, null));
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = ToSafeRelativePath(entry.FullName, overridesFolder);
                if (relativePath is null)
                {
                    errors.Add($"整合包内存在不安全路径：{entry.FullName}");
                    continue;
                }

                var target = Path.Combine(minecraftFolder, relativePath);
                if (!IsWithin(minecraftFolder, target))
                {
                    errors.Add($"整合包内存在不安全路径：{entry.FullName}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var sourceStream = entry.Open();
                using var targetStream = File.Create(target);
                await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
                extracted++;
                progress?.Report(new ModpackInstallProgress(ModpackInstallStage.Overrides, relativePath, extracted, entries.Count, 0, null));
            }
        }

        progress?.Report(new ModpackInstallProgress(ModpackInstallStage.Complete, manifest.Name, 1, 1, 0, null));
        return new ModpackInstallResult(
            string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(modpackZipPath) : manifest.Name,
            baseVersion,
            installedMods,
            errors);
    }

    private static ModpackManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json");
        if (entry is null)
        {
            throw new InvalidOperationException("整合包缺少 manifest.json");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var manifest = JsonSerializer.Deserialize<ModpackManifest>(json, JsonOptions);
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Minecraft.Version))
        {
            throw new InvalidOperationException("整合包 manifest 格式无效");
        }

        return manifest;
    }

    private static bool IsOverrideEntry(string fullName, string overridesFolder)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = fullName.Replace('\\', '/');
        var prefix = overridesFolder.Replace('\\', '/').TrimEnd('/') + "/";
        return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ToSafeRelativePath(string fullName, string overridesFolder)
    {
        var normalized = fullName.Replace('\\', '/');
        var prefix = overridesFolder.Replace('\\', '/').TrimEnd('/') + "/";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = normalized[prefix.Length..];
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            return null;
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static bool IsWithin(string root, string path)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(rootFull, StringComparison.Ordinal);
    }
}
