using Avalonia.Controls;
using HelL.Desktop.Services;
using Avalonia.VisualTree;
using Avalonia.Interactivity;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private string currentPage = "launch";
    private bool readableText;
    private sealed class TextSize(double value) { public double Value { get; } = value; }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, TextSize> textSizes = new();
    private void ToggleReadableText(object? sender, RoutedEventArgs e) { readableText = !readableText; ApplyFrontendLayout(); }
    private void ApplyFrontendLayout()
    {
        if (ShellColumns is null) return;
        ShellColumns.ColumnDefinitions[0].Width = new GridLength(Bounds.Width < 1000 ? 164 : 208);
        foreach (TextBlock text in this.GetVisualDescendants().OfType<TextBlock>())
        {
            double size = textSizes.GetValue(text, control => new TextSize(control.FontSize)).Value;
            text.FontSize = readableText ? Math.Max(13, size * 1.15) : size;
        }
    }
    internal bool IsWorking => task is not null;
    internal Task<bool> ConfirmAction(string title, string body, string accept = "确认") => DesktopDialogs.Confirm(this, title, body, accept);
    internal string DataRoot => DataDirectory.Text ?? "";
}
