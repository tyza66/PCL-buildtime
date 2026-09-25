using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using PCL.Avalonia.ViewModels.Pages;

namespace PCL.Avalonia.Views.Pages;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
    }

    private void SettingsTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SettingsPageViewModel viewModel
            && SettingsTabs.SelectedIndex >= 0
            && viewModel.Sections.Count > SettingsTabs.SelectedIndex)
        {
            viewModel.SelectedSection = viewModel.Sections[SettingsTabs.SelectedIndex];
        }
    }
}
