namespace PCL.Avalonia.Services.Downloads;

/// <summary>
/// 计数型下载进度的统一文案：总数未知或为 0 时不渲染 x/y，
/// 否则下载刚开始的一瞬间会闪出「正在下载资源 0/0」这种没有意义的进度。
/// </summary>
public static class CountedProgressText
{
    public static string Format(string prefix, int completedItems, int totalItems, string? itemName)
    {
        var name = string.IsNullOrWhiteSpace(itemName) ? "" : "：" + itemName.Trim();
        return totalItems > 0
            ? $"{prefix} {completedItems}/{totalItems}{name}"
            : prefix + "...";
    }
}
