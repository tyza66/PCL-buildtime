using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.ViewModels.Pages;

public enum ModDownloadSource
{
    Modrinth,
    CurseForge,
}

public sealed partial class ModsDownloadPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModrinthApi _modrinthApi;
    private readonly ICurseForgeApi _curseForgeApi;
    private readonly IModsDownloadService _modrinthInstaller;
    private readonly ICurseForgeDownloadService _curseForgeInstaller;
    private readonly IPlatformService _platform;
    private CancellationTokenSource? _cancellationTokenSource;

    public ModsDownloadPageViewModel(
        ISettingsService settingsService,
        IModrinthApi modrinthApi,
        IModsDownloadService modrinthInstaller,
        IPlatformService platform,
        ICurseForgeApi? curseForgeApi = null,
        ICurseForgeDownloadService? curseForgeInstaller = null)
    {
        _settingsService = settingsService;
        _modrinthApi = modrinthApi;
        _curseForgeApi = curseForgeApi ?? new UnavailableCurseForgeApi();
        _curseForgeInstaller = curseForgeInstaller ?? new UnavailableCurseForgeInstaller();
        _modrinthInstaller = modrinthInstaller;
        _platform = platform;
    }

    public ObservableCollection<DownloadProjectItemViewModel> Projects { get; } = [];

    public IReadOnlyList<ModDownloadSource> Sources { get; } = [ModDownloadSource.Modrinth, ModDownloadSource.CurseForge];

    public IReadOnlyList<string> GameVersions { get; } =
        ["1.21.4", "1.21", "1.20.1", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2"];

    public IReadOnlyList<string> Loaders { get; } =
        ["fabric", "forge", "neoforge", "quilt", "paper", "spigot", "any"];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _gameVersion = "1.20.1";

    [ObservableProperty]
    private string _loader = "fabric";

    [ObservableProperty]
    private ModDownloadSource _source = ModDownloadSource.Modrinth;

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

    partial void OnSourceChanged(ModDownloadSource value)
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
            if (Source == ModDownloadSource.CurseForge)
            {
                await SearchCurseForgeAsync();
            }
            else
            {
                await SearchModrinthAsync();
            }
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
    private async Task InstallAsync(DownloadProjectItemViewModel item)
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
            if (item.Source == ModDownloadSource.CurseForge)
            {
                await InstallCurseForgeAsync(item);
            }
            else
            {
                await InstallModrinthAsync(item);
            }

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

    private async Task SearchModrinthAsync()
    {
        var results = await _modrinthApi.SearchProjectsAsync(
            SearchText,
            GameVersion,
            EffectiveLoader);
        Projects.Clear();
        foreach (var project in results)
        {
            Projects.Add(DownloadProjectItemViewModel.FromModrinth(project, InstallAsync));
        }

        StatusMessage = $"找到 {Projects.Count} 个 Mod（Modrinth / {GameVersion} / {Loader}）";
    }

    private async Task SearchCurseForgeAsync()
    {
        var results = await _curseForgeApi.SearchProjectsAsync(SearchText);
        Projects.Clear();
        foreach (var project in results)
        {
            Projects.Add(DownloadProjectItemViewModel.FromCurseForge(project, InstallAsync));
        }

        StatusMessage = $"找到 {Projects.Count} 个 Mod（CurseForge / {GameVersion} / {Loader}）";
    }

    private async Task InstallModrinthAsync(DownloadProjectItemViewModel item)
    {
        var versions = await _modrinthApi.GetVersionsAsync(
            item.ProjectId,
            GameVersion,
            EffectiveLoader,
            _cancellationTokenSource!.Token);
        var selected = versions
            .OrderByDescending(version => version.DatePublished ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        if (selected is null)
        {
            throw new InvalidOperationException("没有适配当前版本的下载");
        }

        var settings = _settingsService.Load();
        var folder = Path.Combine(GetMinecraftFolder(settings), "mods");
        var progress = new Progress<DownloadProgress>(OnDownloadProgress);
        await _modrinthInstaller.InstallAsync(selected, folder, progress, _cancellationTokenSource.Token);
    }

    private async Task InstallCurseForgeAsync(DownloadProjectItemViewModel item)
    {
        var files = await _curseForgeApi.GetFilesAsync(
            int.Parse(item.ProjectId),
            GameVersion,
            EffectiveLoader,
            _cancellationTokenSource!.Token);
        var selected = files
            .OrderByDescending(file => file.FileDate ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        if (selected is null)
        {
            throw new InvalidOperationException("没有适配当前版本的下载");
        }

        var settings = _settingsService.Load();
        var folder = Path.Combine(GetMinecraftFolder(settings), "mods");
        var progress = new Progress<DownloadProgress>(OnDownloadProgress);
        await _curseForgeInstaller.InstallAsync(selected, folder, progress, _cancellationTokenSource.Token);
    }

    private string EffectiveLoader => Loader == "any" ? "" : Loader.Trim();

    private string GetMinecraftFolder(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.MinecraftFolder)
            ? _platform.GetDefaultMinecraftFolder()
            : settings.MinecraftFolder;
    }

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

    private sealed class UnavailableCurseForgeApi : ICurseForgeApi
    {
        public Task<IReadOnlyList<CurseForgeProject>> SearchProjectsAsync(
            string query,
            int classId = 6,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("CurseForge 服务不可用");

        public Task<IReadOnlyList<CurseForgeModFile>> GetFilesAsync(
            int projectId,
            string gameVersion,
            string loader,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("CurseForge 服务不可用");

        public Task<IReadOnlyList<CurseForgeModFile>> GetModpackFilesAsync(
            int projectId,
            string gameVersion,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("CurseForge 服务不可用");

        public Task<CurseForgeModFile?> GetFileAsync(
            int projectId,
            int fileId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("CurseForge 服务不可用");
    }

    private sealed class UnavailableCurseForgeInstaller : ICurseForgeDownloadService
    {
        public Task<string> InstallAsync(
            CurseForgeModFile file,
            string modsFolder,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("CurseForge 服务不可用");
    }
}

public sealed partial class DownloadProjectItemViewModel : ObservableObject
{
    private readonly Func<DownloadProjectItemViewModel, Task> _install;

    private DownloadProjectItemViewModel(
        ModDownloadSource source,
        string projectId,
        string title,
        string description,
        string authorText,
        string downloadsText,
        string categoriesText,
        Func<DownloadProjectItemViewModel, Task> install)
    {
        Source = source;
        ProjectId = projectId;
        Title = title;
        Description = description;
        AuthorText = authorText;
        DownloadsText = downloadsText;
        CategoriesText = categoriesText;
        _install = install;
    }

    public static DownloadProjectItemViewModel FromModrinth(
        ModrinthProject project,
        Func<DownloadProjectItemViewModel, Task> install)
    {
        var author = string.IsNullOrWhiteSpace(project.Author)
            ? (string.IsNullOrWhiteSpace(project.Slug) ? project.ProjectId : project.Slug)
            : project.Author;
        return new DownloadProjectItemViewModel(
            ModDownloadSource.Modrinth,
            project.ProjectId,
            project.Title,
            project.Description,
            author,
            FormatDownloads(project.Downloads),
            string.Join(" · ", project.Categories),
            install);
    }

    public static DownloadProjectItemViewModel FromCurseForge(
        CurseForgeProject project,
        Func<DownloadProjectItemViewModel, Task> install)
    {
        var author = project.Authors.Count > 0
            ? project.Authors[0].Name
            : (string.IsNullOrWhiteSpace(project.Slug) ? project.Id.ToString() : project.Slug);
        return new DownloadProjectItemViewModel(
            ModDownloadSource.CurseForge,
            project.Id.ToString(),
            project.Name,
            project.Summary,
            author,
            FormatDownloads(project.DownloadCount),
            string.Join(" · ", project.Categories),
            install);
    }

    public ModDownloadSource Source { get; }

    public string ProjectId { get; }

    public string Title { get; }

    public string Description { get; }

    public string AuthorText { get; }

    public string DownloadsText { get; }

    public string CategoriesText { get; }

    [ObservableProperty]
    private bool _isInstalled;

    [RelayCommand]
    private Task Install() => _install(this);

    private static string FormatDownloads(int downloads)
    {
        if (downloads >= 1_000_000)
        {
            return $"{downloads / 1_000_000.0:F1}M 下载";
        }

        if (downloads >= 1_000)
        {
            return $"{downloads / 1_000.0:F1}K 下载";
        }

        return $"{downloads} 下载";
    }
}
