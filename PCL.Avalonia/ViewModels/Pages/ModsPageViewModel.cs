using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class ModsPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModsService _modsService;
    private readonly IPlatformService _platform;
    private readonly SessionState _session;
    private readonly IVersionManagerService _versionManager;
    private readonly List<ModItemViewModel> _allMods = [];

    public ModsPageViewModel(
        ISettingsService settingsService,
        IModsService modsService,
        IPlatformService platformService,
        SessionState session,
        IVersionManagerService versionManager)
    {
        _settingsService = settingsService;
        _modsService = modsService;
        _platform = platformService;
        _session = session;
        _versionManager = versionManager;
        // 启动页切换选中版本后，Mod 列表要跟着换成该版本目录里的内容。
        _session.PropertyChanged += OnSessionPropertyChanged;
        Refresh();
    }

    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionState.SelectedVersion))
        {
            Refresh();
        }
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
            var (resolved, warning) = ResolveModsFolder();
            var scanned = _modsService.Scan(resolved.ModsFolder);
            _allMods.Clear();
            foreach (var mod in scanned)
            {
                _allMods.Add(new ModItemViewModel(mod, ToggleAsync, DeleteAsync));
            }

            ApplyFilter();
            // 隔离设置读取失败的提示不能丢掉，拼在数量行后面，用户知道判定规则退过一步。
            var status = resolved.Isolated
                ? $"已找到 {_allMods.Count} 个 Mod（当前版本隔离目录）：{resolved.ModsFolder}"
                : $"已找到 {_allMods.Count} 个 Mod：{resolved.ModsFolder}";
            StatusMessage = warning is null ? status : status + "（" + warning + "）";
        }
        catch (Exception ex)
        {
            StatusMessage = "读取 Mod 失败：" + ErrorMessageFormatter.Describe(ex);
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
            // 启用/禁用跟删除是两码事，把armed的确认态收掉，避免按钮一直红着。
            item.CancelDeleteConfirmation();
            StatusMessage = updated.IsEnabled ? $"已启用 {item.DisplayName}" : $"已禁用 {item.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = "切换 Mod 失败：" + ErrorMessageFormatter.Describe(ex);
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

        // 第一击只武装确认态、不碰磁盘：列表里误触一下不该让 Mod 文件直接没了。
        if (!item.IsConfirmingDelete)
        {
            item.EnterDeleteConfirmation();
            StatusMessage = $"再点一次「确认删除」删除 {item.DisplayName}，点错的话刷新列表即可取消";
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
            StatusMessage = "删除 Mod 失败：" + ErrorMessageFormatter.Describe(ex);
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

    /// <summary>
    /// 按当前选中版本解析 Mod 目录：与启动逻辑同一套隔离判定，隔离版本的 Mod
    /// 在 versions/&lt;版本名&gt;/mods 下。每版本设置读取失败时按全局默认判定，
    /// 不阻断列表刷新。
    /// </summary>
    private (ModsFolderResolver.Resolved Resolved, string? Warning) ResolveModsFolder()
    {
        var settings = _settingsService.Load();
        var gameFolder = GetMinecraftFolder(settings);
        var version = _session.SelectedVersion;
        if (version is null)
        {
            return (ModsFolderResolver.Resolve(gameFolder, null, settings.VersionIsolationDefault), null);
        }

        VersionSettings? versionSettings = null;
        string? warning = null;
        try
        {
            versionSettings = _versionManager.LoadSettings(gameFolder, version.Id);
        }
        catch (Exception ex)
        {
            // 每版本设置读不出来（外置卷 I/O 抖动、配置损坏）时不阻断刷新，
            // 按全局默认规则判定，并把原因带回状态行告知用户。
            warning = $"读取版本隔离设置失败，已按默认规则判断：{ErrorMessageFormatter.Brief(ex)}";
        }

        var resolved = ModsFolderResolver.Resolve(
            gameFolder,
            version,
            settings.VersionIsolationDefault,
            versionSettings);
        return (resolved, warning);
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

    /// <summary>第一击只武装确认态，按钮变红字"确认删除"，第二击才真的删文件。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonText))]
    private bool _isConfirmingDelete;

    public string DeleteButtonText => IsConfirmingDelete ? "确认删除" : "删除";

    public void EnterDeleteConfirmation() => IsConfirmingDelete = true;

    public void CancelDeleteConfirmation() => IsConfirmingDelete = false;

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
