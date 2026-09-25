namespace PCL.Avalonia.ViewModels;

public sealed class NavItemViewModel
{
    public NavItemViewModel(string title, object page)
    {
        Title = title;
        Page = page;
    }

    public string Title { get; }

    public object Page { get; }
}
