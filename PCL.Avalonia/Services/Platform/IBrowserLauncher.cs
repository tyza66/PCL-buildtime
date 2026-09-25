namespace PCL.Avalonia.Services.Platform;

public interface IBrowserLauncher
{
    Task OpenAsync(string url, CancellationToken cancellationToken = default);
}
