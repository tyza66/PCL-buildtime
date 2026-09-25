namespace PCL.Avalonia.ViewModels;

/// <summary>
/// 页面被导航切入时调用，用于按需加载数据，避免启动时全部预取。
/// </summary>
public interface IPageActivatable
{
    Task OnActivatedAsync();
}
