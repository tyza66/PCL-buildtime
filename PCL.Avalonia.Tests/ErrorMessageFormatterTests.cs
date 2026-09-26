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
    public void Describe_MissingFile_ExplainsNotFoundBeforeIoError()
    {
        // FileNotFoundException 是 IOException 的子类，说成"文件读写失败"会把用户带偏。
        var text = ErrorMessageFormatter.Describe(
            new FileNotFoundException("版本 JSON 不存在", "/games/mc/versions/1.20.1/1.20.1.json"));

        Assert.Contains("找不到文件", text);
        Assert.Contains("可尝试", text);
        Assert.DoesNotContain("文件读写失败", text);
    }

    [Fact]
    public void Describe_MissingDirectory_ExplainsNotFound()
    {
        var text = ErrorMessageFormatter.Describe(new DirectoryNotFoundException("/games/mc"));

        Assert.Contains("找不到目录", text);
        Assert.Contains("可尝试", text);
        Assert.DoesNotContain("文件读写失败", text);
    }

    [Fact]
    public void Describe_ChineseBusinessErrorWithoutGuidance_GainsAdvice()
    {
        var text = ErrorMessageFormatter.Describe(new InvalidDataException("整合包文件已损坏"));

        Assert.Contains("整合包文件已损坏", text);
        Assert.Contains("切换", text);
    }

    [Fact]
    public void Describe_ChineseMessageAlreadyCarryingGuidance_StaysAsItIs()
    {
        var text = ErrorMessageFormatter.Describe(new ArgumentException("路径含有非法字符，请更换游戏目录"));

        Assert.Equal("路径含有非法字符，请更换游戏目录", text);
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

    [Fact]
    public void Brief_MissingFileAndFolder_GetDedicatedLabels()
    {
        Assert.Equal("文件缺失", ErrorMessageFormatter.Brief(new FileNotFoundException("x")));
        Assert.Equal("目录缺失", ErrorMessageFormatter.Brief(new DirectoryNotFoundException("x")));
    }
}
