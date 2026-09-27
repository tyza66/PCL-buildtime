namespace PCL.Avalonia.ViewModels;

public sealed class NavItemViewModel
{
    public NavItemViewModel(string title, object page, bool isEnabled = true, string? disabledHint = null)
    {
        Title = title;
        Page = page;
        IsEnabled = isEnabled;
        DisabledHint = disabledHint;
    }

    public string Title { get; }

    public object Page { get; }

    public bool IsEnabled { get; }

    public string? DisabledHint { get; }
}
