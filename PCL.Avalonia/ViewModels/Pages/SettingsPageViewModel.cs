using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public SettingsPageViewModel(ISettingsService settingsService, IPlatformService platformService)
    {
        _settingsService = settingsService;
        var settings = settingsService.Load();
        MinecraftFolder = string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? platformService.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
        JavaPath = settings.JavaPath;
        UserName = string.IsNullOrWhiteSpace(settings.UserName) ? "Player" : settings.UserName;
        MaxMemoryMb = settings.MaxMemoryMb;
    }

    [ObservableProperty]
    private string _minecraftFolder = "";

    [ObservableProperty]
    private string _javaPath = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private int _maxMemoryMb = 4096;

    [ObservableProperty]
    private string _statusMessage = "";

    [RelayCommand]
    private void Save()
    {
        var current = _settingsService.Load();
        _settingsService.Save(current with
        {
            MinecraftFolder = MinecraftFolder.Trim(),
            JavaPath = JavaPath.Trim(),
            UserName = string.IsNullOrWhiteSpace(UserName) ? "Player" : UserName.Trim(),
            MaxMemoryMb = MaxMemoryMb,
        });
        StatusMessage = "设置已保存";
    }
}
