namespace PCL.Avalonia.Services;

/// <summary>
/// 解读游戏进程的退出码。退出码本身对玩家没有意义，而 Minecraft 崩在 JVM 里时最常见的诱因
/// 就那几样（Mod 冲突、Java 版本或架构不对、内存不足、显卡驱动旧），按惯例 Unix 下
/// 128+信号 可以反推是哪种死法，这里给出中文判断和下一步去哪看、怎么修。
/// </summary>
public static class GameExitDiagnostics
{
    /// <summary>日志行：只陈述事实，不带判断。</summary>
    public static string LogLine(int exitCode) => $"游戏进程已退出（退出码 {exitCode}）";

    /// <summary>状态栏短标签，避免一整段建议把状态行撑爆。</summary>
    public static string Brief(int exitCode) => exitCode switch
    {
        0 => "正常退出",
        -1 => "退出状态未知",
        1 => "游戏崩溃",
        132 => "Java 架构不匹配",
        134 => "JVM 内部错误",
        136 => "显卡驱动问题",
        137 => "内存不足被结束",
        139 => "游戏段错误",
        _ => "异常退出",
    };

    /// <summary>
    /// 完整排查建议：判断原因，并说明去哪看日志、可以先动哪一格。
    /// 正常退出返回空字符串——好消息不需要建议。
    /// </summary>
    public static string Describe(int exitCode) => exitCode switch
    {
        0 => "",
        -1 => "没能读到游戏进程的退出状态，它可能是被强行结束的。可尝试：重新启动游戏；若反复出现，请重启启动器后重试",
        1 => "游戏崩溃了。可尝试：到游戏目录下 crash-reports 打开最新的崩溃报告，或看 logs/latest.log 最后几十行；确认 Java 版本符合该版本要求；把 Mod 全部禁用后逐个启用排查冲突；把启动页的内存调低一些试试",
        132 => "游戏被系统以非法指令终止（信号 4），在 Apple Silicon 上大多是 Java 架构选错了，拿 x64 的 Java 跑不了 arm64 的游戏。可尝试：到设置页重新选择 Java，arm64 芯片选 aarch64 版本，或让启动器按版本自动挑选",
        134 => "游戏因 JVM 内部错误退出（信号 6）。可尝试：更新 Java；换一个稳定版 Java（如 21 LTS）；把 -Xmx 内存调低；更新显卡驱动",
        136 => "游戏遇到运算/驱动错误（信号 8），多为显卡驱动过旧。可尝试：更新显卡驱动；在设置里把画质、渲染距离调低后重进",
        137 => "游戏被系统强行结束，通常是内存不够被其他程序挤占。可尝试：在启动页把内存调低；关闭浏览器等占内存的程序；确认游戏目录所在磁盘还有可用空间",
        139 => "游戏发生段错误（信号 11），常见于显卡驱动过旧或 Forge 与 OptiFine 跟当前版本不兼容。可尝试：更新显卡驱动；更新或移除 Forge、OptiFine；logs/latest.log 的最后几十行一般会点名是哪一环出的问题",
        _ => $"游戏异常退出（退出码 {exitCode}）。可尝试：到游戏目录下 crash-reports 查看最新崩溃报告，或看 logs/latest.log 的最后几十行；把 Mod 全部禁用后逐个启用排查；仍不行可更换 Java 版本或更新启动器",
    };
}
