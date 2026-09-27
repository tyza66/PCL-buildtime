namespace PCL.Avalonia.Services.Minecraft;

/// <summary>
/// 全局版本隔离默认值，语义与上游 PCL2 的 LaunchArgumentIndieV2 一致。
/// </summary>
public enum VersionIsolationDefault
{
    /// <summary>关闭，所有版本共用游戏目录。</summary>
    Off = 0,

    /// <summary>仅隔离可安装 Mod 的版本（Fabric、Forge、NeoForge、LiteLoader）。</summary>
    ModdableOnly = 1,

    /// <summary>仅隔离非正式版（快照、预发布、远古、愚人节版本）。</summary>
    SnapshotOnly = 2,

    /// <summary>隔离非正式版与可安装 Mod 的版本。</summary>
    SnapshotAndModdable = 3,

    /// <summary>隔离所有版本。</summary>
    All = 4,
}

/// <summary>
/// 版本隔离判定：隔离时游戏目录指向 versions/&lt;版本名&gt;/，否则为公共游戏目录。
/// 每版本手动开关优先，其次探测版本文件夹内的 mods / saves，最后按全局默认。
/// </summary>
public static class VersionIsolationResolver
{
    /// <summary>可安装 Mod 的版本：带有 Fabric / Forge / NeoForge / LiteLoader 加载器。</summary>
    public static bool IsModdable(MinecraftVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version.Loader is LoaderKind.Fabric
            or LoaderKind.Forge
            or LoaderKind.NeoForge
            or LoaderKind.LiteLoader;
    }

    /// <summary>正式版：排除快照、预发布、远古与愚人节版本。</summary>
    public static bool IsRelease(MinecraftVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return version.State is not (InstanceState.Fool or InstanceState.Old or InstanceState.Snapshot);
    }

    /// <summary>
    /// 版本文件夹下存在带文件的 mods 目录，或带子目录的 saves 目录时自动开启隔离，
    /// 与上游对已有 Mod / 存档版本的自动判断一致。
    /// </summary>
    public static bool HasModsOrSaves(string? versionFolder)
    {
        if (string.IsNullOrWhiteSpace(versionFolder))
        {
            return false;
        }

        try
        {
            var modsFolder = Path.Combine(versionFolder, "mods");
            if (Directory.Exists(modsFolder) && Directory.EnumerateFiles(modsFolder).Any())
            {
                return true;
            }

            var savesFolder = Path.Combine(versionFolder, "saves");
            return Directory.Exists(savesFolder) && Directory.EnumerateDirectories(savesFolder).Any();
        }
        catch (IOException)
        {
            // 目录读取失败（外置卷 I/O 抖动等）时不当成有 Mod 或存档，继续走全局默认。
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 判定该版本启动时是否启用版本隔离：
    /// 每版本手动开关 &gt; mods / saves 探测 &gt; 全局默认。
    /// </summary>
    public static bool IsIsolated(
        MinecraftVersion version,
        VersionIsolationDefault globalDefault,
        VersionSettings? versionSettings = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (versionSettings?.Independent is { } manual)
        {
            return manual;
        }

        if (HasModsOrSaves(version.Folder))
        {
            return true;
        }

        return globalDefault switch
        {
            VersionIsolationDefault.Off => false,
            VersionIsolationDefault.ModdableOnly => IsModdable(version),
            VersionIsolationDefault.SnapshotOnly => !IsRelease(version),
            VersionIsolationDefault.SnapshotAndModdable => !IsRelease(version) || IsModdable(version),
            _ => true,
        };
    }

    /// <summary>隔离后的游戏目录：隔离时为版本文件夹，否则为公共游戏目录。</summary>
    public static string ResolveGameDirectory(
        string baseGameFolder,
        MinecraftVersion version,
        VersionIsolationDefault globalDefault,
        VersionSettings? versionSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseGameFolder);
        ArgumentNullException.ThrowIfNull(version);
        return IsIsolated(version, globalDefault, versionSettings)
            ? version.Folder
            : baseGameFolder;
    }
}
