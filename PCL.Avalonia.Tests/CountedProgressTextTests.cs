using PCL.Avalonia.Services.Downloads;

namespace PCL.Avalonia.Tests;

public sealed class CountedProgressTextTests
{
    [Fact]
    public void ZeroTotal_RendersEllipsis_InsteadOfSlashCount()
    {
        Assert.Equal("正在下载资源...", CountedProgressText.Format("正在下载资源", 0, 0, null));
        Assert.Equal("正在下载支持库...", CountedProgressText.Format("正在下载支持库", 5, 0, "core-1.0.jar"));
    }

    [Fact]
    public void PositiveTotal_RendersSlashCount()
    {
        Assert.Equal("正在下载资源 3/500", CountedProgressText.Format("正在下载资源", 3, 500, null));
        Assert.Equal("正在下载资源 3/500：a.png", CountedProgressText.Format("正在下载资源", 3, 500, "a.png"));
    }

    [Fact]
    public void BlankItemName_OmitsTrailingColon()
    {
        Assert.DoesNotContain("：", CountedProgressText.Format("正在下载资源", 3, 500, "   "));
        Assert.DoesNotContain("：", CountedProgressText.Format("正在下载资源", 3, 500, null));
    }
}
