using System.Runtime.InteropServices;
using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class JavaHintsTests
{
    [Fact]
    public void BestMajor_IgnoresInvalidEntriesAndPicksHighest()
    {
        var javas = new[]
        {
            new JavaInfo("/java/8", "1.8.0_402", "x64", 8, true),
            new JavaInfo("/java/broken", "corrupt", "x64", 0, false),
            new JavaInfo("/java/17", "17.0.9", "x64", 17, true),
        };

        Assert.Equal(17, JavaHints.BestMajor(javas));
    }

    [Fact]
    public void BestMajor_ReturnsNull_WhenNothingUsable()
    {
        Assert.Null(JavaHints.BestMajor([new JavaInfo("/java/broken", "corrupt", "x64", 0, false)]));
    }

    [Fact]
    public void ForRequirement_Unknown_WhenVersionDoesNotSayJava()
    {
        var hint = JavaHints.ForRequirement(null, 17);

        Assert.Equal(JavaHintLevel.Unknown, hint.Level);
        Assert.Equal("", hint.Text);
        Assert.Contains("没有提供", hint.Detail);
    }

    [Fact]
    public void ForRequirement_Missing_WhenNoJavaInstalled()
    {
        var hint = JavaHints.ForRequirement(17, null);

        Assert.Equal(JavaHintLevel.Missing, hint.Level);
        Assert.Equal("需 Java 17", hint.Text);
        Assert.Contains("没有检测到 Java", hint.Detail);
        Assert.Contains("安装 Java 17", hint.Detail);
    }

    [Fact]
    public void ForRequirement_TooLow_WhenInstalledJavaTooOld()
    {
        var hint = JavaHints.ForRequirement(17, 8);

        Assert.Equal(JavaHintLevel.TooLow, hint.Level);
        Assert.Equal("需 Java 17", hint.Text);
        Assert.Contains("Java 8", hint.Detail);
        Assert.Contains("安装 Java 17", hint.Detail);
    }

    [Fact]
    public void ForRequirement_Satisfied_WhenJavaNewEnough()
    {
        var hint = JavaHints.ForRequirement(17, 21);

        Assert.Equal(JavaHintLevel.Satisfied, hint.Level);
        Assert.Equal("Java 17", hint.Text);
        Assert.Contains("满足要求", hint.Detail);
    }

    [Fact]
    public void DescribeMismatch_BlocksOnlyWhenJavaIsTooOld()
    {
        // 太低了要拦，且必须点明差距、装哪个版本、去哪儿改。
        var blocked = JavaHints.DescribeMismatch("26.3", 25, 17);
        Assert.NotNull(blocked);
        Assert.Contains("26.3 需要 Java 25", blocked);
        Assert.Contains("只会用到 Java 17", blocked);
        Assert.Contains("安装 Java 25", blocked);
        Assert.Contains("设置页", blocked);

        // 持平、更高、版本没给要求、Java 版本号未知，一律放行：别拿猜测拦玩家的正常启动。
        Assert.Null(JavaHints.DescribeMismatch("26.3", 25, 25));
        Assert.Null(JavaHints.DescribeMismatch("26.3", 25, 26));
        Assert.Null(JavaHints.DescribeMismatch("26.3", null, 8));
        Assert.Null(JavaHints.DescribeMismatch("26.3", 25, null));
        Assert.Null(JavaHints.DescribeMismatch("26.3", 25, 0));
    }

    [Theory]
    [InlineData("x64", Architecture.Arm64)]
    [InlineData("arm64", Architecture.X64)]
    public void DescribeArchitectureMismatch_WarnsWhenJavaDoesNotMatchSystem(string javaArchitecture, Architecture systemArchitecture)
    {
        var warning = JavaHints.DescribeArchitectureMismatch(javaArchitecture, systemArchitecture);

        Assert.NotNull(warning);
        Assert.Contains(javaArchitecture, warning);
        Assert.Contains("brew install openjdk", warning);
        Assert.Contains("可尝试", warning);
    }

    [Theory]
    [InlineData("x64", Architecture.X64)]
    [InlineData("arm64", Architecture.Arm64)]
    [InlineData("unknown", Architecture.X64)]
    [InlineData("", Architecture.Arm64)]
    [InlineData(null, Architecture.X64)]
    [InlineData("x64", Architecture.Armv6)]
    public void DescribeArchitectureMismatch_StaysQuietUnlessItIsSure(string? javaArchitecture, Architecture systemArchitecture)
    {
        // 一致、Java 架构认不出、本机架构不在较真范围，都不许编一句警告吓唬人。
        Assert.Null(JavaHints.DescribeArchitectureMismatch(javaArchitecture, systemArchitecture));
    }
}
