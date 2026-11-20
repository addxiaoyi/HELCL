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
        if (LaunchWorkspaceColumns is not null && LaunchQuickPanel is not null)
        {
            bool stackLaunchColumns = Bounds.Width <= 900;
            LaunchWorkspaceColumns.ColumnDefinitions[0].Width = stackLaunchColumns
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(1.22, GridUnitType.Star);
            LaunchWorkspaceColumns.ColumnDefinitions[1].Width = stackLaunchColumns
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
            Grid.SetRow(LaunchQuickPanel, stackLaunchColumns ? 1 : 0);
            Grid.SetColumn(LaunchQuickPanel, stackLaunchColumns ? 0 : 1);
        }
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
