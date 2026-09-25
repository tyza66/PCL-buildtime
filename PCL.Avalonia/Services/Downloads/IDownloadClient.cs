namespace PCL.Avalonia.Services.Downloads;

public interface IDownloadClient
{
    Task DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<string> GetStringAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default);
}
