using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public sealed class ModsDownloadService : IModsDownloadService
{
    private readonly IDownloadClient _downloadClient;

    public ModsDownloadService(IDownloadClient downloadClient)
    {
        _downloadClient = downloadClient;
    }

    public async Task<string> InstallAsync(
        ModrinthProjectVersion version,
        string modsFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolder);
        var file = version.Files.FirstOrDefault(item => item.Primary) ?? version.Files.FirstOrDefault();
        if (file is null)
        {
            throw new InvalidOperationException("Mod 版本没有可下载的文件");
        }

        if (string.IsNullOrWhiteSpace(file.Url) || string.IsNullOrWhiteSpace(file.Filename))
        {
            throw new InvalidOperationException("Mod 下载地址不完整");
        }

        var fileName = Path.GetFileName(file.Filename.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new InvalidOperationException("Mod 文件名无效");
        }

        Directory.CreateDirectory(modsFolder);
        var destinationPath = Path.Combine(modsFolder, fileName);
        if (File.Exists(destinationPath))
        {
            return destinationPath;
        }

        var request = new DownloadRequest(
            file.Url,
            destinationPath,
            fileName,
            file.Size is 0 ? null : file.Size,
            file.Sha1);
        await _downloadClient.DownloadAsync(request, progress, cancellationToken).ConfigureAwait(false);
        return destinationPath;
    }
}
