using Avalonia.Threading;

namespace PCL.Avalonia.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}
