using System.Text.Json;

namespace PCL.Avalonia.Services;

/// <summary>
/// 把内部异常翻译成用户能看懂、也知道下一步怎么办的中文。裸 <c>ex.Message</c> 经常是
/// 英文堆栈或一句 "A task was canceled"，用户看到也只能干瞪眼，这里统一补上"可尝试"的建议。
/// 业务层自己抛的中文 <see cref="InvalidOperationException"/> 原样透传，不重复包装。
/// </summary>
public static class ErrorMessageFormatter
{
    /// <summary>单行状态展示用：原因 + 一句可操作的修复建议。</summary>
    public static string Describe(Exception exception)
        => exception switch
        {
            TaskCanceledException => "连接超时。可尝试：检查网络是否稳定，稍后重试，或在设置页切换到其他下载源",
            TimeoutException => "连接超时。可尝试：检查网络是否稳定，稍后重试，或在设置页切换到其他下载源",
            OperationCanceledException => "操作已取消。可尝试：重新执行该操作",
            HttpRequestException => $"网络请求失败：{OneLine(exception)}。可尝试：检查网络连接与代理设置，或在设置页切换到其他下载源",
            UnauthorizedAccessException => $"没有足够的系统权限：{OneLine(exception)}。可尝试：以管理员身份运行启动器、更换有写入权限的游戏目录，或临时关闭杀毒软件",
            IOException => $"文件读写失败：{OneLine(exception)}。可尝试：检查磁盘剩余空间、确认文件没有被其他程序占用，或更换游戏目录",
            JsonException => "下载到的内容无法解析，可能是下载源数据异常。可尝试：在设置页切换到其他下载源后重试",
            InvalidOperationException when IsChinese(exception.Message) => exception.Message,
            _ => $"发生了未预期的错误：{OneLine(exception)}。可尝试：重启启动器后重试；若反复出现，请在设置页更换下载源或更新启动器",
        };

    /// <summary>错误列表场景用：短标签，避免整段建议把列表撑爆。</summary>
    public static string Brief(Exception exception)
        => exception switch
        {
            TaskCanceledException or TimeoutException => "连接超时",
            OperationCanceledException => "已取消",
            HttpRequestException => "网络异常",
            UnauthorizedAccessException => "没有写入权限",
            IOException => "磁盘或文件访问异常",
            JsonException => "下载源数据解析失败",
            _ => IsChinese(exception.Message) ? ShortChinese(exception.Message) : "未知错误",
        };

    private static string OneLine(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message.Trim();
        var lineBreak = message.IndexOfAny(['\r', '\n']);
        if (lineBreak >= 0)
        {
            message = message[..lineBreak].TrimEnd();
        }

        return message.Length > 120 ? message[..120] + "..." : message;
    }

    private static string ShortChinese(string message)
    {
        var text = message.Trim();
        var stop = text.IndexOfAny(['。', '，', ',', '；', ';']);
        return stop > 0 ? text[..stop] : text.Length <= 60 ? text : text[..60] + "...";
    }

    private static bool IsChinese(string message)
        => !string.IsNullOrWhiteSpace(message) && message.Any(c => c >= '\u4e00' && c <= '\u9fff');
}
