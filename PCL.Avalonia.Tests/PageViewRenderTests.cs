using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Threading;
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
    public void ConfirmWindowRendersTitleMessageAndBothButtons()
    {
        var window = new ConfirmWindow("删除版本", "确定要删除 1.20.1 吗？该版本无法恢复。", "确定删除");
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
        Assert.Contains("删除版本", texts);
        Assert.Contains("确定要删除 1.20.1 吗？该版本无法恢复。", texts);

        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(2, buttons.Count);
        // 两个按钮必须等高：取消和确认高度不一是最显眼的毛刺。
        Assert.Equal(buttons[0].Bounds.Height, buttons[1].Bounds.Height);
        Assert.True(buttons[0].Bounds.Height > 0, "buttons have not been laid out yet");
        Assert.Contains(buttons, button => (string?)button.Content == "取消");
        // 确认键文案由调用方给，"确定删除"比光秃秃的"确定"更让玩家放心。
        Assert.Contains(buttons, button => (string?)button.Content == "确定删除");

        // 换行后的正文也不能撑破窗口客户区。
        var message = window.GetVisualDescendants().OfType<TextBlock>()
            .First(block => block.Text?.Contains("1.20.1", StringComparison.Ordinal) == true);
        Assert.True(message.Bounds.Right <= window.ClientSize.Width, "确认正文溢出窗口");

        window.Close(false);
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
