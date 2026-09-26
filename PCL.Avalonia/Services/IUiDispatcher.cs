namespace PCL.Avalonia.Services;

public interface IUiDispatcher
{
    void Post(Action action);

    // 防抖调度：同一 key 在 delay 内重复调用，只在最后一次之后触发一次，用于把频繁改动合并成一次落盘。
    void Debounce(string key, TimeSpan delay, Action action);
}
