using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class VersionPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IVersionCatalogService _catalog;
    private readonly SessionState _session;
    private readonly IPlatformService _platform;

    public VersionPageViewModel(
        ISettingsService settingsService,
        IVersionCatalogService catalog,
        SessionState session,
        IPlatformService platform)
    {
        _settingsService = settingsService;
        _catalog = catalog;
        _session = session;
        _platform = platform;
        _session.VersionInstalled += OnVersionInstalled;
        Refresh();
    }

    public ObservableCollection<MinecraftVersion> Versions { get; } = [];

    [ObservableProperty]
    private MinecraftVersion? _selectedVersion;

    [ObservableProperty]
    private string _statusMessage = "";

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var settings = _settingsService.Load();
            var folder = string.IsNullOrWhiteSpace(settings.MinecraftFolder)
                ? _platform.GetDefaultMinecraftFolder()
                : settings.MinecraftFolder;
            var versions = _catalog.Scan(folder);
            Versions.Clear();
            foreach (var version in versions)
            {
                Versions.Add(version);
            }

            SelectedVersion = Versions.FirstOrDefault();
            StatusMessage = $"已找到 {Versions.Count} 个版本：{folder}";
        }
        catch (Exception ex)
        {
            StatusMessage = "读取版本失败：" + ex.Message;
        }
    }

    partial void OnSelectedVersionChanged(MinecraftVersion? value)
    {
        _session.SelectedVersion = value;
    }

    private void OnVersionInstalled(object? sender, string versionId)
    {
        Refresh();
        SelectedVersion = Versions.FirstOrDefault(
            version => string.Equals(version.Id, versionId, StringComparison.OrdinalIgnoreCase));
    }
}
