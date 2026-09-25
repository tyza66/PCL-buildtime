using CommunityToolkit.Mvvm.ComponentModel;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.Services;

public sealed partial class SessionState : ObservableObject
{
    [ObservableProperty]
    private MinecraftVersion? _selectedVersion;
}
