namespace PCL.Avalonia.Services.Minecraft;

public interface IGameLaunch : IDisposable
{
    int ProcessId { get; }

    bool HasExited { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken = default);

    void Kill();
}
