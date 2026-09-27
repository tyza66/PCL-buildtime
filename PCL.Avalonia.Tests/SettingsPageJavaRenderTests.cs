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

    [AvaloniaFact]
    public async Task SettingsPage_ShowsOneClickJavaInstall_WhenJavaIsMissing()
    {
        var window = new Window();
        try
        {
            var javaList = new MutableJavaListService();
            var installer = new StubJavaInstallService();
            var viewModel = new SettingsPageViewModel(
                new StubSettingsService(new AppSettings()),
                new StubPlatformService(),
                new StubThemeService(),
                javaList,
                new ImmediateDispatcher(),
                installer,
                21);
            var view = new SettingsPageView { DataContext = viewModel };
            window.Content = view;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var button = FindButton(view, "下载并安装 Java 21");
            Assert.True(button is not null, "缺 Java 时设置页应给出一键安装按钮");
            var panel = view.FindControl<StackPanel>("JavaInstallPanel");
            Assert.NotNull(panel);
            Assert.True(panel!.IsVisible, "有 Java 缺口时一键安装面板应该可见");

            await viewModel.InstallJavaCommand.ExecuteAsync(21);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(21, installer.InstalledMajor);
            Assert.Equal("/java/jdk-21/bin/java", viewModel.JavaPath);

            // 列表重扫到刚装的这条 Java 之后按钮就该收起来，否则用户会再下一份 180MB。
            javaList.Set(new JavaInfo("/java/jdk-21/bin/java", "21.0.1", "aarch64", 21, true));
            viewModel.RefreshJavaListCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(viewModel.ShowJavaInstallButton);
            Assert.False(panel.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static Button? FindButton(SettingsPageView view, string content)
        => view.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Content as string == content);

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

    /// <summary>内容能在用例中途替换：按钮该不该收起来，取决于装完之后列表里有没有出现新 Java。</summary>
    private sealed class MutableJavaListService : IJavaListService
    {
        private readonly List<JavaInfo> _items = [];

        public IReadOnlyList<JavaInfo> Scan() => _items;

        public void Set(params JavaInfo[] items)
        {
            _items.Clear();
            _items.AddRange(items);
        }

        public JavaInfo? GetJava(string path) => null;

        public void Refresh()
        {
        }
    }

    private sealed class StubJavaInstallService : IJavaInstallService
    {
        public int InstalledMajor { get; private set; }

        public Task<JavaRelease> FetchLatestAsync(
            int majorVersion,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new JavaRelease(
                $"jdk-{majorVersion}.0.1+1",
                majorVersion,
                "https://example.invalid/jdk.tar.gz",
                1,
                "aarch64",
                "mac"));

        public Task<string> InstallAsync(
            int majorVersion,
            IProgress<JavaInstallProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstalledMajor = majorVersion;
            return Task.FromResult("/java/jdk-21/bin/java");
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
