namespace PCL.Avalonia.Services;

public interface IFilePickerService
{
    Task<string?> PickFileAsync(
        string title,
        IReadOnlyList<FilePickerFilter> filters,
        CancellationToken cancellationToken = default);
}

public sealed record FilePickerFilter(string Name, IReadOnlyList<string> Patterns);
