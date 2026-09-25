using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PCL.Avalonia.Views.Pages;

public sealed partial class ResourceDownloadPageView : UserControl
{
    public ResourceDownloadPageView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
