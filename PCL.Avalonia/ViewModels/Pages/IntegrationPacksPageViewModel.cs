using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Downloads;
using PCL.Avalonia.Services.Mods;

namespace PCL.Avalonia.ViewModels.Pages;

public sealed partial class IntegrationPacksPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ICurseForgeModpackService _modpackService;
    private readonly IPlatformService _platform;
    private CancellationTokenSource? _cancellationTokenSource;

    public IntegrationPacksPageViewModel(
        ISettingsService settingsService,
        ICurseForgeModpackService modpackService,
        IPlatformService platform)
    {
        _settingsService = settingsService;
        _modpackService = modpackService;
        _platform = platform;
    }

    public ObservableCollection<IntegrationPackItemViewModel> Projects { get; } = [];

    public IReadOnlyList<string> GameVersions { get; } =
        ["1.21.4", "1.21", "1.20.1", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2"];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _gameVersion = "1.20.1";

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
            var results = await _modpackService.SearchAsync(SearchText);
            Projects.Clear();
            foreach (var project in results)
            {
                Projects.Add(new IntegrationPackItemViewModel(project, InstallAsync));
            }

            StatusMessage = $"找到 {Projects.Count} 个整合包（{GameVersion}）";
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
    private async Task InstallAsync(IntegrationPackItemViewModel item)
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
            var settings = _settingsService.Load();
            var folder = GetMinecraftFolder(settings);
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);
            await _modpackService.InstallAsync(
                item.Project,
                GameVersion,
                folder,
                progress,
                _cancellationTokenSource.Token);
            item.IsInstalled = true;
            StatusMessage = $"已下载 {item.Title}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "下载已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = "下载失败：" + ex.Message;
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

public sealed partial class IntegrationPackItemViewModel : ObservableObject
{
    private readonly Func<IntegrationPackItemViewModel, Task> _install;

    public IntegrationPackItemViewModel(
        CurseForgeProject project,
        Func<IntegrationPackItemViewModel, Task> install)
    {
        Project = project;
        Title = project.Name;
        Description = project.Summary;
        AuthorText = project.Authors.Count > 0
            ? project.Authors[0].Name
            : (string.IsNullOrWhiteSpace(project.Slug) ? project.Id.ToString() : project.Slug);
        DownloadsText = FormatDownloads(project.DownloadCount);
        CategoriesText = string.Join(" · ", project.Categories);
        _install = install;
    }

    public CurseForgeProject Project { get; }

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
