using System.Globalization;
using PCL.Avalonia.Converters;

namespace PCL.Avalonia.Tests;

public sealed class StringNotEmptyConverterTests
{
    private readonly StringNotEmptyConverter _converter = new();

    [Fact]
    public void Convert_OnlyStringsWithTextTurnIntoTrue()
    {
        Assert.True((bool)_converter.Convert("Java 21 满足要求", typeof(bool), null!, CultureInfo.InvariantCulture));
        Assert.False((bool)_converter.Convert("", typeof(bool), null!, CultureInfo.InvariantCulture));
        Assert.False((bool)_converter.Convert("   ", typeof(bool), null!, CultureInfo.InvariantCulture));
        Assert.False((bool)_converter.Convert(null, typeof(bool), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(true, typeof(string), null!, CultureInfo.InvariantCulture));
    }
}
