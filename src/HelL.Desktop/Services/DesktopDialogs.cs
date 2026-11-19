using Avalonia.Controls;
using Avalonia.Layout;

namespace HelL.Desktop.Services;

internal static class DesktopDialogs
{
    internal static Func<string, string, Task<bool>>? Confirmation { get; set; }

    internal static async Task<bool> Confirm(Window owner, string title, string body, string accept = "确认")
    {
        if (Confirmation is { } fixture) return await fixture(title, body);
        var dialog = new Window { Title = title, Width = 560, MaxHeight = 640, MinWidth = 340,
            SizeToContent = SizeToContent.Height, CanResize = true, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new Button { Content = "取消", MinWidth = 84 };
        var submit = new Button { Content = accept, MinWidth = 84, Classes = { "accent" } };
        cancel.Click += (_, _) => dialog.Close(false);
        submit.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 16, Children =
        {
            new TextBlock { Text = title, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new ScrollViewer { MaxHeight = 400, Content = new SelectableTextBlock { Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, submit } }
        } };
        dialog.Opened += (_, _) => cancel.Focus();
        dialog.KeyDown += (_, key) => { if (key.Key == Avalonia.Input.Key.Escape) { key.Handled = true; dialog.Close(false); } };
        return await dialog.ShowDialog<bool>(owner);
    }
}
