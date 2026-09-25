namespace PCL.Avalonia.Services;

public interface IUiDispatcher
{
    void Post(Action action);
}
