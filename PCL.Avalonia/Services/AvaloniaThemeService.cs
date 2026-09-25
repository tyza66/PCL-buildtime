using Avalonia;
using Avalonia.Styling;

namespace PCL.Avalonia.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    public void Apply(bool useDarkTheme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = useDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
