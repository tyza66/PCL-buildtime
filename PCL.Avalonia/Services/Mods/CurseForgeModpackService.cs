using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Services.Mods;

public sealed class CurseForgeModpackService : ICurseForgeModpackService
{
    private const int ModpackClassId = 4471;
    private readonly ICurseForgeApi _api;
    private readonly IDownloadClient _downloadClient;

    public CurseForgeModpackService(ICurseForgeApi api, IDownloadClient downloadClient)
    {
        _api = api;
        _downloadClient = downloadClient;
    }

    public async Task<IReadOnlyList<CurseForgeProject>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var page = await _api.SearchProjectsAsync(
            query,
            ModpackClassId,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return page.Projects;
    }

    public async Task<string> InstallAsync(
        CurseForgeProject project,
        string gameVersion,
        string minecraftFolder,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftFolder);
        var files = await _api.GetModpackFilesAsync(project.Id, gameVersion, cancellationToken).ConfigureAwait(false);
        var file = files
            .OrderByDescending(item => item.FileDate ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        if (file is null || string.IsNullOrWhiteSpace(file.DownloadUrl) || string.IsNullOrWhiteSpace(file.FileName))
        {
            throw new InvalidOperationException("没有适配当前游戏版本的整合包");
        }

        var fileName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or "..")
        {
            throw new InvalidOperationException("整合包文件名无效");
        }

        var downloadsFolder = Path.Combine(minecraftFolder, "downloads");
        Directory.CreateDirectory(downloadsFolder);
        var destinationPath = Path.Combine(downloadsFolder, fileName);
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
