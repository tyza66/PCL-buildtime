using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PCL.Avalonia.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public ConfirmWindow(string title, string message, string confirmText = "确定")
        : this()
    {
        Title = title;
        titleText.Text = title;
        messageText.Text = message;
        confirmButton.Content = confirmText;
        KeyDown += OnWindowKeyDown;
    }

    // Esc 取消、Enter 确认：删版本这种操作要么明确回车，要么明确 Esc，别让误触过去。
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close(false);
                e.Handled = true;
                break;
            case Key.Enter:
                Close(true);
                e.Handled = true;
                break;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
