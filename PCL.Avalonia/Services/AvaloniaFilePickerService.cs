using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace PCL.Avalonia.Services;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickFileAsync(
        string title,
        IReadOnlyList<FilePickerFilter> filters,
        CancellationToken cancellationToken = default)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: not null } desktop)
        {
            return null;
        }

        var storageProvider = desktop.MainWindow.StorageProvider;
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
                .Select(filter => new FilePickerFileType(filter.Name)
                {
                    Patterns = filter.Patterns.ToList(),
                })
                .ToList(),
        };
        var files = await storageProvider.OpenFilePickerAsync(options).ConfigureAwait(true);
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
