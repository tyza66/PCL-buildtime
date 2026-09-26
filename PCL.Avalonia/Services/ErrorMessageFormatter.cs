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
            // 这两个是 IOException 的子类，必须排在它前面，否则下面的 IOException 会先把它们接走。
            FileNotFoundException => $"找不到文件：{OneLine(exception)}。可尝试：确认文件没有被删除或移动，或到设置页重新指定游戏目录",
            DirectoryNotFoundException => $"找不到目录：{OneLine(exception)}。可尝试：确认目录确实存在，或到设置页重新指定游戏目录",
            IOException => $"文件读写失败：{OneLine(exception)}。可尝试：检查磁盘剩余空间、确认文件没有被其他程序占用，或更换游戏目录",
            JsonException => "下载到的内容无法解析，可能是下载源数据异常。可尝试：在设置页切换到其他下载源后重试",
            // 业务层抛的中文异常已经写清了原因，按异常类型补一句对应的解决办法。
            // 原来只放行 InvalidOperationException，InvalidDataException、FormatException 这些
            // 同样带中文原因的就被扣上"未预期错误"的帽子，用户反而不知道该怎么办。
            _ when IsChinese(exception.Message) => WithAdvice(exception),
            _ => $"发生了未预期的错误：{OneLine(exception)}。可尝试：重启启动器后重试；若反复出现，请在设置页更换下载源或更新启动器",
        };

    /// <summary>中文业务消息多半只说了"出了什么事"，这里按类型补一句"接下来怎么办"。</summary>
    private static string WithAdvice(Exception exception)
    {
        var message = OneLine(exception).TrimEnd('.', '。');
        // 消息里已经带了"请刷新列表"这类指引就别再啰嗦，否则同一句会出现两条建议。
        if (message.Contains("可尝试") || message.Contains("请") || message.Contains("试试"))
        {
            return message;
        }

        var advice = AdviceFor(exception);
        return advice.Length == 0 ? message : $"{message}。{advice}";
    }

    private static string AdviceFor(Exception exception)
        => exception switch
        {
            InvalidDataException => "可尝试：在设置页切换到其他下载源后重试，整合包可重新下载一次",
            NotSupportedException => "可尝试：把游戏目录换到不含特殊字符或空格的路径",
            FormatException or ArgumentException => "可尝试：检查上方填写的内容格式后重试",
            _ => "可尝试：重试该操作，或在设置页切换到其他下载源",
        };

    /// <summary>错误列表场景用：短标签，避免整段建议把列表撑爆。</summary>
    public static string Brief(Exception exception)
        => exception switch
        {
            TaskCanceledException or TimeoutException => "连接超时",
            OperationCanceledException => "已取消",
            HttpRequestException => "网络异常",
            UnauthorizedAccessException => "没有写入权限",
            FileNotFoundException => "文件缺失",
            DirectoryNotFoundException => "目录缺失",
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
