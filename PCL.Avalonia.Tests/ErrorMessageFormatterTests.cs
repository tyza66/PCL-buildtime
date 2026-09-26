using System.Text.Json;
using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class ErrorMessageFormatterTests
{
    [Fact]
    public void Describe_NetworkError_KeepsReason_AndPointsToDownloadSource()
    {
        var text = ErrorMessageFormatter.Describe(new HttpRequestException("no route to host"));

        Assert.Contains("网络", text);
        Assert.Contains("no route to host", text);
        Assert.Contains("下载源", text);
    }

    [Fact]
    public void Describe_Timeout_SuggestsRetryAndSourceSwitch()
    {
        var text = ErrorMessageFormatter.Describe(new TaskCanceledException());

        Assert.Contains("超时", text);
        Assert.Contains("下载源", text);
    }

    [Fact]
    public void Describe_PermissionError_SuggestsAdminAndFolderChange()
    {
        var text = ErrorMessageFormatter.Describe(new UnauthorizedAccessException("/games/mc"));

        Assert.Contains("权限", text);
        Assert.Contains("管理员", text);
    }

    [Fact]
    public void Describe_DiskError_SuggestsSpaceAndOccupationChecks()
    {
        var text = ErrorMessageFormatter.Describe(new IOException("disk full"));

        Assert.Contains("磁盘", text);
        Assert.Contains("占用", text);
    }

    [Fact]
    public void Describe_ChineseBusinessError_PassesThroughUnchanged()
    {
        var text = ErrorMessageFormatter.Describe(new InvalidOperationException("该版本已存在，请刷新列表"));

        Assert.Equal("该版本已存在，请刷新列表", text);
    }

    [Fact]
    public void Describe_UnknownError_FallsBackToRestartAdvice()
    {
        var text = ErrorMessageFormatter.Describe(new NotSupportedException("nope"));

        Assert.Contains("重启启动器", text);
        Assert.Contains("nope", text);
    }

    [Fact]
    public void Brief_CommonFailures_ReturnShortChineseLabels()
    {
        Assert.Equal("网络异常", ErrorMessageFormatter.Brief(new HttpRequestException()));
        Assert.Equal("连接超时", ErrorMessageFormatter.Brief(new TimeoutException()));
        Assert.Equal("已取消", ErrorMessageFormatter.Brief(new OperationCanceledException()));
        Assert.Equal("没有写入权限", ErrorMessageFormatter.Brief(new UnauthorizedAccessException()));
        Assert.Equal("下载源数据解析失败", ErrorMessageFormatter.Brief(new JsonException()));
        Assert.Equal("未知错误", ErrorMessageFormatter.Brief(new NotSupportedException("boom")));
    }

    [Fact]
    public void Brief_ChineseBusinessError_KeepsFirstClause()
    {
        Assert.Equal("该版本已存在", ErrorMessageFormatter.Brief(new InvalidOperationException("该版本已存在，请刷新列表")));
    }
}
