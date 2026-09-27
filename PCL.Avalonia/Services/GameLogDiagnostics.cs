using System.Text.RegularExpressions;

namespace PCL.Avalonia.Services;

/// <summary>
/// 实时识别游戏进程输出里的典型失败，趁进程还活着就把"出了什么事、去哪修"讲清楚。
/// 退出码要等进程死透才知道死因，而 JVM 级别的错误（类版本、内存、原生库、登录）
/// 在启动头几秒就打在输出里，这时给建议用户才来得及改。
/// </summary>
public static class GameLogDiagnostics
{
    /// <summary>
    /// 一条命中的诊断。<see cref="Key"/> 是稳定标识，调用方按它去重；
    /// <see cref="Advice"/> 是给用户看的中文建议，必须带上"可尝试"的下一步。
    /// </summary>
    public readonly record struct Diagnostic(string Key, string Advice);

    private sealed class Rule(string key, Regex[] signatures, string advice)
    {
        public string Key { get; } = key;
        public Regex[] Signatures { get; } = signatures;
        public string Advice { get; } = advice;
    }

    // 规则按特异性排序：先命中谁就报谁，避免"内存"这类宽泛词盖住精确病因。
    private static readonly Rule[] Rules =
    [
        new(
            "java-version",
            [
                new Regex(@"UnsupportedClassVersionError", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"has been compiled by a more recent version of the Java Runtime", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "检测到 Java 版本过低：有组件是用更高版本的 Java 编译的。可尝试：到设置页改用更高的 Java（如 Java 21），或删掉设置页里手填的 Java 路径让启动器按版本自动挑选"),
        new(
            "heap-reserve",
            [
                new Regex(@"Could not reserve enough space for (object|the) heap", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"Could not create the Java Virtual Machine", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"Error occurred during initialization of VM", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "JVM 无法启动：为游戏分配的内存可能超过本机可用量。可尝试：在启动页把内存调低一些；无效则到设置页重新扫描 Java，确认它不是损坏或架构不符的安装"),
        new(
            "oom",
            [
                new Regex(@"OutOfMemoryError", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"Java heap space", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "游戏内存溢出：分配给游戏的内存不够用。可尝试：在启动页把内存调高一些；同时关闭浏览器等占内存的程序，确认磁盘仍有可用空间"),
        new(
            "natives",
            [
                new Regex(@"UnsatisfiedLinkError", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"no lwjgl in java\.library\.path", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"Failed to load a library", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "原生组件加载失败：多半是 Java 架构与系统不匹配（Apple Silicon 上拿 x64 的 Java 跑 arm64 游戏就是这样死的）。可尝试：到设置页重新选择与系统架构一致的 Java；无效则删除该版本重新下载"),
        new(
            "corrupt",
            [
                new Regex(@"zip END header", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"ZipException", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"Corrupted (file|JSON|json)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "版本文件损坏：有文件没下载完整或已损坏。可尝试：删除该版本后到下载页重新下载；若反复出现，在设置页切换下载源"),
        new(
            "loader-unsupported",
            [
                new Regex(@"Incompatible (JVM|Java)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"JVM candidate", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"A JNI error has occurred", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "当前 Java 不被该 Mod 加载器支持：Forge / NeoForge / OptiFine 对 Java 版本挑剔。可尝试：到设置页换成加载器要求的 Java（Forge 1.16.5 及更早一般要 Java 8，新版要 Java 17 或更高）"),
        new(
            "mixin",
            [
                new Regex(@"Mixin apply failed", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"Mixin preparation failed", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "Mod 加载失败：常见于 Mod 互相冲突或 Mod 与当前版本不兼容。可尝试：到版本页禁用最近添加的 Mod 后逐个启用排查；必要时更新或移除出问题的 Mod"),
        new(
            "session",
            [
                new Regex(@"[Ii]nvalid session", RegexOptions.Compiled),
                new Regex(@"Failed to authenticate", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                new Regex(@"unable to validate", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            ],
            "登录状态失效：服务器拒绝了登录令牌。可尝试：到账号页重新登录后再启动游戏"),
        new(
            "main-exception",
            [
                new Regex(@"Exception in thread ""main""", RegexOptions.Compiled),
            ],
            "启动主线程抛出异常。可尝试：看启动日志最后几行里紧跟的类名与原因；确认 Java 版本符合要求；把 Mod 全部禁用后重试；仍不行删除该版本重新下载"),
    ];

    /// <summary>
    /// 识别一行输出。命不中文案就别硬编：返回 null 表示这行只是普通日志。
    /// </summary>
    public static Diagnostic? Match(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        foreach (var rule in Rules)
        {
            foreach (var signature in rule.Signatures)
            {
                if (signature.IsMatch(line))
                {
                    return new Diagnostic(rule.Key, rule.Advice);
                }
            }
        }

        return null;
    }
}
