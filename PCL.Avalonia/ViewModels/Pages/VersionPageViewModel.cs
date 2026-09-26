using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.Services.Platform;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class VersionPageViewModel : ObservableObject
{
    private const string FollowGlobalId = "FollowGlobal";
    private const string AutoId = "Auto";
    private const string ManualId = "Manual";

    private readonly ISettingsService _settingsService;
    private readonly IVersionCatalogService _catalog;
    private readonly IInstanceClassifier _classifier;
    private readonly SessionState _session;
    private readonly IPlatformService _platform;
    private readonly IVersionManagerService _versionManager;
    private readonly IFolderOpener _folderOpener;
    private readonly IJavaService _javaService;
    private readonly IGameLauncher _launcher;
    private readonly ILaunchScriptExporter _scriptExporter;
    private readonly IInstancePackExporter _packExporter;
    private readonly IModsService _modsService;
    private readonly List<VersionItemViewModel> _allVersions = [];
    private string _currentFolder = "";
    private bool _isLoadingDetails;

    public VersionPageViewModel(
        ISettingsService settingsService,
        IVersionCatalogService catalog,
        IInstanceClassifier classifier,
        SessionState session,
        IPlatformService platform,
        IVersionManagerService versionManager,
        IFolderOpener folderOpener,
        IJavaService javaService,
        IGameLauncher launcher,
        ILaunchScriptExporter scriptExporter,
        IInstancePackExporter packExporter,
        IModsService modsService)
    {
        _settingsService = settingsService;
        _catalog = catalog;
        _classifier = classifier;
        _session = session;
        _platform = platform;
        _versionManager = versionManager;
        _folderOpener = folderOpener;
        _javaService = javaService;
        _launcher = launcher;
        _scriptExporter = scriptExporter;
        _packExporter = packExporter;
        _modsService = modsService;
        _session.VersionInstalled += OnVersionInstalled;
        Groups.Add(new InstanceGroupViewModel(InstanceGroup.Star, "收藏"));
        Groups.Add(new InstanceGroupViewModel(InstanceGroup.Api, "API"));
        Groups.Add(new InstanceGroupViewModel(InstanceGroup.OriginalLike, "常规版本"));
        Groups.Add(new InstanceGroupViewModel(InstanceGroup.Rubbish, "不常用"));
        Groups.Add(new InstanceGroupViewModel(InstanceGroup.Fool, "愚人节"));
        Groups.Add(new InstanceGroupViewModel(InstanceGroup.Error, "错误"));
        Groups.Add(new InstanceGroupViewModel(InstanceGroup.Hidden, "隐藏"));
        LoadFolders();
    }

    public ObservableCollection<FolderItemViewModel> Folders { get; } = [];

    public ObservableCollection<InstanceGroupViewModel> Groups { get; } = [];

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];

    public IReadOnlyList<VersionItemViewModel> Versions =>
        Groups.SelectMany(group => group.Items).ToList();

    public IReadOnlyList<DisplayTypeOption> DisplayTypeOptions { get; } =
    [
        new(InstanceDisplayType.Auto, "自动"),
        new(InstanceDisplayType.Original, "常规版本"),
        new(InstanceDisplayType.Api, "可安装 Mod"),
        new(InstanceDisplayType.Rubbish, "不常用版本"),
        new(InstanceDisplayType.Star, "收藏"),
        new(InstanceDisplayType.Fool, "愚人节版本"),
        new(InstanceDisplayType.Hidden, "从版本列表中隐藏"),
    ];

    public IReadOnlyList<MemoryModeOption> MemoryModeOptions { get; } =
    [
        new(FollowGlobalId, "跟随全局"),
        new(AutoId, "自动"),
        new(ManualId, "手动"),
    ];

    [ObservableProperty]
    private FolderItemViewModel? _selectedFolder;

    [ObservableProperty]
    private VersionItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private int _selectedDetailTab;

    public bool IsOverviewTab => SelectedDetailTab == 0;
    public bool IsModsTab => SelectedDetailTab == 1;
    public bool IsSettingsTab => SelectedDetailTab == 2;
    public bool IsExportTab => SelectedDetailTab == 3;

    partial void OnSelectedDetailTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsModsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
        OnPropertyChanged(nameof(IsExportTab));
    }

    /// <summary>
    /// XAML 把 CommandParameter 原样当成字符串送来，RelayCommand&lt;T&gt; 不会做类型转换，
    /// 所以这里按文本接参数。分段标签只走命令切换，避免 TwoWay 绑定回写选中态。
    /// </summary>
    [RelayCommand]
    private void SelectDetailTab(string index)
        => SelectedDetailTab = int.TryParse(index, out var parsed) ? parsed : SelectedDetailTab;

    [ObservableProperty]
    private string _descriptionInput = "";

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _newFolderName = "";

    [ObservableProperty]
    private string _newFolderPath = "";

    [ObservableProperty]
    private DisplayTypeOption _displayType = new(InstanceDisplayType.Auto, "自动");

    [ObservableProperty]
    private MemoryModeOption _memoryMode = new(FollowGlobalId, "跟随全局");

    [ObservableProperty]
    private string _maxMemoryInput = "";

    [ObservableProperty]
    private string _javaPathInput = "";

    [ObservableProperty]
    private string _jvmArgumentsInput = "";

    [ObservableProperty]
    private string _gameArgumentsInput = "";

    [ObservableProperty]
    private string _exportName = "";

    [ObservableProperty]
    private string _modsStatus = "";

    [ObservableProperty]
    private bool _isExporting;

    public MinecraftVersion? SelectedVersion => SelectedItem?.Version;

    public string CurrentFolder => _currentFolder;

    partial void OnSelectedFolderChanged(FolderItemViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _currentFolder = value.Path;
        Refresh();
    }

    partial void OnShowHiddenChanged(bool value)
    {
        ApplyGroups();
    }

    partial void OnSelectedItemChanged(VersionItemViewModel? value)
    {
        _session.SelectedVersion = value?.Version;
        LoadSelectedDetails(value);
    }

    partial void OnDisplayTypeChanged(DisplayTypeOption value)
    {
        if (_isLoadingDetails || SelectedItem is null)
        {
            return;
        }

        try
        {
            _versionManager.SetDisplayType(CurrentFolder, SelectedItem.Id, value.Value);
            var updated = _versionManager.LoadSettings(CurrentFolder, SelectedItem.Id);
            SelectedItem.Apply(updated);
            ApplyGroups();
            StatusMessage = $"实例分类已改为“{value.Label}”";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存分类失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        if (string.IsNullOrWhiteSpace(CurrentFolder))
        {
            return;
        }

        try
        {
            var versions = _catalog.Scan(CurrentFolder);
            _allVersions.Clear();
            foreach (var version in versions)
            {
                _allVersions.Add(new VersionItemViewModel(
                    version,
                    _versionManager.LoadSettings(CurrentFolder, version.Id),
                    ToggleFavorite,
                    ToggleHidden,
                    Delete,
                    OpenVersionItemFolder,
                    SelectVersionItem));
            }

            ApplyGroups();
            StatusMessage = $"已找到 {_allVersions.Count} 个版本：{CurrentFolder}";
        }
        catch (Exception ex)
        {
            StatusMessage = "读取版本失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        var path = NewFolderPath.Trim();
        if (path.Length == 0)
        {
            StatusMessage = "请先填写游戏目录路径";
            return;
        }

        var normalized = NormalizeFolderPath(path);
        if (Folders.Any(folder => string.Equals(folder.Path, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "该游戏目录已在列表中";
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewFolderName)
            ? RecommendFolderName(normalized)
            : NewFolderName.Trim();
        var current = _settingsService.Load();
        _settingsService.Save(current with
        {
            LaunchFolders = current.LaunchFolders
                .Append(new MinecraftFolder(name, normalized))
                .ToArray(),
        });
        NewFolderName = "";
        NewFolderPath = "";
        LoadFolders();
        SelectedFolder = Folders.FirstOrDefault(folder =>
            string.Equals(folder.Path, normalized, StringComparison.OrdinalIgnoreCase));
        StatusMessage = $"已添加游戏目录：{normalized}";
    }

    [RelayCommand]
    private void RemoveFolder()
    {
        var folder = SelectedFolder;
        if (folder is null)
        {
            return;
        }

        if (folder.IsDefault)
        {
            StatusMessage = "默认游戏目录不能移除";
            return;
        }

        var current = _settingsService.Load();
        _settingsService.Save(current with
        {
            LaunchFolders = current.LaunchFolders
                .Where(item => !string.Equals(item.Path, folder.Path, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        });
        LoadFolders();
        StatusMessage = $"已从列表移除：{folder.Name}";
    }

    [RelayCommand]
    private void OpenCurrentFolder()
    {
        var path = SelectedFolder?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _folderOpener.Open(path);
        }
        catch (Exception ex)
        {
            StatusMessage = "打开目录失败：" + ex.Message;
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
            _versionManager.SetDescription(CurrentFolder, item.Id, DescriptionInput);
            item.Apply(_versionManager.LoadSettings(CurrentFolder, item.Id));
            StatusMessage = "描述已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存描述失败：" + ex.Message;
        }
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
            var newId = _versionManager.Rename(CurrentFolder, version.Id, NewName);
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
    private void SaveInstanceSettings()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        int? memory;
        if (MemoryMode.Id == ManualId)
        {
            if (!int.TryParse(MaxMemoryInput.Trim(), out var value) || value < 256)
            {
                StatusMessage = "手动内存需要填写不小于 256 的数字";
                return;
            }

            memory = value;
        }
        else if (MemoryMode.Id == AutoId)
        {
            memory = 0;
        }
        else
        {
            memory = null;
        }

        try
        {
            _versionManager.SetInstanceLaunchSettings(
                CurrentFolder,
                item.Id,
                memory,
                JavaPathInput.Trim(),
                JvmArgumentsInput.Trim(),
                GameArgumentsInput.Trim());
            item.Apply(_versionManager.LoadSettings(CurrentFolder, item.Id));
            LoadSelectedDetails(item);
            StatusMessage = "实例设置已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = "保存实例设置失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void ResetInstanceSettings()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        try
        {
            _versionManager.SetDisplayType(CurrentFolder, item.Id, InstanceDisplayType.Auto);
            _versionManager.SetFavorite(CurrentFolder, item.Id, false);
            _versionManager.SetHidden(CurrentFolder, item.Id, false);
            _versionManager.SetDescription(CurrentFolder, item.Id, "");
            _versionManager.SetInstanceLaunchSettings(CurrentFolder, item.Id, null, "", "", "");
            item.Apply(_versionManager.LoadSettings(CurrentFolder, item.Id));
            LoadSelectedDetails(item);
            ApplyGroups();
            StatusMessage = $"已重置 {item.Id} 的独立设置";
        }
        catch (Exception ex)
        {
            StatusMessage = "重置实例设置失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void ExportScript()
    {
        var item = SelectedItem;
        if (item is null)
        {
            return;
        }

        try
        {
            var settings = _settingsService.Load();
            var java = _javaService.ResolveJavaExecutable(settings);
            if (java is null)
            {
                StatusMessage = "未找到 Java，请先在设置页配置 Java 路径";
                return;
            }

            var plan = _launcher.BuildLaunchPlan(
                item.Version,
                settings,
                java,
                _session.SelectedAccount,
                item.Settings);
            var fileName = OperatingSystem.IsWindows() ? "launch.bat" : "launch.sh";
            var result = _scriptExporter.Export(plan, Path.Combine(CurrentFolder, fileName));
            StatusMessage = $"已导出启动脚本：{result}";
        }
        catch (Exception ex)
        {
            StatusMessage = "导出启动脚本失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportPackAsync()
    {
        var item = SelectedItem;
        if (item is null || IsExporting)
        {
            return;
        }

        var displayName = ExportName.Trim();
        if (displayName.Length == 0)
        {
            StatusMessage = "请填写整合包名称";
            return;
        }

        IsExporting = true;
        try
        {
            var fileName = SanitizeFileName(displayName);
            if (fileName.Length == 0)
            {
                StatusMessage = "整合包名称全部由非法字符组成";
                return;
            }

            var directory = Path.Combine(CurrentFolder, "PCL", "Export");
            var outputPath = Path.Combine(directory, fileName + ".zip");
            var result = await Task.Run(() =>
                _packExporter.Export(CurrentFolder, item.Id, displayName, outputPath));
            StatusMessage = $"已导出整合包：{result}";
            _folderOpener.Open(directory);
        }
        catch (Exception ex)
        {
            StatusMessage = "导出整合包失败：" + ex.Message;
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void OpenVersionFolder()
    {
        if (SelectedItem is null)
        {
            return;
        }

        OpenChildFolder(Path.Combine("versions", SelectedItem.Id));
    }

    [RelayCommand]
    private void OpenSavesFolder()
    {
        OpenChildFolder("saves");
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        OpenChildFolder("mods");
    }

    [RelayCommand]
    private void OpenScreenshotsFolder()
    {
        OpenChildFolder("screenshots");
    }

    private void ToggleFavorite(VersionItemViewModel item)
    {
        try
        {
            var next = !item.IsFavorite;
            _versionManager.SetFavorite(CurrentFolder, item.Id, next);
            item.Apply(_versionManager.LoadSettings(CurrentFolder, item.Id));
            ApplyGroups();
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
            var next = !item.IsHidden;
            _versionManager.SetHidden(CurrentFolder, item.Id, next);
            item.Apply(_versionManager.LoadSettings(CurrentFolder, item.Id));
            ApplyGroups();
            StatusMessage = next ? $"已隐藏 {item.Id}" : $"已显示 {item.Id}";
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
            _versionManager.Delete(CurrentFolder, item.Id);
            _allVersions.Remove(item);
            ApplyGroups();
            StatusMessage = $"已删除 {item.Id}";
        }
        catch (Exception ex)
        {
            StatusMessage = "删除版本失败：" + ex.Message;
        }
    }

    private void OpenVersionItemFolder(VersionItemViewModel item)
    {
        OpenChildFolder(Path.Combine("versions", item.Id));
    }

    private void SelectVersionItem(VersionItemViewModel item)
    {
        SelectedItem = item;
    }

    private void SelectVersion(string? versionId)
    {
        var item = _allVersions.FirstOrDefault(
            candidate => string.Equals(candidate.Id, versionId, StringComparison.OrdinalIgnoreCase))
            ?? _allVersions.FirstOrDefault();
        SelectedItem = item;
    }

    private void LoadSelectedDetails(VersionItemViewModel? item)
    {
        Mods.Clear();
        if (item is null)
        {
            ModsStatus = "";
            return;
        }

        _isLoadingDetails = true;
        try
        {
            var settings = item.Settings;
            DescriptionInput = settings.Description;
            DisplayType = DisplayTypeOptions.First(option => option.Value == settings.DisplayType);
            MemoryMode = MemoryModeOptions.First(option =>
                option.Id == ResolveMemoryModeId(settings.MaxMemoryMb));
            MaxMemoryInput = settings.MaxMemoryMb is > 0 ? settings.MaxMemoryMb.Value.ToString() : "";
            JavaPathInput = settings.JavaPath ?? "";
            JvmArgumentsInput = settings.JvmArguments ?? "";
            GameArgumentsInput = settings.GameArguments ?? "";
            ExportName = item.Id;
        }
        finally
        {
            _isLoadingDetails = false;
        }

        LoadMods();
    }

    private void LoadMods()
    {
        Mods.Clear();
        var item = SelectedItem;
        if (item is null)
        {
            ModsStatus = "";
            return;
        }

        try
        {
            foreach (var mod in _modsService.Scan(CurrentFolder))
            {
                Mods.Add(new ModItemViewModel(mod, ToggleModAsync, DeleteModAsync));
            }

            ModsStatus = $"已找到 {Mods.Count} 个 Mod";
        }
        catch (Exception ex)
        {
            ModsStatus = "读取 Mod 失败：" + ex.Message;
        }
    }

    private async Task ToggleModAsync(ModItemViewModel item)
    {
        try
        {
            var updated = await Task.Run(() =>
                _modsService.SetEnabled(item.Mod, !item.IsEnabled));
            item.Apply(updated);
            ModsStatus = updated.IsEnabled ? $"已启用 {item.DisplayName}" : $"已禁用 {item.DisplayName}";
        }
        catch (Exception ex)
        {
            ModsStatus = "切换 Mod 失败：" + ex.Message;
        }
    }

    private async Task DeleteModAsync(ModItemViewModel item)
    {
        try
        {
            await Task.Run(() => _modsService.Delete(item.Mod));
            Mods.Remove(item);
            ModsStatus = $"已删除 {item.DisplayName}";
        }
        catch (Exception ex)
        {
            ModsStatus = "删除 Mod 失败：" + ex.Message;
        }
    }

    private void ApplyGroups()
    {
        foreach (var group in Groups)
        {
            group.Items.Clear();
        }

        var groups = _classifier.Group(
            _allVersions.Select(item => new VersionInstance(item.Version, item.Settings)),
            ShowHidden);
        var itemsByVersion = _allVersions.ToDictionary(item => item.Version);
        foreach (var group in groups)
        {
            foreach (var instance in group.Value)
            {
                Groups[(int)group.Key].Items.Add(itemsByVersion[instance.Version]);
            }
        }

        foreach (var group in Groups)
        {
            group.IsVisible = group.Items.Count > 0;
            group.HeaderText = group.Group == InstanceGroup.Star
                ? group.Title
                : $"{group.Title} ({group.Items.Count})";
        }

        var visible = Versions.ToList();
        var selected = SelectedItem;
        if (selected is null || !visible.Contains(selected))
        {
            SelectedItem = visible.FirstOrDefault();
        }

        OnPropertyChanged(nameof(Versions));
    }

    private void LoadFolders()
    {
        var settings = _settingsService.Load();
        var defaultPath = GetDefaultGameFolder(settings);
        var previous = SelectedFolder?.Path;
        Folders.Clear();
        Folders.Add(new FolderItemViewModel("默认", defaultPath, true));
        foreach (var folder in settings.LaunchFolders)
        {
            if (string.Equals(folder.Path, defaultPath, StringComparison.OrdinalIgnoreCase)
                || Folders.Any(existing => string.Equals(existing.Path, folder.Path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(folder.Name)
                ? RecommendFolderName(folder.Path)
                : folder.Name.Trim();
            Folders.Add(new FolderItemViewModel(name, folder.Path));
        }

        SelectedFolder = Folders.FirstOrDefault(folder =>
            !string.IsNullOrWhiteSpace(previous)
            && string.Equals(folder.Path, previous, StringComparison.OrdinalIgnoreCase))
            ?? Folders.FirstOrDefault();
    }

    private void OpenChildFolder(string relativePath)
    {
        if (SelectedVersion is null)
        {
            return;
        }

        try
        {
            _folderOpener.Open(Path.Combine(CurrentFolder, relativePath));
        }
        catch (Exception ex)
        {
            StatusMessage = "打开文件夹失败：" + ex.Message;
        }
    }

    private void OnVersionInstalled(object? sender, string versionId)
    {
        Refresh();
        SelectVersion(versionId);
    }

    private string GetDefaultGameFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
    }

    private static string NormalizeFolderPath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string RecommendFolderName(string path)
    {
        var normalized = NormalizeFolderPath(path);
        var name = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(name) ? "新游戏目录" : name;
    }

    private static string ResolveMemoryModeId(int? memory)
    {
        return memory switch
        {
            0 => AutoId,
            > 0 => ManualId,
            _ => FollowGlobalId,
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder();
        foreach (var character in value)
        {
            if (!invalid.Contains(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }
}

public sealed record FolderItemViewModel(string Name, string Path, bool IsDefault = false);

public sealed partial class InstanceGroupViewModel : ObservableObject
{
    public InstanceGroupViewModel(InstanceGroup group, string title)
    {
        Group = group;
        Title = title;
    }

    public InstanceGroup Group { get; }

    public string Title { get; }

    public ObservableCollection<VersionItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _headerText = "";
}

public sealed partial class VersionItemViewModel : ObservableObject
{
    private readonly Action<VersionItemViewModel> _toggleFavorite;
    private readonly Action<VersionItemViewModel> _toggleHidden;
    private readonly Action<VersionItemViewModel> _delete;
    private readonly Action<VersionItemViewModel> _openFolder;
    private readonly Action<VersionItemViewModel> _select;

    public VersionItemViewModel(
        MinecraftVersion version,
        VersionSettings settings,
        Action<VersionItemViewModel> toggleFavorite,
        Action<VersionItemViewModel> toggleHidden,
        Action<VersionItemViewModel> delete,
        Action<VersionItemViewModel> openFolder,
        Action<VersionItemViewModel> select)
    {
        Version = version;
        Settings = settings;
        IsFavorite = settings.IsFavorite;
        IsHidden = settings.IsHidden;
        Description = settings.Description;
        DisplayType = settings.DisplayType;
        _toggleFavorite = toggleFavorite;
        _toggleHidden = toggleHidden;
        _delete = delete;
        _openFolder = openFolder;
        _select = select;
    }

    public MinecraftVersion Version { get; }

    public VersionSettings Settings { get; private set; }

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

    [ObservableProperty]
    private InstanceDisplayType _displayType;

    [RelayCommand]
    private void Favorite() => _toggleFavorite(this);

    [RelayCommand]
    private void Hidden() => _toggleHidden(this);

    [RelayCommand]
    private void Delete() => _delete(this);

    [RelayCommand]
    private void OpenFolder() => _openFolder(this);

    [RelayCommand]
    private void Select() => _select(this);

    public void Apply(VersionSettings settings)
    {
        Settings = settings;
        IsFavorite = settings.IsFavorite;
        IsHidden = settings.IsHidden;
        Description = settings.Description;
        DisplayType = settings.DisplayType;
    }
}

public sealed record DisplayTypeOption(InstanceDisplayType Value, string Label);

public sealed record MemoryModeOption(string Id, string Label);
