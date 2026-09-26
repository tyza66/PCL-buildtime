using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.Services.Platform;
using PCL.Avalonia.ViewModels.Pages;
using PCL.Avalonia.Views.Pages;

namespace PCL.Avalonia.Tests;

/// <summary>
/// Layout checks for the page frames themselves: download filter clusters, the launch page's
/// left column and the version page's segmented detail tabs. Every assertion runs against the
/// real Avalonia layout engine with the real page views and view models, so an overlap shows up
/// here instead of on the user's screen.
/// </summary>
public sealed class PageClusterLayoutTests
{
    private const double FormControlHeight = 34;

    [AvaloniaFact]
    public void VersionDetailTabsSitAboveTheirContentWithoutOverlap()
    {
        var view = new VersionPageView { DataContext = CreateVersionPageViewModel() };
        var window = Show(view);

        var strip = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.Children.OfType<ToggleButton>().Count() == 4);
        var segments = strip.Children.OfType<ToggleButton>().ToList();
        var detail = (Panel)strip.GetVisualParent()!;
        var content = (Panel)detail.Children[^1];

        // One baseline for all four segments, one even gap between neighbours.
        Assert.Equal(4, segments.Count);
        Assert.Single(segments.Select(segment => segment.Bounds.Bottom).Distinct());
        for (var i = 1; i < segments.Count; i++)
        {
            Assert.Equal(4, segments[i].Bounds.Left - segments[i - 1].Bounds.Right, 2);
        }

        var viewModel = (VersionPageViewModel)view.DataContext!;
        foreach (var tab in new[] { 0, 1, 2, 3 })
        {
            // XAML hands CommandParameter over as text, so the real view model path is exercised.
            viewModel.SelectDetailTabCommand.Execute(tab.ToString());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var shown = content.Children.Where(child => child.IsVisible).ToList();
            Assert.Single(shown);
            Assert.True(shown[0].Bounds.Height > 0, $"tab {tab} has no measurable content");

            // The board sits inside the fill panel, so lift its origin into the dock panel's space,
            // the one the segment strip was measured in.
            var origin = shown[0].TranslatePoint(new Point(0, 0), detail);
            Assert.True(
                origin.HasValue && origin.Value.Y >= strip.Bounds.Bottom - 1,
                $"tab {tab} content overlaps the segment strip");

            Assert.True(segments[tab].IsChecked, $"segment {tab} is not highlighted");
            foreach (var other in segments.Where(other => !ReferenceEquals(other, segments[tab])))
            {
                Assert.False(other.IsChecked, "another segment stayed highlighted");
            }
        }
    }

    [AvaloniaFact]
    public void VersionCardsPutTheirNameRowAboveEqualWidthActionRows()
    {
        var version = FakeVersion("1.20.1");
        var viewModel = CreateVersionPageViewModel(new StubVersionCatalogService(version));
        var view = new VersionPageView { DataContext = viewModel };
        viewModel.RefreshCommand.Execute(null);
        var window = Show(view);

        // "选择" only exists on a version card, so the row holding it is that card's first action row.
        var firstRow = (Grid)window.GetVisualDescendants().OfType<Button>()
            .First(button => button.Content as string == "选择")
            .GetVisualParent()!;
        // firstRow -> StackPanel -> Border: the card root is one level further up.
        var card = (Border)firstRow.GetVisualParent()!.GetVisualParent()!;
        var name = card.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.Text == version.Id);
        var stamped = card.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.Text == $"{version.Type} · {version.ReleaseTimeText}");
        var secondRow = (Grid)card.GetVisualDescendants().OfType<Button>()
            .First(button => button.Content as string == "删除")
            .GetVisualParent()!;

        // The old card shared one grid line between the info column and the action buttons, which
        // starved the version id and its release stamp down to a couple of glyphs. The info rows
        // now own the card's full inner width, with two equal-width action rows below them.
        var innerWidth = card.Bounds.Width - 20; // 10px of padding on each side
        Assert.Equal(innerWidth, name.Bounds.Width, 1);
        Assert.Equal(innerWidth, stamped.Bounds.Width, 1);
        Assert.True(
            Top(name, card) + name.Bounds.Height <= Top(firstRow, card) + 1,
            "the version name shares the row with the action buttons");
        Assert.True(
            Top(stamped, card) + stamped.Bounds.Height <= Top(firstRow, card) + 1,
            "the release stamp shares the row with the action buttons");
        Assert.True(
            Top(secondRow, card) >= Top(firstRow, card) + firstRow.Bounds.Height - 1,
            "the two action rows overlap");

        foreach (var row in new[] { firstRow, secondRow })
        {
            var buttons = row.Children.OfType<Button>().ToList();
            Assert.True(buttons.Count >= 2, "an action row needs at least two buttons");
            Assert.Single(buttons.Select(button => button.Bounds.Top).Distinct());
            Assert.Single(buttons.Select(button => button.Bounds.Width).Distinct());
            Assert.Single(buttons.Select(button => button.Bounds.Height).Distinct());
            for (var i = 1; i < buttons.Count; i++)
            {
                Assert.Equal(6, buttons[i].Bounds.Left - buttons[i - 1].Bounds.Right, 2);
            }
        }
    }

    [AvaloniaFact]
    public void LaunchPageLeftColumnKeepsItsRowsApart()
    {
        var window = Show(new LaunchPageView());

        var leftColumn = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.RowDefinitions.Count == 4);
        Assert.Equal(4, leftColumn.Children.Count);

        var header = leftColumn.Children[0];
        var list = leftColumn.Children[1];
        var javaRow = leftColumn.Children[2];
        var footer = leftColumn.Children[3];

        // A Panel with no Dock used to fall back to LastChildFill, which let the version list
        // share the row with the folder info block.
        Assert.True(list.Bounds.Top >= header.Bounds.Bottom - 1, "version list overlaps its header");
        Assert.True(javaRow.Bounds.Top >= list.Bounds.Bottom - 1, "java status overlaps the version list");
        Assert.True(footer.Bounds.Top >= javaRow.Bounds.Bottom - 1, "folder info overlaps the java status");
        Assert.Equal(leftColumn.Bounds.Bottom, footer.Bounds.Bottom, 1);
        Assert.True(list.Bounds.Height > header.Bounds.Height, "the list is not the row that stretches");
    }

    [AvaloniaFact]
    public void LaunchPageColumnsLineUpAcrossTheHeaderRow()
    {
        var window = Show(new LaunchPageView());

        var columns = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.ColumnDefinitions.Count == 2
                           && grid.Children.Count == 2
                           && grid.Children.All(child => child is Grid));
        var left = (Grid)columns.Children[0];
        var right = (Grid)columns.Children[1];

        var leftHeader = (Panel)left.Children[0];
        var rightHeader = (Panel)right.Children[0];
        var leftCard = ((Panel)left.Children[1]).Children.OfType<Border>().First();
        var rightCard = (Border)right.Children[1];

        // Both columns open with a header row of the same height, so the two cards below share one
        // top edge instead of starting at unrelated offsets.
        Assert.Equal(
            Top(leftHeader, columns) + leftHeader.Bounds.Height,
            Top(rightHeader, columns) + rightHeader.Bounds.Height, 1);
        Assert.Equal(Top(leftCard, columns), Top(rightCard, columns), 1);

        // The folder block is a single row: the path stays on one line beside its button rather
        // than wrapping into a tall card with a full-width action underneath.
        var javaRow = (Border)left.Children[2];
        var javaRowGrid = (Grid)javaRow.Child!;
        Assert.Equal(2, javaRowGrid.Children.Count);
        Assert.True(((TextBlock)javaRowGrid.Children[1]).Bounds.Height < FormControlHeight,
            "the java status text wrapped onto more lines");

        var folder = (Border)left.Children[3];
        var folderRow = (Grid)folder.Child!;
        var path = (TextBlock)folderRow.Children[0];
        var openFolder = (Button)folderRow.Children[1];
        Assert.Single(folderRow.Children.OfType<TextBlock>());
        Assert.True(path.Bounds.Height < FormControlHeight, "the folder path wrapped onto more lines");
        Assert.Equal(FormControlHeight, openFolder.Bounds.Height, 3);
        Assert.Equal(folderRow.Bounds.Height / 2, openFolder.Bounds.Center.Y, 1);
        Assert.Equal(folderRow.Bounds.Height / 2, path.Bounds.Center.Y, 1);
    }

    [AvaloniaFact]
    public void LaunchPageJavaStatusRowFlagsMissingJavaInDangerColor()
    {
        var view = new LaunchPageView
        {
            DataContext = CreateLaunchPageViewModel(javaPath: null)
        };
        var window = Show(view);

        var status = window.GetVisualDescendants().OfType<TextBlock>()
            .First(block => block.Text?.StartsWith("未检测到 Java", StringComparison.Ordinal) == true);

        Assert.Contains("javaalert", status.Classes);
        Assert.Equal(ThemeBrush("DangerBrush"), status.Foreground);
        Assert.Contains("设置页", status.Text!);
    }

    [AvaloniaFact]
    public void LaunchPageJavaStatusRowShowsTheResolvedJava()
    {
        var view = new LaunchPageView
        {
            DataContext = CreateLaunchPageViewModel(
                "/jdk25/bin/java",
                catalog: null,
                new JavaInfo("/jdk25/bin/java", "25.0.1", "aarch64", 25, true))
        };
        var window = Show(view);

        var status = window.GetVisualDescendants().OfType<TextBlock>()
            .First(block => block.Text?.StartsWith("Java ", StringComparison.Ordinal) == true);

        Assert.DoesNotContain("javaalert", status.Classes);
        Assert.Equal(ThemeBrush("TextSecondaryBrush"), status.Foreground);
        Assert.Contains("25.0.1", status.Text!);
        Assert.Contains("aarch64", status.Text!);
    }

    [AvaloniaFact]
    public async Task InstalledVersionListOpensAChineseContextMenu()
    {
        var version = FakeVersion("1.20.1");
        // 走真实的构造刷新，别在窗口 Show 之后再往列表里塞项——异步刷新会把它清掉。
        var viewModel = CreateLaunchPageViewModel(javaPath: null, new StubVersionCatalogService(version));
        var view = new LaunchPageView { DataContext = viewModel };
        var window = Show(view);
        for (var i = 0; i < 200 && viewModel.InstalledVersions.Count == 0; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.Single(viewModel.InstalledVersions);

        var item = window.GetVisualDescendants().OfType<ListBoxItem>().Single();
        // Flyout 挂在模板根 Grid 上，没打开时不在可视树里，只能顺着 ContextFlyout 属性摸过去。
        var flyout = item.GetVisualDescendants().OfType<Grid>()
            .Select(grid => grid.ContextFlyout)
            .OfType<MenuFlyout>()
            .Single();

        // 模板根 Grid 必须是可命中的：右键落在卡片留白里也要能弹菜单。
        var flyoutOwner = item.GetVisualDescendants().OfType<Grid>()
            .Single(grid => grid.ContextFlyout is MenuFlyout);
        Assert.NotNull(flyoutOwner.Background);
        Assert.Equal(new Thickness(0), flyoutOwner.Margin);
        // 12,9 的内边距改挂到子容器上，行高不变，但 Grid 自己铺满整行，右键处处都能弹菜单。
        Assert.Equal(new Thickness(12, 9), flyoutOwner.GetVisualDescendants().OfType<StackPanel>().First().Margin);
        Assert.Equal(item.Bounds.Height, flyoutOwner.Bounds.Height);
        Assert.Equal(item.Bounds.Width, flyoutOwner.Bounds.Width);

        // 真机上右键弹出时 Popup 会把 DataContext 接到版本实例上，测试里手动给菜单项补上，绑定才能求值。
        foreach (var menu in flyout.Items.OfType<MenuItem>())
        {
            menu.DataContext = item.DataContext;
        }

        Dispatcher.UIThread.RunJobs();
        var boundHeaders = flyout.Items.OfType<MenuItem>().Select(menu => menu.Header?.ToString()).ToList();
        Assert.Equal(new[] { "选择该版本", "收藏", "打开版本文件夹", "删除版本" }, boundHeaders);
        Assert.Contains(flyout.Items, entry => entry is Separator);

        // 每条菜单都要接到这个实例自己的命令上，右键才点得动；英文占位说明绑定断了。
        foreach (var menu in flyout.Items.OfType<MenuItem>())
        {
            Assert.NotNull(menu.Command);
        }

        // 收藏项文案跟着实例状态换：现在是「收藏」，收藏过就应该变「取消收藏」。
        var favoriteMenu = flyout.Items.OfType<MenuItem>().ElementAt(1);
        viewModel.InstalledVersions[0].IsFavorite = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("取消收藏", favoriteMenu.Header?.ToString());
    }

    private static double Top(Visual visual, Visual reference)
        => visual.TranslatePoint(new Point(0, 0), reference)?.Y ?? double.NaN;

    private static IBrush? ThemeBrush(string key)
        => Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var brush)
            ? (IBrush?)brush
            : null;

    [AvaloniaFact]
    public void DownloadFilterClustersKeepEvenGapsAndHugTheRightEdge()
    {
        AssertCluster(new ModsDownloadPageView());
        AssertCluster(new ResourceDownloadPageView());
        AssertCluster(new IntegrationPacksPageView());
    }

    private static void AssertCluster(UserControl page)
    {
        var window = Show(page);

        var cluster = window.GetVisualDescendants().OfType<StackPanel>()
            .First(panel => panel.Orientation == Orientation.Horizontal
                            && panel.Children.Any(child => child is ComboBox)
                            && panel.Children.Any(child => child is Button));
        var row = (Grid)cluster.GetVisualParent()!;
        var search = (TextBox)cluster.Children[0];

        // Same height for the input and every filter next to it.
        Assert.Equal(FormControlHeight, search.Bounds.Height, 3);
        foreach (var child in cluster.Children)
        {
            Assert.Equal(search.Bounds.Height, child.Bounds.Height, 3);
        }

        // One uniform gap between every neighbour, the search box included.
        for (var i = 1; i < cluster.Children.Count; i++)
        {
            Assert.Equal(8, cluster.Children[i].Bounds.Left - cluster.Children[i - 1].Bounds.Right, 2);
        }

        // Bounds are parent-relative: the cluster's own box is in the row's space, so its width is
        // what the children's edges are measured against, and its right edge is in the row's space.
        Assert.Equal(cluster.Bounds.Width, cluster.Children[^1].Bounds.Right, 2);
        Assert.Equal(0, cluster.Children[0].Bounds.Left, 2);
        Assert.Equal(row.Bounds.Right, cluster.Bounds.Right, 2);
        Assert.True(cluster.Children.Count >= 2, "cluster needs a filter and an action button");
        window.Close();
    }

    [AvaloniaFact]
    public void DangerBrushResolvesInBothThemes()
    {
        // The delete buttons pointed at a key that was never declared, so they rendered grey.
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(
                Application.Current!.TryFindResource("DangerBrush", theme, out var brush),
                $"DangerBrush is missing for the {theme} theme");
            Assert.IsType<SolidColorBrush>(brush);
        }
    }

    private static Window Show(object content)
    {
        var window = new Window { Width = 1200, Height = 820, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static VersionPageViewModel CreateVersionPageViewModel()
        => CreateVersionPageViewModel(new StubVersionCatalogService());

    private static VersionPageViewModel CreateVersionPageViewModel(IVersionCatalogService catalog) => new(
        new StubSettingsService(),
        catalog,
        new InstanceClassifier(),
        new SessionState(),
        new StubPlatformService(),
        new StubVersionManagerService(),
        new StubFolderOpener(),
        new StubJavaService(),
        new StubGameLauncher(),
        new StubScriptExporter(),
        new InstancePackExporter(),
        new StubModsService());

    private static MinecraftVersion FakeVersion(string id) => new()
    {
        Id = id,
        Folder = $"/games/mc/versions/{id}",
        JsonPath = $"/games/mc/versions/{id}/{id}.json",
        ReleaseTime = new DateTimeOffset(2026, 9, 15, 19, 23, 0, TimeSpan.Zero)
    };

    private static LaunchPageViewModel CreateLaunchPageViewModel(
        string? javaPath,
        IVersionCatalogService? catalog = null,
        params JavaInfo[] java) => new(
        new StubSettingsService(javaPath),
        // 没装 Java 时解析结果也要是空，设置页的路径和扫描列表两条路都堵上。
        new StubJavaService(javaPath),
        new StubGameLauncher(),
        new SessionState(),
        new StubDispatcher(),
        new StubMicrosoftAuthentication(),
        new StubAccountService(),
        new StubVersionManagerService(),
        catalog ?? new StubVersionCatalogService(),
        new StubPlatformService(),
        new StubFolderOpener(),
        new StubJavaListService(java));

    private sealed class StubSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;

        public StubSettingsService(string? javaPath = "/usr/bin/java")
            => _settings = new() { MinecraftFolder = "/games/mc", JavaPath = javaPath ?? "" };

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings)
        {
        }
    }

    private sealed class StubVersionCatalogService : IVersionCatalogService
    {
        private readonly MinecraftVersion[] _versions;

        public StubVersionCatalogService(params MinecraftVersion[] versions) => _versions = versions;

        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder) => _versions;

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id) => null;
    }

    private sealed class StubPlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class StubVersionManagerService : IVersionManagerService
    {
        public VersionSettings LoadSettings(string minecraftFolder, string versionId) => new();

        public void SetFavorite(string minecraftFolder, string versionId, bool isFavorite)
        {
        }

        public void SetHidden(string minecraftFolder, string versionId, bool isHidden)
        {
        }

        public void SetDisplayType(string minecraftFolder, string versionId, InstanceDisplayType displayType)
        {
        }

        public void SetDescription(string minecraftFolder, string versionId, string description)
        {
        }

        public void SetInstanceLaunchSettings(
            string minecraftFolder,
            string versionId,
            int? maxMemoryMb,
            string? javaPath,
            string? jvmArguments,
            string? gameArguments)
        {
        }

        public string Rename(string minecraftFolder, string versionId, string newName) => newName;

        public void Delete(string minecraftFolder, string versionId)
        {
        }
    }

    private sealed class StubFolderOpener : IFolderOpener
    {
        public void Open(string path)
        {
        }
    }

    private sealed class StubJavaService : IJavaService
    {
        private readonly string? _resolved;

        public StubJavaService(string? resolved = null) => _resolved = resolved;

        public string? ResolveJavaExecutable(AppSettings settings) => Resolve(settings);

        public string? ResolveJavaExecutable(AppSettings settings, int? requiredMajorVersion)
            => Resolve(settings);

        // 空白路径在真实实现里等于没设置，扫描落空返回 null；stub 照抄这个约定，
        // 否则"未检测到 Java"分支测不到。
        private string? Resolve(AppSettings settings)
            => !string.IsNullOrWhiteSpace(_resolved) ? _resolved
                : !string.IsNullOrWhiteSpace(settings.JavaPath) ? settings.JavaPath
                : null;
    }

    private sealed class StubJavaListService : IJavaListService
    {
        private readonly JavaInfo[] _items;

        public StubJavaListService(params JavaInfo[] items) => _items = items;

        public IReadOnlyList<JavaInfo> Scan() => _items;

        public JavaInfo? GetJava(string path) => _items.FirstOrDefault(item => item.Path == path);

        public void Refresh()
        {
        }
    }

    private sealed class StubDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public void Debounce(string key, TimeSpan delay, Action action) => action();
    }

    private sealed class StubMicrosoftAuthentication : IMicrosoftAuthenticationService
    {
        public Task<MicrosoftAccountSession> LoginAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<MicrosoftAccountSession?> RefreshAsync(
            Account account,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubAccountService : IAccountService
    {
        public IReadOnlyList<Account> Load() => [];

        public Account AddOfflineAccount(string name) => throw new NotSupportedException();

        public Account AddMicrosoftAccount(MicrosoftAccountSession session) => throw new NotSupportedException();

        public void RemoveAccount(Guid id) => throw new NotSupportedException();

        public void SetDefaultAccount(Guid id) => throw new NotSupportedException();

        public Account? GetDefaultAccount() => null;
    }

    private sealed class StubGameLauncher : IGameLauncher
    {
        public LaunchPlan BuildLaunchPlan(
            MinecraftVersion version,
            AppSettings settings,
            string javaExecutable,
            Account? account = null,
            VersionSettings? versionSettings = null)
            => throw new NotSupportedException();

        public IGameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null)
            => throw new NotSupportedException();
    }

    private sealed class StubScriptExporter : ILaunchScriptExporter
    {
        public string Export(LaunchPlan plan, string filePath) => filePath;
    }

    private sealed class StubModsService : IModsService
    {
        public IReadOnlyList<ModInfo> Scan(string minecraftFolder) => [];

        public ModInfo SetEnabled(ModInfo mod, bool enabled) => mod;

        public void Delete(ModInfo mod)
        {
        }
    }
}
