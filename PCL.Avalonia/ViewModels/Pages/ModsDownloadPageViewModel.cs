using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class ModsDownloadPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IModrinthApi _api;
    private readonly IModsDownloadService _installer;
    private readonly IPlatformService _platform;
    private CancellationTokenSource? _cancellationTokenSource;

    public ModsDownloadPageViewModel(
        ISettingsService settingsService,
        IModrinthApi api,
        IModsDownloadService installer,
        IPlatformService platform)
    {
        _settingsService = settingsService;
        _api = api;
        _installer = installer;
        _platform = platform;
    }

    public ObservableCollection<ModProjectItemViewModel> Projects { get; } = [];

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
    private bool _isBusy;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private double _progressPercent;

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
            var results = await _api.SearchProjectsAsync(SearchText, GameVersion, EffectiveLoader);
            Projects.Clear();
            foreach (var project in results)
            {
                Projects.Add(new ModProjectItemViewModel(project, InstallAsync));
            }

            StatusMessage = $"找到 {Projects.Count} 个 Mod（{GameVersion} / {Loader}）";
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
    private async Task InstallAsync(ModProjectItemViewModel item)
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
            var versions = await _api.GetVersionsAsync(
                item.Project.ProjectId,
                GameVersion,
                EffectiveLoader,
                _cancellationTokenSource.Token);
            var selected = versions
                .OrderByDescending(version => version.DatePublished ?? DateTimeOffset.MinValue)
                .FirstOrDefault();
            if (selected is null)
            {
                StatusMessage = "没有适配当前版本的下载";
                return;
            }

            var settings = _settingsService.Load();
            var folder = Path.Combine(GetMinecraftFolder(settings), "mods");
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);
            await _installer.InstallAsync(selected, folder, progress, _cancellationTokenSource.Token);
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
}

public sealed partial class ModProjectItemViewModel : ObservableObject
{
    private readonly Func<ModProjectItemViewModel, Task> _install;

    public ModProjectItemViewModel(ModrinthProject project, Func<ModProjectItemViewModel, Task> install)
    {
        Project = project;
        Title = project.Title;
        Description = project.Description;
        AuthorText = string.IsNullOrWhiteSpace(project.Author)
            ? (string.IsNullOrWhiteSpace(project.Slug) ? project.ProjectId : project.Slug)
            : project.Author;
        DownloadsText = FormatDownloads(project.Downloads);
        CategoriesText = string.Join(" · ", project.Categories);
        _install = install;
    }

    public ModrinthProject Project { get; }

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
