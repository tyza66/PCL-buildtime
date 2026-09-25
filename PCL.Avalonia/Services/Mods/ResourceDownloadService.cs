using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public sealed class ResourceDownloadService : IResourceDownloadService
{
    private readonly IDownloadClient _downloadClient;

    public ResourceDownloadService(IDownloadClient downloadClient)
    {
        _downloadClient = downloadClient;
    }

    public async Task<string> InstallAsync(
        ResourceType type,
        ResourceFileItem file,
        string minecraftFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);
        if (string.IsNullOrWhiteSpace(file.Url) || string.IsNullOrWhiteSpace(file.FileName))
        {
            throw new InvalidOperationException("资源下载地址不完整");
        }

        var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new InvalidOperationException("资源文件名无效");
        }

        var targetFolder = Path.Combine(minecraftFolder, type.GetTargetFolder());
        Directory.CreateDirectory(targetFolder);
        var destinationPath = Path.Combine(targetFolder, fileName);
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
