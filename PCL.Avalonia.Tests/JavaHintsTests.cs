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
}
