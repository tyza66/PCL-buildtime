using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using PCL.Avalonia.Views;

namespace PCL.Avalonia.Services;

/// <summary>
/// 模态确认框实现：挂在主窗口上，Esc 取消、Enter 确认。
/// 拿不到宿主窗口时一律视为取消——宁可误取消，也不能误删玩家的版本。
/// </summary>
public sealed class AvaloniaConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = GetMainWindow();
        if (owner is null)
        {
            return Task.FromResult(false);
        }

        return new ConfirmWindow(title, message, "确定删除").ShowDialog<bool>(owner);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.Windows.OfType<MainWindow>().FirstOrDefault();
    }
}
