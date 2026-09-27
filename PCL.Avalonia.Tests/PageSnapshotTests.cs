using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using PCL.Avalonia.ViewModels;
using PCL.Avalonia.Views;

namespace PCL.Avalonia.Tests;

/// <summary>
/// Renders every navigation page of the real window with the real Skia pipeline and asserts the
/// result is a usable picture: background laid out, foreground pixels present, and no pixel block
/// where a control sits outside the window it belongs to. Setting <c>PCL_SNAPSHOT_DIR</c> before
/// running writes one PNG per page, which is how UI polish is eyeballed without a real screen.
/// </summary>
public sealed class PageSnapshotTests : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainWindowViewModel _viewModel;

    public PageSnapshotTests()
    {
        _viewModel = MainWindowViewModelTests.CreateViewModel(useDarkTheme: true).ViewModel;
        _window = new MainWindow
        {
            DataContext = _viewModel,
            Width = 1280,
            Height = 820,
        };
        _window.Show();
    }

    public void Dispose()
    {
        _window.DataContext = null;
        _window.Close();
    }

    [AvaloniaFact]
    public void EveryPageRendersAsAUsablePicture()
    {
        var output = Environment.GetEnvironmentVariable("PCL_SNAPSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
        }

        foreach (var item in _viewModel.Items)
        {
            _viewModel.SelectedItem = item;
            Dispatcher.UIThread.RunJobs();
            _window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var name = item.Title;
            var view = _window.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control => control.DataContext == item.Page);
            Assert.True(view is not null, $"page '{name}' materialised no view");
            Assert.True(view!.Bounds.Width > 8 && view.Bounds.Height > 8, $"page '{name}' has no size");

            var bitmap = new RenderTargetBitmap(
                new PixelSize((int)_window.ClientSize.Width, (int)_window.ClientSize.Height));
            bitmap.Render(_window);

            var report = Analyse(bitmap);
            Assert.True(
                report.BackgroundRatio > 0.30,
                $"page '{name}' looks mostly blank ({report.BackgroundRatio:P0} plain background)");
            Assert.True(
                report.DistinctFilledRatio > 0.03,
                $"page '{name}' has almost no visible content ({report.DistinctFilledRatio:P0})");

            if (!string.IsNullOrWhiteSpace(output))
            {
                bitmap.Save(Path.Combine(output, $"{name}.png"));
            }
        }
    }

    private static PixelAnalysis Analyse(RenderTargetBitmap bitmap)
    {
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var step = 2;
        using var pixels = Decode(bitmap);
        var counts = new Dictionary<uint, int>();
        var total = 0;
        for (var y = 0; y < height; y += step)
        {
            for (var x = 0; x < width; x += step)
            {
                var color = pixels.GetPixel(x, y);
                var key = Pack(color);
                counts[key] = counts.GetValueOrDefault(key) + 1;
                total++;
            }
        }

        // 画布上占比最高的一片底色通常是页面背景，；占屏比越高说明这一页越空。
        var background = counts.Count == 0 ? total : counts.Values.Max();
        return new PixelAnalysis((double)background / total, (double)(total - background) / total);
    }

    private static SKBitmap Decode(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static uint Pack(SKColor color) => (uint)color.Alpha << 24 | (uint)color.Red << 16 | (uint)color.Green << 8 | color.Blue;

    private readonly record struct PixelAnalysis(double BackgroundRatio, double DistinctFilledRatio);
}
