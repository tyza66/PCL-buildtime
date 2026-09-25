using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PCL.Avalonia.Services.Downloads;

public sealed class HttpDownloadClient : IDownloadClient
{
    private readonly HttpClient _httpClient;

    public HttpDownloadClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public async Task DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Exception? lastError = null;
        foreach (var url in request.Urls)
        {
            try
            {
                await DownloadFromUrlAsync(request, url, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                CleanupTemporaryFile(request.DestinationPath);
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                CleanupTemporaryFile(request.DestinationPath);
            }
        }

        throw lastError ?? new InvalidOperationException("没有可用的下载地址");
    }

    public async Task<string> GetStringAsync(
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);
        if (urls.Count == 0)
        {
            throw new ArgumentException("至少需要一个下载地址", nameof(urls));
        }

        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("没有可用的下载地址");
    }

    private async Task DownloadFromUrlAsync(
        DownloadRequest request,
        string url,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(request.DestinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = request.DestinationPath + ".tmp";
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var totalLength = response.Content.Headers.ContentLength;
        await using var destination = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        var buffer = new byte[81920];
        var received = 0L;
        using var sha1 = request.ExpectedSha1 is null
            ? null
            : IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            sha1?.AppendData(buffer.AsSpan(0, read));
            progress?.Report(new DownloadProgress(received, totalLength));
        }

        if (request.ExpectedSize is not null && received != request.ExpectedSize.Value)
        {
            throw new InvalidDataException($"文件大小不匹配：期望 {request.ExpectedSize} 字节，实际 {received} 字节");
        }

        if (sha1 is not null)
        {
            var actualSha1 = Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();
            if (!string.Equals(actualSha1, request.ExpectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-1 不匹配：期望 {request.ExpectedSha1}，实际 {actualSha1}");
            }
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, request.DestinationPath, overwrite: true);
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PCL2.Avalonia", "1.0"));
        return client;
    }

    private static void CleanupTemporaryFile(string destinationPath)
    {
        var temporaryPath = destinationPath + ".tmp";
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
