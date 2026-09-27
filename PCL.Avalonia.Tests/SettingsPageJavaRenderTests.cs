using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PCL.Avalonia.Converters;
using PCL.Avalonia.Services;
using PCL.Avalonia.ViewModels.Pages;
using PCL.Avalonia.Views.Pages;

namespace PCL.Avalonia.Tests;

/// <summary>
/// 设置页 Java 列表的架构警示标记：绑定写错只是界面看不见，功能层测试摸不到，
/// 必须真渲染一次才能证明"与本机架构不一致"会亮起来。
/// </summary>
public sealed class SettingsPageJavaRenderTests
{
    [AvaloniaFact]
    public void SettingsPage_ShowsArchitectureWarning_WhenJavaDoesNotMatchSystem()
    {
        var previousProvider = JavaArchitectureMismatchConverter.SystemArchitectureProvider;
        // 固定把"本机"说成 arm64，灌一条 x64 的 Java 进去，不管测试机真实是什么芯片结果都稳定。
        JavaArchitectureMismatchConverter.SystemArchitectureProvider = () => Architecture.Arm64;
        var window = new Window();
        try
        {
            var viewModel = new SettingsPageViewModel(
                new StubSettingsService(new AppSettings { JavaPath = "/opt/java/bin/java" }),
                new StubPlatformService(),
                new StubThemeService(),
                new StubJavaListService(new JavaInfo("/opt/java/bin/java", "21.0.2", "x64", 21, true)),
                new ImmediateDispatcher());
            var view = new SettingsPageView { DataContext = viewModel };
            window.Content = view;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var flag = view.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(block => block.Text as string == "与本机架构不一致");
            Assert.True(flag is not null, "架构不符的 Java 条目没有渲染出警示标记");
            Assert.True(flag!.IsVisible, "架构不符时警示标记应该可见");
        }
        finally
        {
            JavaArchitectureMismatchConverter.SystemArchitectureProvider = previousProvider;
            window.Close();
        }
    }

    private sealed class StubSettingsService(AppSettings settings) : ISettingsService
    {
        public AppSettings Load() => settings;

        public void Save(AppSettings settings)
        {
        }
    }

    private sealed class StubPlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class StubThemeService : IThemeService
    {
        public void Apply(bool useDarkTheme)
        {
        }
    }

    private sealed class StubJavaListService(params JavaInfo[] items) : IJavaListService
    {
        public IReadOnlyList<JavaInfo> Scan() => items;

        public JavaInfo? GetJava(string path) => null;

        public void Refresh()
        {
        }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public void Debounce(string key, TimeSpan delay, Action action)
        {
        }
    }
}
