using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Tests;

public sealed class StartupDiagnosticsServiceTests
{
    private static readonly StartupDiagnosticsService Service = new();

    private static JavaInfo Java(int major) => new(
        $"/java/{major}/bin/java",
        $"{major}.0.1",
        "aarch64",
        major,
        IsValid: true);

    private static DiagnosticItem ItemOf(StartupDiagnosticsInput input, string title)
        => Service.Run(input).Single(item => item.Title == title);

    [Fact]
    public void NoJava_ReportsFailure_PointingAtSettingsPage()
    {
        var item = ItemOf(new StartupDiagnosticsInput("", [], [], []), "Java 环境");

        Assert.False(item.IsOk);
        Assert.Contains("设置", item.Detail);
        Assert.Contains("Java 21", item.Detail);
    }

    [Fact]
    public void DetectedJava_ReportsCountAndBestVersion()
    {
        var item = ItemOf(
            new StartupDiagnosticsInput("", [], [Java(17), Java(21)], []),
            "Java 环境");

        Assert.True(item.IsOk);
        Assert.Contains("2", item.Detail);
        Assert.Contains("Java 21", item.Detail);
    }

    [Fact]
    public void VersionRequiringNewerJavaThanInstalled_IsListed()
    {
        var item = ItemOf(
            new StartupDiagnosticsInput(
                "",
                [],
                [Java(17)],
                [new InstalledJavaRequirement("1.21.4", 21), new InstalledJavaRequirement("1.16.5", 8)]),
            "已安装版本");

        Assert.False(item.IsOk);
        Assert.Contains("1.21.4", item.Detail);
        Assert.Contains("Java 21", item.Detail);
        Assert.DoesNotContain("1.16.5", item.Detail);
    }

    [Fact]
    public void VersionRequirementUnknown_DoesNotFailTheCheck()
    {
        var item = ItemOf(
            new StartupDiagnosticsInput(
                "",
                [],
                [Java(17)],
                [new InstalledJavaRequirement("24w14a", null)]),
            "已安装版本");

        Assert.True(item.IsOk);
    }

    [Fact]
    public void AllSatisfiedVersions_Pass()
    {
        var item = ItemOf(
            new StartupDiagnosticsInput(
                "",
                [],
                [Java(21)],
                [new InstalledJavaRequirement("1.21.4", 21), new InstalledJavaRequirement("1.16.5", 8)]),
            "已安装版本");

        Assert.True(item.IsOk);
    }

    [Fact]
    public void NoInstalledVersions_ReportsMildSuccess()
    {
        var item = ItemOf(new StartupDiagnosticsInput("", [], [Java(21)], []), "已安装版本");

        Assert.True(item.IsOk);
    }

    [Fact]
    public void BlankGameFolder_Fails_WithVersionPageHint()
    {
        var item = ItemOf(new StartupDiagnosticsInput("   ", [], [], []), "游戏目录");

        Assert.False(item.IsOk);
        Assert.Contains("版本", item.Detail);
    }

    [Fact]
    public void MissingGameFolder_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "pcl-gone-" + Guid.NewGuid().ToString("N"));

        var item = ItemOf(new StartupDiagnosticsInput(missing, [], [], []), "游戏目录");

        Assert.False(item.IsOk);
        Assert.Contains("不存在", item.Detail);
    }

    [Fact]
    public void WritableGameFolder_Passes()
    {
        var temp = Directory.CreateTempSubdirectory("pcl-diag-");
        try
        {
            var item = ItemOf(new StartupDiagnosticsInput(temp.FullName, [], [], []), "游戏目录");

            Assert.True(item.IsOk);
        }
        finally
        {
            temp.Delete(true);
        }
    }

    [Fact]
    public void MissingLaunchFolder_IsListed()
    {
        var item = ItemOf(
            new StartupDiagnosticsInput(
                "",
                [new MinecraftFolder("主目录", Path.Combine(Path.GetTempPath(), "pcl-nope-" + Guid.NewGuid().ToString("N")))],
                [],
                []),
            "启动目录");

        Assert.False(item.IsOk);
        Assert.Contains("主目录", item.Detail);
    }

    [Fact]
    public void ExistingLaunchFolders_Pass()
    {
        var temp = Directory.CreateTempSubdirectory("pcl-diag-");
        try
        {
            var item = ItemOf(
                new StartupDiagnosticsInput("", [new MinecraftFolder("主目录", temp.FullName)], [], []),
                "启动目录");

            Assert.True(item.IsOk);
        }
        finally
        {
            temp.Delete(true);
        }
    }
}
