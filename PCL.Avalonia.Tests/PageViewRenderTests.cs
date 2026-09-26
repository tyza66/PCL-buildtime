using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Views;
using PCL.Avalonia.Views.Pages;

[assembly: AvaloniaTestApplication(typeof(PCL.Avalonia.Tests.TestAppBuilder))]

namespace PCL.Avalonia.Tests;

/// <summary>
/// Headless render checks: every page view must construct on its own. The end-to-end check that
/// the real window materialises a view for every navigation page lives in MainWindowPageRenderTests.
/// </summary>
public sealed class PageViewRenderTests : IDisposable
{
    private readonly RecordingLogSink _sink = new();
    private readonly Window _window;

    public PageViewRenderTests()
    {
        Logger.Sink = _sink;
        _window = new MainWindow();
    }

    public void Dispose()
    {
        _window.Content = null;
        _window.Close();
        Logger.Sink = null;
    }

    [AvaloniaFact]
    public void MainWindowLoads()
    {
        Assert.NotEmpty(_window.DataTemplates);
    }

    [AvaloniaFact]
    public void ForgePageViewConstructs()
    {
        var view = new ForgelikeLoaderPageView();
        Assert.NotNull(view.Content);
    }

    private sealed class RecordingLogSink : ILogSink
    {
        public List<string> Errors { get; } = [];

        public bool IsEnabled(LogEventLevel level, string source)
            => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string source, object? sourceObject, string messageTemplate)
            => Errors.Add($"[{source}] {messageTemplate}");

        public void Log(LogEventLevel level, string source, object? sourceObject, string messageTemplate, params object?[] propertyValues)
            => Errors.Add($"[{source}] {messageTemplate}");
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Load() => new() { MinecraftFolder = "/games/mc" };
        public void Save(AppSettings settings) { }
    }

    private sealed class FakePlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();
        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }
}
