using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Platform;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class VersionPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IVersionCatalogService _catalog;
    private readonly SessionState _session;
    private readonly IPlatformService _platform;
    private readonly IVersionManagerService _versionManager;
    private readonly IFolderOpener _folderOpener;
    private readonly IJavaService _javaService;
    private readonly IGameLauncher _launcher;
    private readonly ILaunchScriptExporter _scriptExporter;
    private readonly List<VersionItemViewModel> _allVersions = [];

    public VersionPageViewModel(
        ISettingsService settingsService,
        IVersionCatalogService catalog,
        SessionState session,
        IPlatformService platform,
        IVersionManagerService versionManager,
        IFolderOpener folderOpener,
        IJavaService javaService,
        IGameLauncher launcher,
        ILaunchScriptExporter scriptExporter)
    {
        _settingsService = settingsService;
        _catalog = catalog;
        _session = session;
        _platform = platform;
        _versionManager = versionManager;
        _folderOpener = folderOpener;
        _javaService = javaService;
        _launcher = launcher;
        _scriptExporter = scriptExporter;
        _session.VersionInstalled += OnVersionInstalled;
        Refresh();
    }

    public ObservableCollection<VersionItemViewModel> Versions { get; } = [];

    [ObservableProperty]
    private VersionItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _descriptionInput = "";

    public MinecraftVersion? SelectedVersion => SelectedItem?.Version;

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var versions = _catalog.Scan(folder);
            _allVersions.Clear();
            foreach (var version in versions)
            {
                _allVersions.Add(new VersionItemViewModel(
                    version,
                    _versionManager.LoadSettings(folder, version.Id),
                    ToggleFavorite,
                    ToggleHidden,
                    Delete,
                    OpenFolder));
            }

            ApplyFilter();
            StatusMessage = $"已找到 {_allVersions.Count} 个版本：{folder}";
        }
        catch (Exception ex)
        {
            StatusMessage = "读取版本失败：" + ex.Message;
        }
    }

    partial void OnShowHiddenChanged(bool value)
    {
        ApplyFilter();
    }

    partial void OnSelectedItemChanged(VersionItemViewModel? value)
    {
        _session.SelectedVersion = value?.Version;
        DescriptionInput = value?.Description ?? "";
    }

    [RelayCommand]
    private void Rename()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var newId = _versionManager.Rename(folder, version.Id, NewName);
            NewName = "";
            Refresh();
            SelectVersion(newId);
            StatusMessage = $"重命名成功：{newId}";
        }
        catch (Exception ex)
        {
            StatusMessage = "重命名失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void SaveDescription()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            _versionManager.SetDescription(folder, item.Id, DescriptionInput);
            item.Apply(_versionManager.LoadSettings(folder, item.Id));
            StatusMessage = "描述已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存描述失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void ExportScript()
    {
        var version = SelectedVersion;
        if (version is null)
        {
            return;
        }

        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var java = _javaService.ResolveJavaExecutable(settings);
            if (java is null)
            {
                StatusMessage = "未找到 Java，请先在设置页配置 Java 路径";
                return;
            }

            var plan = _launcher.BuildLaunchPlan(version, settings, java, _session.SelectedAccount);
            var fileName = OperatingSystem.IsWindows() ? "launch.bat" : "launch.sh";
            var result = _scriptExporter.Export(plan, Path.Combine(folder, fileName));
            StatusMessage = $"已导出启动脚本：{result}";
        }
        catch (Exception ex)
        {
            StatusMessage = "导出启动脚本失败：" + ex.Message;
        }
    }

    private void ToggleFavorite(VersionItemViewModel item)
    {
        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var next = !item.IsFavorite;
            _versionManager.SetFavorite(folder, item.Id, next);
            item.Apply(_versionManager.LoadSettings(folder, item.Id));
            StatusMessage = next ? $"已收藏 {item.Id}" : $"已取消收藏 {item.Id}";
        }
        catch (Exception ex)
        {
            StatusMessage = "收藏操作失败：" + ex.Message;
        }
    }

    private void ToggleHidden(VersionItemViewModel item)
    {
        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var next = !item.IsHidden;
            _versionManager.SetHidden(folder, item.Id, next);
            item.Apply(_versionManager.LoadSettings(folder, item.Id));
            StatusMessage = next ? $"已隐藏 {item.Id}" : $"已显示 {item.Id}";
            if (next && !ShowHidden)
            {
                ApplyFilter();
                if (ReferenceEquals(SelectedItem, item))
                {
                    SelectedItem = Versions.FirstOrDefault();
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "隐藏操作失败：" + ex.Message;
        }
    }

    private void Delete(VersionItemViewModel item)
    {
        try
        {
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            _versionManager.Delete(folder, item.Id);
            _allVersions.Remove(item);
            Versions.Remove(item);
            if (ReferenceEquals(SelectedItem, item))
            {
                SelectedItem = Versions.FirstOrDefault();
            }

            StatusMessage = $"已删除 {item.Id}";
        }
        catch (Exception ex)
        {
            StatusMessage = "删除版本失败：" + ex.Message;
        }
    }

    private void OpenFolder(VersionItemViewModel item)
    {
        try
        {
            _folderOpener.Open(item.Version.Folder);
        }
        catch (Exception ex)
        {
            StatusMessage = "打开文件夹失败：" + ex.Message;
        }
    }

    private void ApplyFilter()
    {
        Versions.Clear();
        foreach (var item in _allVersions)
        {
            if (ShowHidden || !item.IsHidden)
            {
                Versions.Add(item);
            }
        }

        if (SelectedItem is null || !Versions.Contains(SelectedItem))
        {
            SelectedItem = Versions.FirstOrDefault();
        }
    }

    private void SelectVersion(string? versionId)
    {
        var item = Versions.FirstOrDefault(
            candidate => string.Equals(candidate.Id, versionId, StringComparison.OrdinalIgnoreCase))
            ?? Versions.FirstOrDefault();
        SelectedItem = item;
    }

    private string GetMinecraftFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
    }

    private void OnVersionInstalled(object? sender, string versionId)
    {
        Refresh();
        SelectVersion(versionId);
    }
}

public sealed partial class VersionItemViewModel : ObservableObject
{
    private readonly Action<VersionItemViewModel> _toggleFavorite;
    private readonly Action<VersionItemViewModel> _toggleHidden;
    private readonly Action<VersionItemViewModel> _delete;
    private readonly Action<VersionItemViewModel> _openFolder;

    public VersionItemViewModel(
        MinecraftVersion version,
        VersionSettings settings,
        Action<VersionItemViewModel> toggleFavorite,
        Action<VersionItemViewModel> toggleHidden,
        Action<VersionItemViewModel> delete,
        Action<VersionItemViewModel> openFolder)
    {
        Version = version;
        _toggleFavorite = toggleFavorite;
        _toggleHidden = toggleHidden;
        _delete = delete;
        _openFolder = openFolder;
        IsFavorite = settings.IsFavorite;
        IsHidden = settings.IsHidden;
        Description = settings.Description;
    }

    public MinecraftVersion Version { get; }

    public string Id => Version.Id;

    public string Type => Version.Type;

    public string ReleaseTimeText => Version.ReleaseTimeText;

    public string? InheritsFrom => Version.InheritsFrom;

    public string FavoriteButtonText => IsFavorite ? "取消收藏" : "收藏";

    public string HiddenButtonText => IsHidden ? "显示" : "隐藏";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteButtonText))]
    private bool _isFavorite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenButtonText))]
    private bool _isHidden;

    [ObservableProperty]
    private string _description = "";

    [RelayCommand]
    private void Favorite() => _toggleFavorite(this);

    [RelayCommand]
    private void Hidden() => _toggleHidden(this);

    [RelayCommand]
    private void Delete() => _delete(this);

    [RelayCommand]
    private void OpenFolder() => _openFolder(this);

    public void Apply(VersionSettings settings)
    {
        IsFavorite = settings.IsFavorite;
        IsHidden = settings.IsHidden;
        Description = settings.Description;
    }
}
