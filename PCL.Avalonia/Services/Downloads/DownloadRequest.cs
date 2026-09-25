namespace PCL.Avalonia.Services.Downloads;

public sealed record DownloadRequest
{
    public DownloadRequest(
        string url,
        string destinationPath,
        string? name = null,
        long? expectedSize = null,
        string? expectedSha1 = null)
        : this([url], destinationPath, name, expectedSize, expectedSha1)
    {
    }

    public DownloadRequest(
        IReadOnlyList<string> urls,
        string destinationPath,
        string? name = null,
        long? expectedSize = null,
        string? expectedSha1 = null)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (urls.Count == 0 || urls.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("至少需要一个有效下载地址", nameof(urls));
        }

        Urls = urls;
        DestinationPath = destinationPath;
        Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(destinationPath) : name;
        ExpectedSize = expectedSize;
        ExpectedSha1 = expectedSha1;
    }

    public IReadOnlyList<string> Urls { get; }

    public string DestinationPath { get; }

    public string Name { get; }

    public long? ExpectedSize { get; }

    public string? ExpectedSha1 { get; }
}
