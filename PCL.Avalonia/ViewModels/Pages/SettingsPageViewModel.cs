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
        DownloadSource = DownloadSources.First(option => option.Source == settings.DownloadSource);
        JvmArguments = settings.JvmArguments;
        GameArguments = settings.GameArguments;
    }

    public IReadOnlyList<DownloadSourceOption> DownloadSources { get; } =
    [
        new(PCL.Avalonia.Services.DownloadSource.Bmclapi, "BMCLAPI（推荐）"),
        new(PCL.Avalonia.Services.DownloadSource.Mojang, "Mojang 官方"),
    ];

    [ObservableProperty]
    private string _minecraftFolder = "";

    [ObservableProperty]
    private string _javaPath = "";

    [ObservableProperty]
    private string _userName = "";

    [ObservableProperty]
    private int _maxMemoryMb = 4096;

    [ObservableProperty]
    private string _jvmArguments = "";

    [ObservableProperty]
    private string _gameArguments = "";

    [ObservableProperty]
    private DownloadSourceOption _downloadSource = new(PCL.Avalonia.Services.DownloadSource.Bmclapi, "BMCLAPI（推荐）");

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
            DownloadSource = DownloadSource.Source,
            JvmArguments = JvmArguments.Trim(),
            GameArguments = GameArguments.Trim(),
        });
        StatusMessage = "设置已保存";
    }
}

public sealed record DownloadSourceOption(DownloadSource Source, string Label);
