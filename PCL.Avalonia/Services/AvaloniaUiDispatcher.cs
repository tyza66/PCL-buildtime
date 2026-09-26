using Avalonia.Threading;

namespace PCL.Avalonia.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    private readonly Dictionary<string, System.Threading.Timer> _timers = [];
    private readonly object _gate = new();

    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public void Debounce(string key, TimeSpan delay, Action action)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(key, out var existing))
            {
                existing.Change(delay, Timeout.InfiniteTimeSpan);
                return;
            }

            // 一次性定时器：到点后把真正要做的事丢回 UI 线程执行，避免在后台线程碰界面状态。
            var timer = new System.Threading.Timer(
                _ => Dispatcher.UIThread.Post(action),
                null,
                delay,
                Timeout.InfiniteTimeSpan);
            _timers.Add(key, timer);
        }
    }
}
