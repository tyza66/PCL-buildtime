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
}
