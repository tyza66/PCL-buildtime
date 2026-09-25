using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services;

public sealed partial class SessionState : ObservableObject
{
    public event EventHandler<string>? VersionInstalled;

    [ObservableProperty]
    private MinecraftVersion? _selectedVersion;

    public void NotifyVersionInstalled(string versionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        VersionInstalled?.Invoke(this, versionId);
    }
}
