using PCL.Avalonia.Services;

namespace PCL.Avalonia.Tests;

public sealed class MinecraftJavaRequirementTests
{
    [Theory]
    [InlineData("1.12.2", 8)]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17", 16)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.18", 17)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.19.4", 17)]
    [InlineData("1.20.1", 17)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.20.5", 21)]
    [InlineData("1.20.6", 21)]
    [InlineData("1.21", 21)]
    [InlineData("1.21.4", 21)]
    [InlineData(" 1.21.4 ", 21)]
    [InlineData("26.3", 25)]
    public void GetRequiredMajor_KnownRelease_ReturnsMappedMajor(string version, int expected)
        => Assert.Equal(expected, MinecraftJavaRequirement.GetRequiredMajor(version));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("23w13a")]
    [InlineData("1.20.5-pre2")]
    [InlineData("1.20.1-forge")]
    [InlineData("abc")]
    public void GetRequiredMajor_UnknownVersion_ReturnsNull(string version)
        => Assert.Null(MinecraftJavaRequirement.GetRequiredMajor(version));
}
