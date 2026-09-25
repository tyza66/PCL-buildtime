using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.ViewModels.Pages;

using ResourceTypeKind = PCL.Avalonia.Services.Mods.ResourceType;

public sealed partial class ResourceDownloadPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IResourceSearchService _searchService;
    private readonly IResourceDownloadService _downloadService;
    private readonly IPlatformService _platform;
    private readonly IModrinthApi _modrinthApi;
    private readonly ICurseForgeApi _curseForgeApi;
    private CancellationTokenSource? _cancellationTokenSource;

    public ResourceDownloadPageViewModel(
        ISettingsService settingsService,
        IResourceSearchService searchService,
        IResourceDownloadService downloadService,
        IPlatformService platform,
        IModrinthApi modrinthApi,
        ICurseForgeApi curseForgeApi)
    {
        _settingsService = settingsService;
        _searchService = searchService;
        _downloadService = downloadService;
        _platform = platform;
        _modrinthApi = modrinthApi;
        _curseForgeApi = curseForgeApi;
    }

    public ObservableCollection<ResourceProjectItemViewModel> Projects { get; } = [];

    public IReadOnlyList<ResourceTypeOption> ResourceTypeOptions { get; } =
    [
        new(ResourceTypeKind.Mod, "Mod"),
        new(ResourceTypeKind.ResourcePack, "资源包"),
        new(ResourceTypeKind.Shader, "光影"),
        new(ResourceTypeKind.DataPack, "数据包"),
    ];

    public IReadOnlyList<ResourceSourceOption> SourceOptions { get; } =
    [
        new(ResourceSource.Modrinth, "Modrinth"),
        new(ResourceSource.CurseForge, "CurseForge"),
    ];

    public IReadOnlyList<string> GameVersions { get; } =
        ["", "1.21.4", "1.21", "1.20.1", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2"];

    public IReadOnlyList<string> Loaders { get; } =
        ["any", "fabric", "forge", "neoforge", "quilt"];

    [ObservableProperty]
    private ResourceTypeOption _resourceType = new(ResourceTypeKind.Mod, "Mod");

    [ObservableProperty]
    private ResourceSourceOption _source = new(ResourceSource.Modrinth, "Modrinth");

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _gameVersion = "1.20.1";

    [ObservableProperty]
    private string _loader = "fabric";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressPercent;

    public bool IsLoaderVisible => ResourceType.Value == ResourceTypeKind.Mod;

    partial void OnResourceTypeChanged(ResourceTypeOption value)
    {
        Projects.Clear();
        StatusMessage = "";
        OnPropertyChanged(nameof(IsLoaderVisible));
        if (value.Value != ResourceTypeKind.Mod)
        {
            Loader = "any";
        }
    }

    partial void OnSourceChanged(ResourceSourceOption value)
    {
        Projects.Clear();
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "";
        try
        {
            var results = await _searchService.SearchAsync(
                ResourceType.Value,
                SearchText,
                GameVersion,
                Loader,
                null,
                Source.Value);
            Projects.Clear();
            foreach (var project in results)
            {
                Projects.Add(new ResourceProjectItemViewModel(project, InstallAsync));
            }

            StatusMessage = $"找到 {Projects.Count} 个{ResourceType.Label}（{Source.Label} / {GameVersion}）";
        }
        catch (Exception ex)
        {
            StatusMessage = "搜索失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallAsync(ResourceProjectItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsProgressVisible = true;
        ProgressPercent = 0;
        ProgressText = "";
        _cancellationTokenSource = new CancellationTokenSource();
        try
        {
            var file = await ResolveFileAsync(item);
            var settings = _settingsService.Load();
            var folder = string.IsNullOrWhiteSpace(settings.MinecraftFolder)
                ? _platform.GetDefaultMinecraftFolder()
                : settings.MinecraftFolder;
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);
            await _downloadService.InstallAsync(
                item.Type,
                file,
                folder,
                progress,
                _cancellationTokenSource.Token);
            item.IsInstalled = true;
            StatusMessage = $"已安装 {item.Title}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "安装已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = "安装失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task<ResourceFileItem> ResolveFileAsync(ResourceProjectItemViewModel item)
    {
        var token = _cancellationTokenSource!.Token;
        if (item.Source == ResourceSource.CurseForge)
        {
            var files = await _curseForgeApi.GetFilesAsync(
                int.Parse(item.ProjectId),
                GameVersion,
                item.Type == ResourceTypeKind.Mod ? EffectiveLoader : "",
                token);
            var selected = files
                .OrderByDescending(file => file.FileDate ?? DateTimeOffset.MinValue)
                .FirstOrDefault();
            if (selected is null)
            {
                throw new InvalidOperationException("没有适配当前版本的下载");
            }

            return new ResourceFileItem
            {
                Source = ResourceSource.CurseForge,
                ProjectId = item.ProjectId,
                FileId = selected.Id.ToString(),
                DisplayName = selected.DisplayName,
                FileName = selected.FileName,
                Url = selected.DownloadUrl ?? "",
                Size = selected.FileLength,
                Sha1 = selected.Sha1,
            };
        }

        var versions = await _modrinthApi.GetVersionsAsync(
            item.ProjectId,
            GameVersion,
            item.Type == ResourceTypeKind.Mod ? EffectiveLoader : "",
            token);
        var version = versions
            .OrderByDescending(version => version.DatePublished ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        if (version is null)
        {
            throw new InvalidOperationException("没有适配当前版本的下载");
        }

        var modrinthFile = version.Files.FirstOrDefault(file => file.Primary) ?? version.Files.FirstOrDefault();
        if (modrinthFile is null)
        {
            throw new InvalidOperationException("资源版本没有可下载的文件");
        }

        return new ResourceFileItem
        {
            Source = ResourceSource.Modrinth,
            ProjectId = item.ProjectId,
            FileId = version.Id,
            DisplayName = version.Name,
            FileName = modrinthFile.Filename,
            Url = modrinthFile.Url,
            Size = modrinthFile.Size,
            Sha1 = modrinthFile.Sha1,
        };
    }

    private string EffectiveLoader => Loader is "any" or "" ? "" : Loader.Trim();

    private void OnDownloadProgress(DownloadProgress value)
    {
        if (value.TotalLength is > 0)
        {
            ProgressPercent = value.Fraction * 100;
            ProgressText = $"下载中 {value.Received / (1024.0 * 1024.0):F1} MB / {value.TotalLength.Value / (1024.0 * 1024.0):F1} MB";
        }
        else
        {
            ProgressText = $"下载中 {value.Received / (1024.0 * 1024.0):F1} MB";
        }
    }
}

public sealed record ResourceTypeOption(ResourceType Value, string Label);

public sealed record ResourceSourceOption(ResourceSource Value, string Label);

public sealed partial class ResourceProjectItemViewModel : ObservableObject
{
    private readonly Func<ResourceProjectItemViewModel, Task> _install;

    public ResourceProjectItemViewModel(
        ResourceProjectItem project,
        Func<ResourceProjectItemViewModel, Task> install)
    {
        ProjectId = project.ProjectId;
        Title = project.Title;
        Description = project.Description;
        AuthorText = project.AuthorText;
        DownloadsText = project.DownloadsText;
        CategoriesText = project.CategoriesText;
        Source = project.Source;
        Type = project.Type;
        _install = install;
    }

    public string ProjectId { get; }

    public string Title { get; }

    public string Description { get; }

    public string AuthorText { get; }

    public string DownloadsText { get; }

    public string CategoriesText { get; }

    public ResourceSource Source { get; }

    public ResourceType Type { get; }

    [ObservableProperty]
    private bool _isInstalled;

    [RelayCommand]
    private Task Install() => _install(this);
}
