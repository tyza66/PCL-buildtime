using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;

namespace PCL.Avalonia.Tests;

/// <summary>
/// Toolbar rhythm checks against the real Avalonia layout engine: an input and its neighbouring
/// buttons must share one height and one vertical center, whatever the title block above them does
/// to the row, and the gaps between neighbours must match the declared column spacing.
/// </summary>
public sealed class FormControlLayoutTests
{
    private static void Arrange(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ToolbarButtonColumnsFillTheirSpacing()
    {
        // Installer toolbars keep the input and its action buttons in one row of Auto columns, so
        // the leftover space lands on the input instead of stretching the gaps between buttons.
        AssertToolbar("FabricLoaderPageView.axaml", "*,240,Auto,Auto");
        AssertToolbar("ForgelikeLoaderPageView.axaml", "*,220,Auto,Auto");
    }

    /// <summary>
    /// Download pages moved their filters into a right-aligned cluster: fixed-width combo columns
    /// used to leave uneven gaps next to the search box, so only the cluster owns a width now.
    /// </summary>
    [AvaloniaFact]
    public void DownloadFilterClustersFillTheirRow()
    {
        AssertToolbar("ModsDownloadPageView.axaml", "*,Auto");
        AssertToolbar("ResourceDownloadPageView.axaml", "*,Auto");
        AssertToolbar("IntegrationPacksPageView.axaml", "*,Auto");
    }

    private static void AssertToolbar(string pageFile, string expectedColumns)
    {
        var path = Path.Combine(RepoRoot(), "PCL.Avalonia", "Views", "Pages", pageFile);
        Assert.True(File.Exists(path), $"missing {path}");
        var xaml = File.ReadAllText(path);
        Assert.Contains($"ColumnDefinitions=\"{expectedColumns}\"", xaml, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void TextBoxStyleSuppressesFluentThemesEnglishMenu()
    {
        var path = Path.Combine(RepoRoot(), "PCL.Avalonia", "Themes", "AppStyles.axaml");
        Assert.True(File.Exists(path), $"missing {path}");
        var xaml = File.ReadAllText(path);
        // ChineseTextBoxMenu owns the edit menu; FluentTheme's English Cut/Copy/Paste flyout must stay off.
        Assert.Contains("<Setter Property=\"ContextFlyout\" Value=\"{x:Null}\" />", xaml, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PCL.Avalonia.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [AvaloniaFact]
    public void InputAndButtonsShareOneHeight()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(20),
        };
        var textBox = new TextBox { Width = 240 };
        var comboBox = new ComboBox { Width = 120 };
        var button = new Button { Content = "安装所选", Width = 96 };
        panel.Children.Add(textBox);
        panel.Children.Add(comboBox);
        panel.Children.Add(button);
        var window = new Window { Width = 640, Height = 140, Content = panel };

        Arrange(window);

        // The shared form control rhythm: 34 device-independent pixels.
        Assert.Equal(34, textBox.Bounds.Height, 3);
        Assert.Equal(34, comboBox.Bounds.Height, 3);
        Assert.Equal(34, button.Bounds.Height, 3);
        Assert.Equal(textBox.Bounds.Height, comboBox.Bounds.Height, 3);
        Assert.Equal(textBox.Bounds.Height, button.Bounds.Height, 3);
    }

    [AvaloniaFact]
    public void ToolbarRowKeepsControlsAlignedWithTitleBlock()
    {
        // Fixed-width button columns left the leftover space between neighbours, so buttons sit in
        // Auto columns and ColumnSpacing is the only gap.
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,240,Auto,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 14),
        };
        var title = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = "Fabric", FontSize = 26 },
                new TextBlock { Text = "请先填写游戏版本", FontSize = 13 },
            },
        };
        var textBox = new TextBox { Watermark = "游戏版本，如 1.20.1" };
        var install = new Button { Content = "安装所选" };
        var refresh = new Button { Content = "刷新" };
        Grid.SetColumn(textBox, 1);
        Grid.SetColumn(install, 2);
        Grid.SetColumn(refresh, 3);
        row.Children.Add(title);
        row.Children.Add(textBox);
        row.Children.Add(install);
        row.Children.Add(refresh);

        var window = new Window { Width = 960, Height = 220, Content = row };

        Arrange(window);

        // The two-line title block must not stretch the controls: same height, same center line.
        Assert.Equal(textBox.Bounds.Height, install.Bounds.Height, 3);
        Assert.Equal(install.Bounds.Height, refresh.Bounds.Height, 3);
        Assert.Equal(34, textBox.Bounds.Height, 3);
        Assert.Equal(34, install.Bounds.Height, 3);
        Assert.Equal(textBox.Bounds.Center.Y, install.Bounds.Center.Y, 1.5);
        Assert.Equal(install.Bounds.Center.Y, refresh.Bounds.Center.Y, 1.5);

        // Uniform gaps between neighbours.
        Assert.Equal(8, install.Bounds.Left - textBox.Bounds.Right, 3);
        Assert.Equal(8, refresh.Bounds.Left - install.Bounds.Right, 3);
    }
}
