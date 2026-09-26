using System.Globalization;
using Avalonia.Data.Converters;

namespace PCL.Avalonia.Converters;

/// <summary>
/// Turns an <see cref="int"/> count (typically <c>SomeCollection.Count</c>) into
/// <c>true</c> when it is zero, so empty-state overlays can bind straight to a list size.
/// </summary>
public sealed class IsEmptyCollectionConverter : IValueConverter
{
    public static readonly IsEmptyCollectionConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int count => count == 0,
            null => true,
            _ => false
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
