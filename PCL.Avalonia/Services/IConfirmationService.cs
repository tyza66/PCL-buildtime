namespace PCL.Avalonia.Services;

/// <summary>
/// 不可逆操作（删版本之类）的统一确认入口：真实实现弹模态框，测试里换成同步返回的假实现。
/// </summary>
public interface IConfirmationService
{
    /// <summary>弹出确认框并等待用户决定；返回 false 表示取消，调用方必须什么都不做。</summary>
    Task<bool> ConfirmAsync(string title, string message);
}
