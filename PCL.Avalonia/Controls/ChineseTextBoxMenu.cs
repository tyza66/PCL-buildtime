using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace PCL.Avalonia.Controls;

/// <summary>
/// FluentTheme ships an English Cut/Copy/Paste flyout on every text box. This replaces it with a
/// Chinese menu. The menu is built in code rather than declared in a style so that each item acts on
/// the exact text box it was opened for, and so items can enable themselves against that box.
/// </summary>
public static class ChineseTextBoxMenu
{
    /// <summary>Answers every context request raised inside a top level, e.g. right click or the menu key.</summary>
    public static void Attach(TopLevel topLevel) =>
        topLevel.AddHandler(Control.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Bubble, true);

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not StyledElement source)
        {
            return;
        }

        var target = source.GetSelfAndLogicalAncestors().OfType<TextBox>().FirstOrDefault();
        if (target is null)
        {
            return;
        }

        // Stop FluentTheme's own flyout from appearing next to ours.
        e.Handled = true;
        Create(target).ShowAt(target, true);
    }

    /// <summary>Builds the Chinese edit menu for one text box. Exposed so the menu can be tested.</summary>
    public static MenuFlyout Create(TextBox target)
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(Item("剪切(_T)", target, () => target.Cut()));
        flyout.Items.Add(Item("复制(_C)", target, () => target.Copy()));
        flyout.Items.Add(Item("粘贴(_P)", target, () => target.Paste()));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item("全选(_A)", target, () => target.SelectAll()));
        return flyout;
    }

    private static MenuItem Item(string header, TextBox target, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        item.IsEnabled = header switch
        {
            "剪切(_T)" or "复制(_C)" => target.SelectionStart != target.SelectionEnd,
            "全选(_A)" => !string.IsNullOrEmpty(target.Text),
            _ => true,
        };
        return item;
    }
}
