using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class ModsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModsService _modsService;
    private readonly IPlatformService _platform;
    private readonly List<ModItemViewModel> _allMods = [];

    public ModsPageViewModel(
        ISettingsService settingsService,
        IModsService modsService,
        IPlatformService platformService)
    {
        _settingsService = settingsService;
        _modsService = modsService;
        _platform = platformService;
        Refresh();
    }

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private void Refresh()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var folder = GetMinecraftFolder(_settingsService.Load());
            var scanned = _modsService.Scan(folder);
            _allMods.Clear();
            foreach (var mod in scanned)
            {
                _allMods.Add(new ModItemViewModel(mod, ToggleAsync, DeleteAsync));
            }

            ApplyFilter();
            StatusMessage = $"已找到 {_allMods.Count} 个 Mod：{Path.Combine(folder, "mods")}";
        }
        catch (Exception ex)
        {
            StatusMessage = "读取 Mod 失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private async Task ToggleAsync(ModItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = await Task.Run(() => _modsService.SetEnabled(item.Mod, !item.IsEnabled));
            item.Apply(updated);
            StatusMessage = updated.IsEnabled ? $"已启用 {item.DisplayName}" : $"已禁用 {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "切换 Mod 失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync(ModItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _modsService.Delete(item.Mod));
            _allMods.Remove(item);
            Mods.Remove(item);
            StatusMessage = $"已删除 {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "删除 Mod 失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        Mods.Clear();
        foreach (var item in _allMods)
        {
            if (string.IsNullOrWhiteSpace(query)
                || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Mods.Add(item);
            }
        }
    }

    private string GetMinecraftFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
    }
}

public sealed partial class ModItemViewModel : ObservableObject
{
    private readonly Func<ModItemViewModel, Task> _toggle;
    private readonly Func<ModItemViewModel, Task> _delete;

    public ModItemViewModel(
        ModInfo mod,
        Func<ModItemViewModel, Task> toggle,
        Func<ModItemViewModel, Task> delete)
    {
        Mod = mod;
        DisplayName = mod.DisplayName;
        SizeText = FormatSize(mod.SizeBytes);
        LastModifiedText = mod.LastModifiedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        IsEnabled = mod.IsEnabled;
        _toggle = toggle;
        _delete = delete;
    }

    public ModInfo Mod { get; private set; }

    public string DisplayName { get; }

    public string SizeText { get; }

    public string LastModifiedText { get; }

    public string EnabledText => IsEnabled ? "已启用" : "已禁用";

    public string ToggleButtonText => IsEnabled ? "禁用" : "启用";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnabledText))]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    private bool _isEnabled;

    [RelayCommand]
    private Task Toggle() => _toggle(this);

    [RelayCommand]
    private Task Delete() => _delete(this);

    public void Apply(ModInfo updated)
    {
        Mod = updated;
        IsEnabled = updated.IsEnabled;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
