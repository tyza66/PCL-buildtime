using System.Diagnostics;

namespace PCL.Avalonia.Services.Platform;

public sealed class DefaultBrowserLauncher : IBrowserLauncher
{
    public Task OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // 打不开浏览器时仍可在界面上显示验证地址和代码。
        }

        return Task.CompletedTask;
    }
}
