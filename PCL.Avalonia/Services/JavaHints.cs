using System.Runtime.InteropServices;

namespace PCL.Avalonia.Services;

/// <summary>
/// Java 要求提示的档位。Unknown 表示版本没提供要求信息，不要替用户下结论。
/// </summary>
public enum JavaHintLevel
{
    Unknown,
    Satisfied,
    TooLow,
    Missing,
}

/// <summary>
/// 把"这个版本要哪个 Java"和"本机最高检测到哪个 Java"合成一句用户能照着做的提示。
/// Text 给卡片徽标用，要短；Detail 给悬停提示和状态栏用，必须带上下一步该怎么做。
/// </summary>
public readonly record struct JavaHints(JavaHintLevel Level, string Text, string Detail)
{
    /// <summary>本机检测到的最高 Java 大版本号，一个都没检测到返回 null。</summary>
    public static int? BestMajor(IEnumerable<JavaInfo> javas)
        => javas
            .Where(java => java.IsValid && java.MajorVersion > 0)
            .Select(java => (int?)java.MajorVersion)
            .Max();

    /// <summary>
    /// 按"版本要求 / 本机最高"组提示。requiredMajor 为 null（版本 JSON 没提供要求）时
    /// 返回 Unknown，Text 是空串，界面据此隐藏徽标，不要编一个版本号吓唬人。
    /// </summary>
    public static JavaHints ForRequirement(int? requiredMajor, int? bestMajor)
    {
        if (requiredMajor is not { } required)
        {
            return new JavaHints(
                JavaHintLevel.Unknown,
                "",
                "该版本没有提供 Java 要求信息，启动时会按实际检测到的 Java 运行");
        }

        if (bestMajor is null)
        {
            return new JavaHints(
                JavaHintLevel.Missing,
                $"需 Java {required}",
                $"该版本需要 Java {required}，但本机没有检测到 Java。"
                + $"请先安装 Java {required}，再到设置页指定路径，否则无法启动");
        }

        if (bestMajor < required)
        {
            return new JavaHints(
                JavaHintLevel.TooLow,
                $"需 Java {required}",
                $"该版本需要 Java {required}，当前检测到的最高版本是 Java {bestMajor}。"
                + $"请到设置页更换或安装 Java {required}");
        }

        return new JavaHints(
            JavaHintLevel.Satisfied,
            $"Java {required}",
            $"该版本需要 Java {required}，当前 Java {bestMajor} 满足要求");
    }

    /// <summary>
    /// Java 大版本不满足时的一句"不能就这么跑"的原因，放行返回 null。
    /// 找不到匹配大版本时 JavaService 会退回默认 Java，不拦下来游戏只会莫名崩溃，玩家看不出是 Java 的锅。
    /// 启动和导出启动脚本都要先过这一关：一个直接终止启动，一个也要把风险讲在状态栏。
    /// </summary>
    public static string? DescribeMismatch(string versionId, int? requiredMajor, int? actualMajor)
    {
        if (requiredMajor is not { } required
            || actualMajor is not { } actual
            || actual <= 0
            || actual >= required)
        {
            return null;
        }

        return $"{versionId} 需要 Java {required}，当前只会用到 Java {actual}。"
            + $"可尝试：安装 Java {required}，或到设置页把 Java 路径指定到 Java {required}";
    }

    /// <summary>
    /// Java 架构和本机原生架构对不上时的一句警告，一致、认不出、或平台不该较真时返回 null。
    /// Apple Silicon 上拿 x64 的 Java 起游戏，轻则在 Rosetta 下慢一半，重则新版 LWJGL 直接
    /// UnsatisfiedLinkError，等游戏崩了再查没人会想到是 Java 架构，启动前就该讲清。
    /// 只警告不拦：用户机器上可能就装了这一个 Java，拦下来等于彻底不让人玩。
    /// </summary>
    public static string? DescribeArchitectureMismatch(
        string? javaArchitecture,
        Architecture systemArchitecture)
    {
        var native = DescribeNativeArchitecture(systemArchitecture);
        if (native is null)
        {
            return null;
        }

        var java = NormalizeArchitecture(javaArchitecture);
        if (java is null)
        {
            return null;
        }

        if (java == native)
        {
            return null;
        }

        return $"即将使用的 Java 是 {java} 架构，本机是 {native} 架构，游戏可能启动失败或明显卡顿。"
            + $"可尝试：在设置页的 Java 列表里改选 {native} 架构的 Java，或先安装 {native} 版 Java"
            + "（macOS 可执行 brew install openjdk）再回到设置页重新扫描";
    }

    /// <summary>只在本机原生架构是 x64 / arm64 时较真，别的架构不替用户下结论。</summary>
    private static string? DescribeNativeArchitecture(Architecture architecture)
        => architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

    /// <summary>把 <see cref="JavaInfo.Architecture"/> 的 "x64" / "arm64" / "unknown" 收敛成对比用词，认不出返回 null。</summary>
    private static string? NormalizeArchitecture(string? architecture)
    {
        if (string.IsNullOrWhiteSpace(architecture))
        {
            return null;
        }

        var text = architecture.Trim().ToLowerInvariant();
        if (text is "unknown" or "?" or "-")
        {
            return null;
        }

        if (text.Contains("x64") || text.Contains("x86_64") || text.Contains("amd64"))
        {
            return "x64";
        }

        if (text.Contains("arm64") || text.Contains("aarch64"))
        {
            return "arm64";
        }

        return null;
    }
}
