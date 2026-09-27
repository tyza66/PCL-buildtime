using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia.Data.Converters;
using PCL.Avalonia.Services;

namespace PCL.Avalonia.Converters;

/// <summary>
/// Java 架构与本机原生架构不一致时返回 true，供设置页的 Java 列表把该条目标成警示色。
/// 系统架构走 <see cref="SystemArchitectureProvider"/>，默认取本机真实值，测试可替换成固定值。
/// </summary>
public sealed class JavaArchitectureMismatchConverter : IValueConverter
{
    public static readonly JavaArchitectureMismatchConverter Instance = new();

    /// <summary>系统架构来源。headless 测试里换成固定值，避免断言结果随运行机器的芯片而变。</summary>
    public static Func<Architecture> SystemArchitectureProvider { get; set; }
        = () => RuntimeInformation.OSArchitecture;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => JavaHints.DescribeArchitectureMismatch(value as string, SystemArchitectureProvider()) is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
