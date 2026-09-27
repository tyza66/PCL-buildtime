using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class GameLogDiagnosticsTests
{
    [Fact]
    public void BenignOutput_IsNotDiagnosed()
    {
        // 原版启动信息刷屏是常态，不能逢行就建议，那样真建议会被埋掉。
        Assert.Null(GameLogDiagnostics.Match("[main/INFO]: Loaded 3 recipes"));
        Assert.Null(GameLogDiagnostics.Match(""));
        Assert.Null(GameLogDiagnostics.Match(null));
    }

    [Fact]
    public void UnsupportedClassVersionError_ExplainsJavaUpgrade()
    {
        var hit = GameLogDiagnostics.Match(
            "java.lang.UnsupportedClassVersionError: net/minecraft/client/main/Main : Unsupported major.minor version 52.0");

        Assert.NotNull(hit);
        Assert.Equal("java-version", hit.Value.Key);
        Assert.Contains("Java", hit.Value.Advice);
        Assert.Contains("设置页", hit.Value.Advice);
    }

    [Fact]
    public void CompiledByNewerRuntime_AlsoCountsAsJavaTooLow()
    {
        // 同一个病因的另一种写法，别让用户自己意识到这是 Java 版本问题。
        var hit = GameLogDiagnostics.Match(
            "class file has wrong version 65.0, should be 61.0, has been compiled by a more recent version of the Java Runtime");

        Assert.NotNull(hit);
        Assert.Equal("java-version", hit.Value.Key);
    }

    [Fact]
    public void HeapReserveFailure_PointsAtMemorySlider()
    {
        var hit = GameLogDiagnostics.Match(
            "Error occurred during initialization of VM: Could not reserve enough space for object heap");

        Assert.NotNull(hit);
        Assert.Equal("heap-reserve", hit.Value.Key);
        Assert.Contains("内存", hit.Value.Advice);
        Assert.Contains("启动页", hit.Value.Advice);
    }

    [Fact]
    public void OutOfMemory_SaysRaiseMemory_AndPointsAtLaunchPage()
    {
        var hit = GameLogDiagnostics.Match("java.lang.OutOfMemoryError: Java heap space");

        Assert.NotNull(hit);
        Assert.Equal("oom", hit.Value.Key);
        Assert.Contains("内存", hit.Value.Advice);
    }

    [Fact]
    public void UnsatisfiedLinkError_BlamesJavaArchitecture()
    {
        // Apple Silicon 拿 x64 Java 跑 arm64 是最常见的死法，签名和退出码 132 讲的是同一件事。
        var hit = GameLogDiagnostics.Match(
            "Exception in thread \"main\" java.lang.UnsatisfiedLinkError: no lwjgl in java.library.path");

        Assert.NotNull(hit);
        Assert.Equal("natives", hit.Value.Key);
        Assert.Contains("架构", hit.Value.Advice);
    }

    [Fact]
    public void CorruptZip_TellsUserToRedownload()
    {
        var hit = GameLogDiagnostics.Match("java.util.zip.ZipException: zip END header not found");

        Assert.NotNull(hit);
        Assert.Equal("corrupt", hit.Value.Key);
        Assert.Contains("重新下载", hit.Value.Advice);
    }

    [Fact]
    public void IncompatibleJvm_NamesModLoaderJavaRequirement()
    {
        var hit = GameLogDiagnostics.Match("Incompatible JVM (Fatal): JVM version 25 is not supported by Forge");

        Assert.NotNull(hit);
        Assert.Equal("loader-unsupported", hit.Value.Key);
        Assert.Contains("Java", hit.Value.Advice);
    }

    [Fact]
    public void MixinFailure_GuidesModIsolation()
    {
        var hit = GameLogDiagnostics.Match("Mixin apply failed: mixins.json:Mixed could not be applied");

        Assert.NotNull(hit);
        Assert.Equal("mixin", hit.Value.Key);
        Assert.Contains("Mod", hit.Value.Advice);
    }

    [Fact]
    public void InvalidSession_PointsAtAccountPage()
    {
        var hit = GameLogDiagnostics.Match("Failed to authenticate: invalid session");

        Assert.NotNull(hit);
        Assert.Equal("session", hit.Value.Key);
        Assert.Contains("账号页", hit.Value.Advice);
    }

    [Fact]
    public void SpecificRuleWins_WhenSeveralSignaturesOnOneLine()
    {
        // UnsupportedClassVersionError 所在行经常还带着 main 线程异常，
        // 必须先报 Java 版本这个能修的病因，而不是泛泛的"主线程异常"。
        var hit = GameLogDiagnostics.Match(
            "Exception in thread \"main\" java.lang.UnsupportedClassVersionError: bad class version");

        Assert.NotNull(hit);
        Assert.Equal("java-version", hit.Value.Key);
    }

    [Fact]
    public void EveryAdvice_CarriesActionableNextStep()
    {
        // 所有建议都要能满足"报错也要给解决方法"：一句"可尝试"是底线。
        foreach (var key in new[]
                 {
                     "java-version", "heap-reserve", "oom", "natives",
                     "corrupt", "loader-unsupported", "mixin", "session", "main-exception",
                 })
        {
            var sample = key switch
            {
                "java-version" => "UnsupportedClassVersionError",
                "heap-reserve" => "Could not create the Java Virtual Machine",
                "oom" => "OutOfMemoryError",
                "natives" => "UnsatisfiedLinkError",
                "corrupt" => "ZipException",
                "loader-unsupported" => "Incompatible JVM",
                "mixin" => "Mixin apply failed",
                "session" => "invalid session",
                _ => "Exception in thread \"main\"",
            };
            var hit = GameLogDiagnostics.Match(sample);
            Assert.NotNull(hit);
            Assert.Equal(key, hit.Value.Key);
            Assert.Contains("可尝试", hit.Value.Advice);
        }
    }
}
