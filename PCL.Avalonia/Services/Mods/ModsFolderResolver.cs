using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services.Mods;

/// <summary>
/// 解析 Mod 目录：与启动逻辑共用同一套版本隔离判定。开启隔离时指向
/// versions/&lt;版本名&gt;/mods，否则为公共游戏目录下的 mods；未选择版本时
/// 退回公共目录。下载与管理两个页面都走这里，保证装进去的 Mod 就是游戏加载的 Mod。
/// </summary>
public static class ModsFolderResolver
{
    /// <summary>解析结果：游戏目录、Mod 目录，以及该版本是否处于隔离状态。</summary>
    public sealed record Resolved(string GameFolder, string ModsFolder, bool Isolated);

    public static Resolved Resolve(
        string baseGameFolder,
        MinecraftVersion? selectedVersion,
        VersionIsolationDefault isolationDefault,
        VersionSettings? versionSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseGameFolder);
        if (selectedVersion is null)
        {
            return new Resolved(baseGameFolder, Path.Combine(baseGameFolder, "mods"), false);
        }

        var isolated = VersionIsolationResolver.IsIsolated(
            selectedVersion,
            isolationDefault,
            versionSettings);
        var gameFolder = VersionIsolationResolver.ResolveGameDirectory(
            baseGameFolder,
            selectedVersion,
            isolationDefault,
            versionSettings);
        return new Resolved(gameFolder, Path.Combine(gameFolder, "mods"), isolated);
    }
}
