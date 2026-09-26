using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PCL.Avalonia.ViewModels;
using PCL.Avalonia.Views;
using PCL.Avalonia.Views.Pages;

namespace PCL.Avalonia.Tests;

/// <summary>
/// Drives the real MainWindow with the real MainWindowViewModel so every nav page has to go
/// through the window's own DataTemplates, and asserts that materialising a page view never
/// logs a binding error (a broken view binding used to swallow the whole view and blank the page).
/// </summary>
public sealed class MainWindowPageRenderTests : IDisposable
{
    private readonly RecordingLogSink _sink = new();
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _viewModel;

    public MainWindowPageRenderTests()
    {
        Logger.Sink = _sink;
        _window = new MainWindow();
        _viewModel = MainWindowViewModelTests.CreateViewModel(useDarkTheme: true).ViewModel;
        _window.DataContext = _viewModel;
        _window.Show();
    }

    public void Dispose()
    {
        _window.DataContext = null;
        _window.Close();
        Logger.Sink = null;
    }

    [AvaloniaFact]
    public void EveryPage_MaterialisesItsView_WithoutBindingErrors()
    {
        foreach (var item in _viewModel.Items)
        {
            _viewModel.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();

            var view = FindPageView();
            Assert.True(
                view is not null,
                $"Page '{item.Title}' ({item.Page.GetType().Name}) materialised no view. Log: {_sink.Describe()}");
        }

        Assert.True(
            _sink.SevereEntries.Count == 0,
            "Binding errors while rendering pages: " + string.Join(" | ", _sink.SevereEntries));
    }

    [AvaloniaFact]
    public void ForgePage_MaterialisesForgelikeLoaderPageView()
    {
        Select("Forge");

        var view = _window.GetVisualDescendants().OfType<ForgelikeLoaderPageView>().FirstOrDefault();
        Assert.True(view is not null, $"Forge view missing. Log: {_sink.Describe()}");

        var forgeButton = view!.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Content as string == "Forge");
        Assert.True(forgeButton is not null, "Forge mode button missing.");
        Assert.True(
            forgeButton!.Classes.Contains("active"),
            "Forge mode button should start in the active class.");
    }

    [AvaloniaFact]
    public void ForgePage_ModeButtons_SwitchModes()
    {
        Select("Forge");

        var vm = (ViewModels.Pages.ForgelikeLoaderPageViewModel)_viewModel.Items.First(i => i.Title == "Forge").Page;
        Assert.False(vm.IsNeoForgeMode);

        var neoButton = FindModeButton("NeoForge");
        Assert.True(neoButton is not null, "NeoForge mode button missing.");

        Assert.True(neoButton!.Command is not null, "NeoForge button has no command.");
        Assert.True(neoButton.Command!.CanExecute(null), "NeoForge command cannot execute.");
        neoButton.Command.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsNeoForgeMode, "Clicking NeoForge did not switch the mode.");
        Assert.True(neoButton.Classes.Contains("active"), "NeoForge button is not highlighted after switching.");
        var forgeButton = FindModeButton("Forge");
        Assert.False(forgeButton!.Classes.Contains("active"), "Forge button stays highlighted after switching.");
    }

    private void Select(string title)
    {
        _viewModel.SelectedItem = _viewModel.Items.First(i => i.Title == title);
        Dispatcher.UIThread.RunJobs();
    }

    private Button? FindModeButton(string content)
    {
        return _window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Content as string == content);
    }

    private object? FindPageView()
    {
        return _window.GetVisualDescendants()
            .FirstOrDefault(v => v is UserControl);
    }

    private sealed class RecordingLogSink : ILogSink
    {
        public List<string> Entries { get; } = [];

        /// <summary>
        /// Entries that are not the harmless "binding path hit a null" kind, which are
        /// expected while a page sits on an empty selection or session.
        /// </summary>
        public List<string> SevereEntries
            => Entries.Where(e => !e.Contains("Value is null", StringComparison.Ordinal)).ToList();

        public bool IsEnabled(LogEventLevel level, string source)
            => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string source, object? sourceObject, string messageTemplate)
            => Entries.Add($"[{level} {source}] {messageTemplate}");

        public void Log(LogEventLevel level, string source, object? sourceObject, string messageTemplate, params object?[] propertyValues)
            => Entries.Add($"[{level} {source}] {messageTemplate} {string.Join(", ", propertyValues)}");

        public string Describe() => Entries.Count == 0 ? "<no warnings>" : string.Join(" | ", Entries);
    }
}
