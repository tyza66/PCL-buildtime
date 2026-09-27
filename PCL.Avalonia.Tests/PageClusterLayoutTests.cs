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
            // 星号列按可用宽度整除，除不尽时最后 1px 会落到某一列上，所以按 1px 容差比较。
            Assert.True(
                buttons.Max(button => button.Bounds.Width) - buttons.Min(button => button.Bounds.Width) <= 1,
                "the buttons in an action row do not share one width");
            Assert.Single(buttons.Select(button => button.Bounds.Height).Distinct());
            // A label that wraps onto two lines grows its button, so this keeps them one line tall.
            Assert.Equal(FormControlHeight, buttons[0].Bounds.Height, 3);
            for (var i = 1; i < buttons.Count; i++)
            {
                Assert.Equal(6, buttons[i].Bounds.Left - buttons[i - 1].Bounds.Right, 2);
            }
        }
    }

    [AvaloniaFact]
    public void VersionPageColumnsEachKeepTheirOwnLane()
    {
        var version = FakeVersion("1.20.1");
        var viewModel = CreateVersionPageViewModel(new StubVersionCatalogService(version));
        var view = new VersionPageView { DataContext = viewModel };
        viewModel.RefreshCommand.Execute(null);
        var window = Show(view);

        var folderCard = window.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.Text == "游戏目录")
            .GetVisualAncestors().OfType<Border>().First();
        var grid = (Grid)folderCard.GetVisualParent()!;
        // 直接按 Border 搜会先命中版本列那一层（"选择"按钮的祖先也算"包含它"），
        // 所以从按钮出发向上取第一层 Border 才是卡片，再往上找直接挂在 grid 上的列。
        var card = window.GetVisualDescendants().OfType<Button>()
            .First(button => button.Content as string == "选择")
            .GetVisualAncestors().OfType<Border>().First();
        var versionColumn = card.GetVisualAncestors().OfType<Border>()
            .First(border => ReferenceEquals(border.GetVisualParent(), grid));
        var detailCard = window.GetVisualDescendants().OfType<ToggleButton>()
            .First(toggle => toggle.Content as string == "总体")
            .GetVisualAncestors()
            .First(ancestor => ReferenceEquals(ancestor.GetVisualParent(), grid));

        // The version column used to be wrapped in a Panel, which silently dropped the
        // Grid.Column assignment and stacked the version list on top of the folder column.
        // 版本详情卡里新增了版本隔离下拉与状态说明，列宽从 190/280 放宽到 200/330 才不裁字。
        Assert.Equal(200d, grid.ColumnDefinitions[0].Width.Value, 1);
        Assert.Equal(330d, grid.ColumnDefinitions[1].Width.Value, 1);
        Assert.Equal(200d, folderCard.Bounds.Width, 1);
        Assert.Equal(330d, versionColumn.Bounds.Width, 1);

        var origin = new Point(0, 0);
        var folderLeft = folderCard.TranslatePoint(origin, window)!.Value;
        var versionLeft = versionColumn.TranslatePoint(origin, window)!.Value;
        var detailLeft = detailCard.TranslatePoint(origin, window)!.Value;
        var cardLeft = card.TranslatePoint(origin, window)!.Value;

        Assert.True(
            versionLeft.X >= folderLeft.X + folderCard.Bounds.Width,
            "the version column starts inside the folder column");
        Assert.True(
            detailLeft.X >= versionLeft.X + versionColumn.Bounds.Width,
            "the detail column starts inside the version column");
        Assert.True(
            cardLeft.X >= versionLeft.X,
            "the version card sits outside its own column");
        Assert.True(
            cardLeft.X + card.Bounds.Width <= versionLeft.X + versionColumn.Bounds.Width + 1,
            "the version card runs past the end of its column");

        var name = card.GetVisualDescendants().OfType<TextBlock>()
            .First(text => text.Text == version.Id);
        Assert.True(
            name.Bounds.Width >= 240,
            $"the version name only has {name.Bounds.Width:0.#}px to render in");
        window.Close();
    }

    [AvaloniaFact]
    public void VersionPageFolderButtonsSitInTwoRowsWithoutOverlap()
    {
        var version = FakeVersion("1.20.1");
        var viewModel = CreateVersionPageViewModel(new StubVersionCatalogService(version));
        var view = new VersionPageView { DataContext = viewModel };
        viewModel.RefreshCommand.Execute(null);
        var window = Show(view);

        var labels = new[] { "打开版本目录", "打开存档", "打开 Mod 目录", "打开截图" };
        var buttons = labels
            .Select(label => window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Content as string == label))
            .ToList();
        var rows = buttons.Select(button => (Grid)button.GetVisualParent()!).Distinct().ToList();

        // The grid used to declare Grid.Row without RowDefinitions, so all four buttons landed on
        // one line and the later two covered the first two outright.
        Assert.Single(rows);
        Assert.Equal(2, rows[0].RowDefinitions.Count);
        Assert.Single(buttons.Select(button => button.Bounds.Width).Distinct());
        Assert.Single(buttons.Select(button => button.Bounds.Height).Distinct());
        Assert.Equal(8, buttons[1].Bounds.Left - buttons[0].Bounds.Right, 2);
        Assert.Equal(8, buttons[3].Bounds.Left - buttons[2].Bounds.Right, 2);
        Assert.Equal(buttons[0].Bounds.Top, buttons[1].Bounds.Top, 1);
        Assert.Equal(buttons[2].Bounds.Top, buttons[3].Bounds.Top, 1);
        Assert.True(
            buttons[2].Bounds.Top >= buttons[0].Bounds.Bottom - 1,
            "the second row of folder buttons overlaps the first");
        window.Close();
    }

    [AvaloniaFact]
    public void VersionDetailTabsStayInsideTheDetailCardAtNarrowWidths()
    {
        // Window width minus the 220px sidebar, 56px of page margins and 24px of column gaps is
        // what the detail column gets: 1090 is the minimum window, so this is its worst case.
        foreach (var windowWidth in new[] { 1110d, 1300d, 1420d, 1720d })
        {
            var view = new VersionPageView { DataContext = CreateVersionPageViewModel() };
            var window = new Window { Width = windowWidth, Height = 820, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var strip = window.GetVisualDescendants().OfType<Grid>()
                .First(grid => grid.Children.OfType<ToggleButton>().Count() == 4);
            var segments = strip.Children.OfType<ToggleButton>().ToList();
            var card = (Border)strip.GetVisualParent()!.GetVisualParent()!;

            // Four labels with fixed widths used to overflow the card at 1090, pushing the last
            // segment under the card edge; the strip now shares the width evenly instead.
            foreach (var segment in segments)
            {
                var right = segment.TranslatePoint(new Point(segment.Bounds.Width, 0), card)!.Value.X;
                Assert.True(
                    right <= card.Bounds.Width + 1,
                    $"a tab segment runs {right - card.Bounds.Width:0.#}px past the detail card at width {windowWidth}");
                Assert.True(
                    segment.Bounds.Width > 0,
                    $"a tab segment collapsed to nothing at width {windowWidth}");
            }

            for (var i = 1; i < segments.Count; i++)
            {
                Assert.True(
                    segments[i].Bounds.Left >= segments[i - 1].Bounds.Right,
                    $"tab segments overlap at width {windowWidth}");
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HiddenVersionGroupsLeaveNoGapInTheList()
    {
        var version = FakeVersion("1.20.1");
        var viewModel = CreateVersionPageViewModel(new StubVersionCatalogService(version));
        var view = new VersionPageView { DataContext = viewModel };
        viewModel.RefreshCommand.Execute(null);
        var window = Show(view);

        var emptyGroups = viewModel.Groups.Where(group => group.Items.Count == 0).ToList();
        Assert.NotEmpty(emptyGroups);

        foreach (var group in emptyGroups)
        {
            var panel = window.GetVisualDescendants().OfType<StackPanel>()
                .FirstOrDefault(panel => ReferenceEquals(panel.DataContext, group));
            Assert.True(panel is not null, $"no panel rendered for the empty {group.HeaderText} group");
            // IsVisible used to sit on the header only, so an empty group kept its 12px margin.
            Assert.Equal(0, panel!.Bounds.Height, 1);
            Assert.False(panel.IsVisible);
        }
        window.Close();
    }

    [AvaloniaFact]
    public void LaunchPageLeftColumnKeepsItsRowsApart()
    {
        var window = Show(new LaunchPageView());

        // 左栏正文三行：版本卡片、Java 状态、目录块。版本卡片占 * 行，接管左栏剩余高度，
        // 只留紧凑的 Java / 目录行贴在底端，版面底部不再空出一大块。
        var leftBody = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.RowDefinitions.Count == 3
                           && grid.ColumnDefinitions.Count == 0
                           && grid.Children.Count == 3
                           && grid.Children[0] is Panel);
        Assert.Equal(GridUnitType.Star, leftBody.RowDefinitions[0].Height.GridUnitType);
        Assert.True(leftBody.RowDefinitions[1].Height.GridUnitType == GridUnitType.Auto, "java row should hug its content");
        Assert.True(leftBody.RowDefinitions[2].Height.GridUnitType == GridUnitType.Auto, "folder row should hug its content");
        Assert.Equal(3, leftBody.Children.Count);

        var list = leftBody.Children[0];
        var javaRow = leftBody.Children[1];
        var folder = leftBody.Children[2];

        Assert.True(javaRow.Bounds.Top >= list.Bounds.Bottom - 1, "java status overlaps the version list");
        Assert.True(folder.Bounds.Top >= javaRow.Bounds.Bottom - 1, "folder info overlaps the java status");
        Assert.True(list.Bounds.Height > 0, "the version list collapsed");
        // 版本卡片上端贴着页眉下沿、下端贴着 Java 行上沿：左栏这 200 多像素必须用它吃掉。
        Assert.True(
            list.Bounds.Bottom + leftBody.RowSpacing >= javaRow.Bounds.Top - 1,
            $"the version list stops at {list.Bounds.Bottom} but the java row starts at {javaRow.Bounds.Top}");
        Assert.True(
            list.Bounds.Height >= 200,
            $"the version list only takes {list.Bounds.Height} of the column and leaves a slab below");
        Assert.True(
            folder.Bounds.Bottom <= leftBody.Bounds.Bottom + 1,
            "the folder card overflows the column");
    }

    [AvaloniaFact]
    public void LaunchPageColumnsLineUpAcrossTheHeaderRow()
    {
        var window = Show(new LaunchPageView());

        // 两栏页眉同处一行：四个子元素依次是左页眉、右页眉、左正文、右日志。
        var columns = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.ColumnDefinitions.Count == 2
                           && grid.RowDefinitions.Count == 2
                           && grid.Children.Count == 4);
        var leftHeader = (Panel)columns.Children[0];
        var rightHeader = (Panel)columns.Children[1];
        var leftBody = (Grid)columns.Children[2];
        var rightLog = (Panel)columns.Children[3];

        var leftCard = ((Panel)leftBody.Children[0]).Children.OfType<Border>().First();
        var rightCard = rightLog.Children.OfType<Border>().First();

        // Both columns open with a header row of the same height, so the two cards below share one
        // top edge instead of starting at unrelated offsets. 页眉共处一行之后，卡片的上沿是
        // 同一行排出来的，同一个 Rect，不再允许各自舍入差出 1px。
        var lh = (Grid)leftHeader;
        var rh = (Grid)rightHeader;
        Assert.Equal(lh.Bounds.Height, rh.Bounds.Height, 1);
        Assert.True(
            Math.Abs(Top(leftCard, columns) - Top(rightCard, columns)) < 0.5,
            $"left card sits at {Top(leftCard, columns)} but right card at {Top(rightCard, columns)}");

        // Java 状态还是一行讲完：只报版本和架构，完整路径进 ToolTip，不给左列添堵。
        var javaRow = (Border)leftBody.Children[1];
        var javaRowGrid = (Grid)javaRow.Child!;
        Assert.Equal(2, javaRowGrid.Children.Count);
        Assert.True(((TextBlock)javaRowGrid.Children[1]).Bounds.Height < FormControlHeight,
            "the java status text wrapped onto more lines");

        // 目录块拆三行：标签 + 按钮、路径、隔离判定。路径终于能换行，不再被省略号啃成
        // "/Users/tyza66/Libr..." 这种看不出是什么的半截字。
        var folder = (Border)leftBody.Children[2];
        var folderGrid = (Grid)folder.Child!;
        Assert.Equal(3, folderGrid.RowDefinitions.Count);
        Assert.Equal(3, folderGrid.Children.Count);

        var labelRow = (Grid)folderGrid.Children[0];
        var folderLabel = (TextBlock)labelRow.Children[0];
        var openFolder = (Button)labelRow.Children[1];
        Assert.Equal("游戏目录", folderLabel.Text);
        Assert.Single(labelRow.Children.OfType<TextBlock>());
        Assert.Equal(FormControlHeight, openFolder.Bounds.Height, 3);
        // 子像素舍入会让两者差 0.5px 左右，真正要防的是标签被挤到按钮上方另一条线上。
        Assert.True(
            Math.Abs(folderLabel.Bounds.Center.Y - openFolder.Bounds.Center.Y) < 1,
            $"the folder label sits at {folderLabel.Bounds.Center.Y} but the button at {openFolder.Bounds.Center.Y}");
        // 按钮贴卡片右沿，和上方"刷新"按钮落在同一条竖线上。
        Assert.Equal(labelRow.Bounds.Width, openFolder.Bounds.Right, 1);

        // 路径最多折两行，再多就是版面失控；宽度也不许越过卡片内边距。
        var path = (TextBlock)folderGrid.Children[1];
        Assert.True(path.Bounds.Height <= FormControlHeight, "the folder path grew past two lines");
        Assert.True(path.Bounds.Width <= labelRow.Bounds.Width + 1, "the folder path overflows the card");

        // 版本隔离状态挂在路径行正下方，自成一行：既说明存档写在哪，又不挤占路径行。
        var isolationStatus = (TextBlock)folderGrid.Children[2];
        Assert.True(
            Top(isolationStatus, folderGrid) >= Top(path, folderGrid) + path.Bounds.Height - 1,
            "the isolation status row overlaps the folder path row");
        Assert.True(
            isolationStatus.Bounds.Bottom <= folderGrid.Bounds.Height + 1,
            "the isolation status row overflows the card");
    }

    [AvaloniaFact]
    public void LaunchPageVersionCardFillsTheLeftColumn()
    {
        var window = Show(new LaunchPageView());

        var columns = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.ColumnDefinitions.Count == 2
                           && grid.RowDefinitions.Count == 2
                           && grid.Children.Count == 4);
        var leftBody = (Grid)columns.Children[2];

        // 版本卡片铺满所在行：空白留在卡片内部由滚动条解决，版面底部不再突然截断。
        var listPanel = (Panel)leftBody.Children[0];
        var listCard = listPanel.Children.OfType<Border>().First();
        Assert.True(
            Math.Abs(listCard.Bounds.Height - listPanel.Bounds.Height) < 1,
            $"the version card is {listCard.Bounds.Height} tall but was given {listPanel.Bounds.Height}");
        Assert.True(
            Math.Abs(listCard.Bounds.Width - listPanel.Bounds.Width) < 1,
            "the version card is narrower than its row");

        // 列表项离卡片边缘留了边，选中高亮不再啃到圆角上。
        var listBox = listCard.GetVisualDescendants().OfType<ListBox>().First();
        Assert.True(listBox.Padding.Left > 0, "list items touch the rounded card edge");
        Assert.True(listBox.Padding.Left <= 8, $"the list inset {listBox.Padding.Left} is too roomy");
    }

    [AvaloniaFact]
    public void LaunchPageLogPanelExplainsItselfWhileEmpty()
    {
        var viewModel = CreateLaunchPageViewModel(null);
        var view = new LaunchPageView { DataContext = viewModel };
        var window = Show(view);

        var columns = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.ColumnDefinitions.Count == 2
                           && grid.RowDefinitions.Count == 2
                           && grid.Children.Count == 4);
        var logPanel = (Panel)columns.Children[3];
        var hint = logPanel.Children.OfType<TextBlock>().First();
        var logBox = logPanel.Children.OfType<Border>().First()
            .GetVisualDescendants().OfType<TextBox>().First();

        // 空日志时给一句说明，有输出后提示让位给真正的日志内容。
        Assert.True(hint.IsVisible, "the empty log panel shows no hint");
        Assert.False(viewModel.HasLogContent);
        Assert.Equal(0, logBox.Text?.Length ?? 1);

        viewModel.LogText = "[20:00:00] [main/INFO]: 第一行输出";
        window.UpdateLayout();

        Assert.True(viewModel.HasLogContent);
        Assert.False(hint.IsVisible, "the log hint stays on top of real output");
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
        new StubJavaListService(),
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

        public void SetInstanceIsolation(string minecraftFolder, string versionId, bool? independent)
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
