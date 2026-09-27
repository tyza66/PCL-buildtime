using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services;

/// <summary>
/// 启动自检里的一条检查结果。IsOk 为 false 时 Detail 必须以「可尝试：」收尾给出中文解法，
/// 这是启动器的既定约定：报错必须带路，不能只告诉用户坏了。
/// </summary>
public sealed record DiagnosticItem(bool IsOk, string Title, string Detail);

/// <summary>已安装版本对 Java 的要求，RequiredMajor 为 null 表示版本 JSON 没提供要求，不猜。</summary>
public sealed record InstalledJavaRequirement(string VersionId, int? RequiredMajor);

/// <summary>启动自检的输入：游戏目录、全部启动目录、本机 Java、已安装版本的要求。</summary>
public sealed record StartupDiagnosticsInput(
    string GameFolder,
    IReadOnlyList<MinecraftFolder> LaunchFolders,
    IReadOnlyList<JavaInfo> Javas,
    IReadOnlyList<InstalledJavaRequirement> InstalledRequirements);

public interface IStartupDiagnosticsService
{
    IReadOnlyList<DiagnosticItem> Run(StartupDiagnosticsInput input);
}

/// <summary>
/// 把日常翻车点（Java 缺失、版本要求的 Java 过高、游戏目录失效、磁盘空间不足、启动目录丢失）
/// 集中跑一遍，逐条给修复建议。「其他」页的启动自检按钮驱动它。
/// </summary>
public sealed class StartupDiagnosticsService : IStartupDiagnosticsService
{
    /// <summary>低于该剩余空间视为放不下游戏。</summary>
    private const long LowFreeSpaceBytes = 5L << 30;

    /// <summary>失败明细里最多列几条，超出的折叠成「等共 N 项」。</summary>
    private const int MaxListedItems = 5;

    public IReadOnlyList<DiagnosticItem> Run(StartupDiagnosticsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return
        [
            CheckJava(input.Javas),
            CheckInstalledVersions(input.Javas, input.InstalledRequirements),
            CheckGameFolder(input.GameFolder),
            CheckLaunchFolders(input.LaunchFolders),
        ];
    }

    private static DiagnosticItem CheckJava(IReadOnlyList<JavaInfo> javas)
    {
        var best = JavaHints.BestMajor(javas);
        if (best is null)
        {
            return new DiagnosticItem(
                false,
                "Java 环境",
                "未检测到可用的 Java。可尝试：在「设置 → Java」中扫描本机或手动指定 Java 21 的路径，"
                + "也可以先从 adoptium.net 等站点安装后再扫描。");
        }

        var count = javas.Count(java => java is { IsValid: true, MajorVersion: > 0 });
        return new DiagnosticItem(true, "Java 环境", $"检测到 {count} 个可用的 Java，最高为 Java {best}。");
    }

    private static DiagnosticItem CheckInstalledVersions(
        IReadOnlyList<JavaInfo> javas,
        IReadOnlyList<InstalledJavaRequirement> requirements)
    {
        if (requirements.Count == 0)
        {
            return new DiagnosticItem(
                true,
                "已安装版本",
                "当前没有已安装的游戏版本。可尝试：在「版本」页面下载新版本，或导入已有的整合包。");
        }

        var best = JavaHints.BestMajor(javas);
        var unsatisfied = requirements
            .Where(requirement => requirement.RequiredMajor is { } required && (best is null || required > best))
            .ToArray();
        if (unsatisfied.Length == 0)
        {
            return new DiagnosticItem(true, "已安装版本", $"{requirements.Count} 个已安装版本均满足 Java 要求。");
        }

        var listed = string.Join(
            "、",
            unsatisfied.Take(MaxListedItems).Select(requirement => $"{requirement.VersionId}（需要 Java {requirement.RequiredMajor}）"));
        var more = unsatisfied.Length > MaxListedItems ? $" 等共 {unsatisfied.Length} 个版本" : "";
        var current = best is null ? "本机没有可用的 Java" : $"本机最高为 Java {best}";
        return new DiagnosticItem(
            false,
            "已安装版本",
            $"{listed}{more}的 Java 要求高于当前环境（{current}）。可尝试：安装相应版本的 Java，"
            + "然后在「设置 → Java」中重新扫描或手动指定。");
    }

    private static DiagnosticItem CheckGameFolder(string gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            return new DiagnosticItem(
                false,
                "游戏目录",
                "尚未设置游戏目录。可尝试：在「版本」页面添加目录，或到「设置」里确认默认位置。");
        }

        if (!Directory.Exists(gameFolder))
        {
            return new DiagnosticItem(
                false,
                "游戏目录",
                $"目录 {gameFolder} 不存在。可尝试：检查外接磁盘是否已挂载，或重新指定游戏目录。");
        }

        var probe = Path.Combine(gameFolder, ".pcl-write-test");
        try
        {
            File.WriteAllText(probe, "pcl");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new DiagnosticItem(
                false,
                "游戏目录",
                $"无法写入目录 {gameFolder}。可尝试：确认磁盘未处于只读状态，或用磁盘工具修复权限后重试。");
        }

        var free = GetFreeSpace(gameFolder);
        if (free is not null && free < LowFreeSpaceBytes)
        {
            return new DiagnosticItem(
                false,
                "游戏目录",
                $"磁盘剩余空间仅 {FormatGiB(free.Value)}，可能放不下游戏。可尝试：清理临时文件后重试，或把游戏目录换到剩余空间更大的磁盘。");
        }

        var space = free is null ? "剩余空间未知" : $"剩余空间 {FormatGiB(free.Value)}";
        return new DiagnosticItem(true, "游戏目录", $"目录可写，{space}。");
    }

    private static DiagnosticItem CheckLaunchFolders(IReadOnlyList<MinecraftFolder> launchFolders)
    {
        if (launchFolders.Count == 0)
        {
            return new DiagnosticItem(
                true,
                "启动目录",
                "没有额外的启动目录，游戏会直接使用游戏目录下的 .minecraft。");
        }

        var missing = launchFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Path) && !Directory.Exists(folder.Path))
            .ToArray();
        if (missing.Length == 0)
        {
            return new DiagnosticItem(true, "启动目录", $"{launchFolders.Count} 个启动目录均存在。");
        }

        var listed = string.Join(
            "、",
            missing.Take(MaxListedItems).Select(folder => string.IsNullOrWhiteSpace(folder.Name) ? folder.Path : $"{folder.Name}（{folder.Path}）"));
        var more = missing.Length > MaxListedItems ? $" 等共 {missing.Length} 个" : "";
        return new DiagnosticItem(
            false,
            "启动目录",
            $"{listed}{more}不存在。可尝试：在游戏目录设置里移除失效的启动目录，或修正它们的路径。");
    }

    private static long? GetFreeSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string FormatGiB(long bytes)
        => (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0") + " GB";
}
