using System.Diagnostics;

namespace PCL.Avalonia.Services.Minecraft;

public sealed class GameLaunch : IDisposable
{
    private readonly Process _process;

    internal GameLaunch(Process process)
    {
        _process = process;
    }

    public int ProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
