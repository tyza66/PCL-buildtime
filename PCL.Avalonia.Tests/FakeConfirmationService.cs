using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

/// <summary>
/// 测试用的确认框：默认一律确认（保持既有删除用例语义），
/// 需要验证「取消就不删」时把 Result 置 false；Requests 记住每次弹窗的标题和正文。
/// </summary>
internal sealed class FakeConfirmationService : IConfirmationService
{
    public bool Result { get; set; } = true;

    public List<string> Requests { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message)
    {
        Requests.Add($"{title}\n{message}");
        return Task.FromResult(Result);
    }
}
