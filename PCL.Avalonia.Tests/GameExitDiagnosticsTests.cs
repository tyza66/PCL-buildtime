using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class GameExitDiagnosticsTests
{
    [Fact]
    public void CleanExit_HasNoAdvice_AndReadsAsNormal()
    {
        Assert.Equal("", GameExitDiagnostics.Describe(0));
        Assert.Equal("正常退出", GameExitDiagnostics.Brief(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    public void CrashExit_PointsToCrashReports_AndModTriage(int exitCode)
    {
        var advice = GameExitDiagnostics.Describe(exitCode);

        Assert.Contains("crash-reports", advice);
        Assert.Contains("logs/latest.log", advice);
        Assert.Contains("Mod", advice);
        Assert.Contains("可尝试", advice);
    }

    [Fact]
    public void IllegalInstructionExit_BlamesJavaArchitecture_AndSettingsPage()
    {
        // 128+4：Apple Silicon 上拿错架构的 Java 是最常见的死法，得说出"设置页换 Java"。
        var advice = GameExitDiagnostics.Describe(132);

        Assert.Contains("Apple Silicon", advice);
        Assert.Contains("设置页", advice);
        Assert.Contains("aarch64", advice);
    }

    [Fact]
    public void KilledExit_SaysMemory_AndPointsAtTheLaunchPageSlider()
    {
        // 128+9：多数时候是被 OOM killer 收掉的，玩家能自己动手的就是把内存调低。
        var advice = GameExitDiagnostics.Describe(137);

        Assert.Contains("内存", advice);
        Assert.Contains("启动页", advice);
    }

    [Fact]
    public void SegfaultExit_NamesGraphicsDriverAndForge()
    {
        var advice = GameExitDiagnostics.Describe(139);

        Assert.Contains("显卡驱动", advice);
        Assert.Contains("Forge", advice);
    }

    [Fact]
    public void UnknownExitState_ExplainsWhy_AndSuggestsRetry()
    {
        Assert.Contains("重新启动游戏", GameExitDiagnostics.Describe(-1));
        Assert.Equal("退出状态未知", GameExitDiagnostics.Brief(-1));
    }

    [Fact]
    public void LogLine_StatesTheCodeWithoutJudging()
    {
        Assert.Equal("游戏进程已退出（退出码 1）", GameExitDiagnostics.LogLine(1));
    }
}
