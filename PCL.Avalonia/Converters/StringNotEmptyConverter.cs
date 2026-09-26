using System.Globalization;
using Avalonia.Data.Converters;

namespace PCL.Avalonia.Converters;

/// <summary>
/// Turns a string into <c>true</c> when it carries text, so hint rows can bind straight to the
/// message they show and disappear once there is nothing to say.
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
