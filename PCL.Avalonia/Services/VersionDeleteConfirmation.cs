namespace PCL.Avalonia.Services;

/// <summary>
/// 版本删除确认文案：启动页和版本页必须一字不差，玩家在两个入口看到的后果说明要一样。
/// </summary>
public static class VersionDeleteConfirmation
{
    public const string Title = "删除版本";

    public static string Describe(string versionId) =>
        $"确定要删除 {versionId} 吗？该版本的 jar、存档、Mod 和配置文件都会一起删除，且无法恢复。";
}
