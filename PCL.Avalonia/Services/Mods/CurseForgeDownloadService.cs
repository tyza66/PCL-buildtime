using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public sealed class CurseForgeDownloadService : ICurseForgeDownloadService
{
    private readonly IDownloadClient _downloadClient;

    public CurseForgeDownloadService(IDownloadClient downloadClient)
    {
        _downloadClient = downloadClient;
    }

    public async Task<string> InstallAsync(
        CurseForgeModFile file,
        string modsFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolder);
        if (string.IsNullOrWhiteSpace(file.DownloadUrl) || string.IsNullOrWhiteSpace(file.FileName))
        {
            throw new InvalidOperationException("Mod 下载地址不完整");
        }

        var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
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
            file.DownloadUrl,
            destinationPath,
            fileName,
            file.FileLength is 0 ? null : file.FileLength,
            file.Sha1);
        await _downloadClient.DownloadAsync(request, progress, cancellationToken).ConfigureAwait(false);
        return destinationPath;
    }
}
