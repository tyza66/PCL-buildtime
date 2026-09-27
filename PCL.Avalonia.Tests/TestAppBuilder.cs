using Avalonia;
using Avalonia.Headless;

namespace PCL.Avalonia.Tests;

public static class TestAppBuilder
{
    // UseSkia + 关闭无头绘图替身：测试里量到、画到的是真实排版与像素结果。
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
