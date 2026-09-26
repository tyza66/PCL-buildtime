using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PCL.Avalonia.Services;
using PCL.Avalonia.Services.Accounts;
using PCL.Avalonia.Services.Game;
using PCL.Avalonia.Services.Minecraft;
using PCL.Avalonia.Services.Mods;
using PCL.Avalonia.Services.Platform;
using PCL.Avalonia.ViewModels.Pages;
using PCL.Avalonia.Views.Pages;

namespace PCL.Avalonia.Tests;

/// <summary>
/// Layout checks for the page frames themselves: download filter clusters, the launch page's
/// left column and the version page's segmented detail tabs. Every assertion runs against the
/// real Avalonia layout engine with the real page views and view models, so an overlap shows up
/// here instead of on the user's screen.
/// </summary>
public sealed class PageClusterLayoutTests
{
    private const double FormControlHeight = 34;

    [AvaloniaFact]
    public void VersionDetailTabsSitAboveTheirContentWithoutOverlap()
    {
        var view = new VersionPageView { DataContext = CreateVersionPageViewModel() };
        var window = Show(view);

        var strip = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.Children.OfType<ToggleButton>().Count() == 4);
        var segments = strip.Children.OfType<ToggleButton>().ToList();
        var detail = (Panel)strip.GetVisualParent()!;
        var content = (Panel)detail.Children[^1];

        // One baseline for all four segments, one even gap between neighbours.
        Assert.Equal(4, segments.Count);
        Assert.Single(segments.Select(segment => segment.Bounds.Bottom).Distinct());
        for (var i = 1; i < segments.Count; i++)
        {
            Assert.Equal(4, segments[i].Bounds.Left - segments[i - 1].Bounds.Right, 2);
        }

        var viewModel = (VersionPageViewModel)view.DataContext!;
        foreach (var tab in new[] { 0, 1, 2, 3 })
        {
            // XAML hands CommandParameter over as text, so the real view model path is exercised.
            viewModel.SelectDetailTabCommand.Execute(tab.ToString());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var shown = content.Children.Where(child => child.IsVisible).ToList();
            Assert.Single(shown);
            Assert.True(shown[0].Bounds.Height > 0, $"tab {tab} has no measurable content");

            // The board sits inside the fill panel, so lift its origin into the dock panel's space,
            // the one the segment strip was measured in.
            var origin = shown[0].TranslatePoint(new Point(0, 0), detail);
            Assert.True(
                origin.HasValue && origin.Value.Y >= strip.Bounds.Bottom - 1,
                $"tab {tab} content overlaps the segment strip");

            Assert.True(segments[tab].IsChecked, $"segment {tab} is not highlighted");
            foreach (var other in segments.Where(other => !ReferenceEquals(other, segments[tab])))
            {
                Assert.False(other.IsChecked, "another segment stayed highlighted");
            }
        }
    }

    [AvaloniaFact]
    public void LaunchPageLeftColumnKeepsItsThreeRowsApart()
    {
        var window = Show(new LaunchPageView());

        var leftColumn = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.RowDefinitions.Count == 3);
        Assert.Equal(3, leftColumn.Children.Count);

        var header = leftColumn.Children[0];
        var list = leftColumn.Children[1];
        var footer = leftColumn.Children[2];

        // A Panel with no Dock used to fall back to LastChildFill, which let the version list
        // share the row with the folder info block.
        Assert.True(list.Bounds.Top >= header.Bounds.Bottom - 1, "version list overlaps its header");
        Assert.True(footer.Bounds.Top >= list.Bounds.Bottom - 1, "folder info overlaps the version list");
        Assert.Equal(leftColumn.Bounds.Bottom, footer.Bounds.Bottom, 1);
        Assert.True(list.Bounds.Height > header.Bounds.Height, "the list is not the row that stretches");
    }

    [AvaloniaFact]
    public void LaunchPageColumnsLineUpAcrossTheHeaderRow()
    {
        var window = Show(new LaunchPageView());

        var columns = window.GetVisualDescendants().OfType<Grid>()
            .First(grid => grid.ColumnDefinitions.Count == 2
                           && grid.Children.Count == 2
                           && grid.Children.All(child => child is Grid));
        var left = (Grid)columns.Children[0];
        var right = (Grid)columns.Children[1];

        var leftHeader = (Panel)left.Children[0];
        var rightHeader = (Panel)right.Children[0];
        var leftCard = ((Panel)left.Children[1]).Children.OfType<Border>().First();
        var rightCard = (Border)right.Children[1];

        // Both columns open with a header row of the same height, so the two cards below share one
        // top edge instead of starting at unrelated offsets.
        Assert.Equal(
            Top(leftHeader, columns) + leftHeader.Bounds.Height,
            Top(rightHeader, columns) + rightHeader.Bounds.Height, 1);
        Assert.Equal(Top(leftCard, columns), Top(rightCard, columns), 1);

        // The folder block is a single row: the path stays on one line beside its button rather
        // than wrapping into a tall card with a full-width action underneath.
        var folder = (Border)left.Children[2];
        var folderRow = (Grid)folder.Child!;
        var path = (TextBlock)folderRow.Children[0];
        var openFolder = (Button)folderRow.Children[1];
        Assert.Single(folderRow.Children.OfType<TextBlock>());
        Assert.True(path.Bounds.Height < FormControlHeight, "the folder path wrapped onto more lines");
        Assert.Equal(FormControlHeight, openFolder.Bounds.Height, 3);
        Assert.Equal(folderRow.Bounds.Height / 2, openFolder.Bounds.Center.Y, 1);
        Assert.Equal(folderRow.Bounds.Height / 2, path.Bounds.Center.Y, 1);
    }

    private static double Top(Visual visual, Visual reference)
        => visual.TranslatePoint(new Point(0, 0), reference)?.Y ?? double.NaN;

    [AvaloniaFact]
    public void DownloadFilterClustersKeepEvenGapsAndHugTheRightEdge()
    {
        AssertCluster(new ModsDownloadPageView());
        AssertCluster(new ResourceDownloadPageView());
        AssertCluster(new IntegrationPacksPageView());
    }

    private static void AssertCluster(UserControl page)
    {
        var window = Show(page);

        var cluster = window.GetVisualDescendants().OfType<StackPanel>()
            .First(panel => panel.Orientation == Orientation.Horizontal
                            && panel.Children.Any(child => child is ComboBox)
                            && panel.Children.Any(child => child is Button));
        var row = (Grid)cluster.GetVisualParent()!;
        var search = (TextBox)row.Children[0];

        // Same height for the input and every filter next to it.
        Assert.Equal(FormControlHeight, search.Bounds.Height, 3);
        foreach (var child in cluster.Children)
        {
            Assert.Equal(search.Bounds.Height, child.Bounds.Height, 3);
        }

        // One uniform gap between neighbours, and one between the search box and the cluster.
        for (var i = 1; i < cluster.Children.Count; i++)
        {
            Assert.Equal(8, cluster.Children[i].Bounds.Left - cluster.Children[i - 1].Bounds.Right, 2);
        }

        Assert.Equal(8, cluster.Bounds.Left - search.Bounds.Right, 2);
        // Bounds are parent-relative: the cluster's own box is in the row's space, so its width is
        // what the children's edges are measured against, and its right edge is in the row's space.
        Assert.Equal(cluster.Bounds.Width, cluster.Children[^1].Bounds.Right, 2);
        Assert.Equal(0, cluster.Children[0].Bounds.Left, 2);
        Assert.Equal(row.Bounds.Right, cluster.Bounds.Right, 2);
        Assert.True(cluster.Children.Count >= 2, "cluster needs a filter and an action button");
        window.Close();
    }

    [AvaloniaFact]
    public void DangerBrushResolvesInBothThemes()
    {
        // The delete buttons pointed at a key that was never declared, so they rendered grey.
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.True(
                Application.Current!.TryFindResource("DangerBrush", theme, out var brush),
                $"DangerBrush is missing for the {theme} theme");
            Assert.IsType<SolidColorBrush>(brush);
        }
    }

    private static Window Show(object content)
    {
        var window = new Window { Width = 1200, Height = 820, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static VersionPageViewModel CreateVersionPageViewModel() => new(
        new StubSettingsService(),
        new StubVersionCatalogService(),
        new InstanceClassifier(),
        new SessionState(),
        new StubPlatformService(),
        new StubVersionManagerService(),
        new StubFolderOpener(),
        new StubJavaService(),
        new StubGameLauncher(),
        new StubScriptExporter(),
        new InstancePackExporter(),
        new StubModsService());

    private sealed class StubSettingsService : ISettingsService
    {
        public AppSettings Load() => new() { MinecraftFolder = "/games/mc", JavaPath = "/usr/bin/java" };

        public void Save(AppSettings settings)
        {
        }
    }

    private sealed class StubVersionCatalogService : IVersionCatalogService
    {
        public IReadOnlyList<MinecraftVersion> Scan(string minecraftFolder) => [];

        public MinecraftVersionJson? LoadJson(string minecraftFolder, string id) => null;
    }

    private sealed class StubPlatformService : IPlatformService
    {
        public string GetConfigDirectory() => Path.GetTempPath();

        public string GetDefaultMinecraftFolder() => "/default/.minecraft";
    }

    private sealed class StubVersionManagerService : IVersionManagerService
    {
        public VersionSettings LoadSettings(string minecraftFolder, string versionId) => new();

        public void SetFavorite(string minecraftFolder, string versionId, bool isFavorite)
        {
        }

        public void SetHidden(string minecraftFolder, string versionId, bool isHidden)
        {
        }

        public void SetDisplayType(string minecraftFolder, string versionId, InstanceDisplayType displayType)
        {
        }

        public void SetDescription(string minecraftFolder, string versionId, string description)
        {
        }

        public void SetInstanceLaunchSettings(
            string minecraftFolder,
            string versionId,
            int? maxMemoryMb,
            string? javaPath,
            string? jvmArguments,
            string? gameArguments)
        {
        }

        public string Rename(string minecraftFolder, string versionId, string newName) => newName;

        public void Delete(string minecraftFolder, string versionId)
        {
        }
    }

    private sealed class StubFolderOpener : IFolderOpener
    {
        public void Open(string path)
        {
        }
    }

    private sealed class StubJavaService : IJavaService
    {
        public string? ResolveJavaExecutable(AppSettings settings) => settings.JavaPath;

        public string? ResolveJavaExecutable(AppSettings settings, int? requiredMajorVersion)
            => settings.JavaPath;
    }

    private sealed class StubGameLauncher : IGameLauncher
    {
        public LaunchPlan BuildLaunchPlan(
            MinecraftVersion version,
            AppSettings settings,
            string javaExecutable,
            Account? account = null,
            VersionSettings? versionSettings = null)
            => throw new NotSupportedException();

        public IGameLaunch Launch(LaunchPlan plan, IProgress<string>? output = null)
            => throw new NotSupportedException();
    }

    private sealed class StubScriptExporter : ILaunchScriptExporter
    {
        public string Export(LaunchPlan plan, string filePath) => filePath;
    }

    private sealed class StubModsService : IModsService
    {
        public IReadOnlyList<ModInfo> Scan(string minecraftFolder) => [];

        public ModInfo SetEnabled(ModInfo mod, bool enabled) => mod;

        public void Delete(ModInfo mod)
        {
        }
    }
}
