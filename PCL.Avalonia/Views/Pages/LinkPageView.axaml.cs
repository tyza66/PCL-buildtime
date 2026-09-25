using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Views.Pages;

public partial class LinkPageView : UserControl
{
    public LinkPageView()
    {
        InitializeComponent();
    }

    private void CopyInviteCode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LinkPageViewModel viewModel)
        {
            CopyText(viewModel.InviteCodeText);
        }
    }

    private void CopyAddress_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LinkPageViewModel viewModel && viewModel.Session is { ClientAddress.Length: > 0 } session)
        {
            CopyText(session.ClientAddress);
        }
    }

    private void CopyText(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            clipboard.SetTextAsync(text);
        }
    }
}
