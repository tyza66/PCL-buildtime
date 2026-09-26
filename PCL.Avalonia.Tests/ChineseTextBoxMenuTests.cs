using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PCL.Avalonia.Controls;

namespace PCL.Avalonia.Tests;

/// <summary>
/// The text box edit menu must be Chinese: FluentTheme's English Cut/Copy/Paste flyout is replaced,
/// and the items must act on the box they were opened for.
/// </summary>
public sealed class ChineseTextBoxMenuTests
{
    private static void Arrange(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ContextRequestOpensTheChineseMenu()
    {
        var textBox = new TextBox { Text = "1.20.1", Width = 240 };
        textBox.SelectionStart = 0;
        textBox.SelectionEnd = 6;
        var window = new Window { Width = 400, Height = 200, Content = new StackPanel { Children = { textBox } } };
        Arrange(window);
        ChineseTextBoxMenu.Attach(window);

        var args = new ContextRequestedEventArgs
        {
            RoutedEvent = Control.ContextRequestedEvent,
            Source = textBox,
        };
        textBox.RaiseEvent(args);
        Dispatcher.UIThread.RunJobs();

        Assert.True(args.Handled, "context request should be handled so FluentTheme's English flyout stays closed");

        var headers = window.GetVisualDescendants().OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList();
        Assert.Contains("剪切(_T)", headers);
        Assert.Contains("复制(_C)", headers);
        Assert.Contains("粘贴(_P)", headers);
        Assert.Contains("全选(_A)", headers);
        Assert.DoesNotContain("Cut", headers, StringComparer.Ordinal);
    }

    [AvaloniaFact]
    public void MenuItemsEnableThemselvesAgainstTheBox()
    {
        var textBox = new TextBox { Text = "hello" };
        var items = ChineseTextBoxMenu.Create(textBox).Items.OfType<MenuItem>().ToList();

        // No selection: cut and copy have nothing to act on.
        Assert.False(items.Single(i => Equals(i.Header, "剪切(_T)")).IsEnabled);
        Assert.False(items.Single(i => Equals(i.Header, "复制(_C)")).IsEnabled);
        Assert.True(items.Single(i => Equals(i.Header, "粘贴(_P)")).IsEnabled);
        Assert.True(items.Single(i => Equals(i.Header, "全选(_A)")).IsEnabled);

        textBox.SelectionStart = 0;
        textBox.SelectionEnd = 5;
        var withSelection = ChineseTextBoxMenu.Create(textBox).Items.OfType<MenuItem>().ToList();
        Assert.True(withSelection.Single(i => Equals(i.Header, "剪切(_T)")).IsEnabled);
        Assert.True(withSelection.Single(i => Equals(i.Header, "复制(_C)")).IsEnabled);
    }

    [AvaloniaFact]
    public void SelectAllItemSelectsTheWholeText()
    {
        var textBox = new TextBox { Text = "1.20.1" };
        var selectAll = ChineseTextBoxMenu.Create(textBox).Items.OfType<MenuItem>()
            .Single(i => Equals(i.Header, "全选(_A)"));

        selectAll.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(0, textBox.SelectionStart);
        Assert.Equal(textBox.Text.Length, textBox.SelectionEnd);
    }
}
