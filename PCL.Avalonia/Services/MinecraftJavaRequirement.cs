using System.Text.RegularExpressions;

namespace PCL.Avalonia.Services;

/// <summary>
/// 按游戏版本号推断它需要的 Java 大版本号。Mod / 资源 / 整合包这些下载页手里只有
/// "1.20.1" 这样的版本号字符串，没有版本 JSON 可以读 javaVersion.majorVersion，
/// 先用 Mojang 官方的对应关系给用户一句提示，别等装完启动才发现 Java 版本不对。
/// 解析不了的版本（快照、预发布、乱填的）返回 null，调用方按"未知"处理，不要瞎猜。
/// </summary>
public static partial class MinecraftJavaRequirement
{
    // 只认 "1.20.1" / "1.21" / "26.3" 这种纯数字三段式；快照 24w14a、预发布 1.20.5-pre1 一律放过。
    [GeneratedRegex(@"^(\d+)(?:\.(\d+))?(?:\.(\d+))?$")]
    private static partial Regex VersionPattern();

    /// <summary>
    /// 返回该游戏版本要求的 Java 大版本号（如 1.20.1 → 17，1.21.4 → 21），识别不了返回 null。
    /// </summary>
    public static int? GetRequiredMajor(string? minecraftVersion)
    {
        var text = minecraftVersion?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = VersionPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        _ = int.TryParse(match.Groups[1].Value, out var major);
        _ = int.TryParse(match.Groups[2].Value, out var minor);
        _ = int.TryParse(match.Groups[3].Value, out var patch);

        if (major == 1)
        {
            // 1.20.5（24w14a）起换成 Java 21，1.18 到 1.20.4 是 17，1.17 是 16，更早的都是 8。
            if (minor >= 21 || (minor == 20 && patch >= 5))
            {
                return 21;
            }

            if (minor >= 18)
            {
                return 17;
            }

            return minor == 17 ? 16 : 8;
        }

        // Mojang 之后改用年份命名（如 26.3），对应 Java 25。
        return major >= 2 ? 25 : null;
    }
}
