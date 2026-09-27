using System.Diagnostics;
using System.Reflection;

namespace PCL.Avalonia.Services;

/// <summary>
/// 取启动器自己的版本号。<c>&lt;Version&gt;</c> 落在程序集的"信息性版本"上，直接读
/// <see cref="Assembly.GetName()"/> 只会拿到 <c>AssemblyVersion</c>（本仓库固定是 1.0.0），
/// 于是界面上永远显示 1.0.0、传给游戏的 -Dlauncher_version 也是谎报。这里统一按
/// 信息性版本 → 文件版本 → 程序集版本的顺序取，并去掉 "+提交号" 尾巴。
/// </summary>
public static class AppVersionInfo
{
    /// <summary>形如 1.0.20261010 的三段版本号；取不到时退回 1.0.0。</summary>
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var assembly = typeof(AppVersionInfo).Assembly;

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var text = Normalize(informational);
        if (!string.IsNullOrEmpty(text))
        {
            return text;
        }

        var file = Normalize(FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion);
        if (!string.IsNullOrEmpty(file))
        {
            return file;
        }

        return Normalize(assembly.GetName().Version?.ToString(3)) ?? "1.0.0";
    }

    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // 信息性版本可能带 "+abc123" 的提交号，文件版本可能是 "1.0.20261010" 或 "1, 0, 20261010, 0"。
        var text = raw.Trim();
        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        if (text.Contains(','))
        {
            text = string.Join('.', text.Split(',').Select(part => part.Trim()));
        }

        text = text.Trim();
        return text.Length == 0 ? null : text;
    }
}
