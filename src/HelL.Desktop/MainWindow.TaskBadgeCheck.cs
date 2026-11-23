using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using HelL.Desktop.Models;
using SkiaSharp;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckTaskBadgeContrastAsync()
    {
        string folder = Path.Combine(Program.DataRoot, "task-badges");
        Directory.CreateDirectory(folder);
        var list = TasksView.FindControl<ListBox>("TaskList")!;
        var previousTasks = list.ItemsSource;
        var previousSelection = list.SelectedItem;
        string theme = CurrentTheme, page = currentPage;
        var hints = TransparencyLevelHint;
        var failures = new List<string>();
        var samples = new List<object>();
        try
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (ActualTransparencyLevel != WindowTransparencyLevel.None && DateTime.UtcNow < deadline) await Task.Delay(20);
            if (ActualTransparencyLevel != WindowTransparencyLevel.None) throw new IOException("Task contrast needs the opaque reference backdrop.");
            var states = new[] { DesktopTaskState.Running, DesktopTaskState.Cancelling, DesktopTaskState.Completed, DesktopTaskState.Failed, DesktopTaskState.Cancelled };
            foreach (DesktopTaskState state in states)
            {
                var item = new DesktopTask("任务状态对比度验收");
                list.ItemsSource = new[] { item };
                list.SelectedItem = item;
                ShowPage("tasks");
                await SettleReading();
                if (state == DesktopTaskState.Cancelling) item.RequestCancellation();
                else if (state != DesktopTaskState.Running) item.Finish(state, "受控任务状态");
                foreach (string mode in new[] { "dark", "light" })
                {
                    SelectTheme(mode, persist: false);
                    ShowPage("tasks");
                    await SettleReading();
                    string image = Path.Combine(folder, $"{state}-{mode}.png");
                    SaveIconWindow(image);
                    var label = list.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == item.StateLabel && text.Parent is Border);
                    var badge = (Border)label.Parent!;
                    Point at = badge.TranslatePoint(new Point(3, badge.Bounds.Height / 2), this) ?? default;
                    using var pixels = SKBitmap.Decode(image) ?? throw new IOException("Task badge screenshot could not be decoded.");
                    SKColor paper = pixels.GetPixel((int)at.X, (int)at.Y);
                    Color ink = ((ISolidColorBrush)label.Foreground!).Color;
                    double ratio = ReadabilityRatio(ink, paper);
                    samples.Add(new { mode, state = state.ToString(), ratio, ink = ink.ToString(), paper = paper.ToString() });
                    if (ratio < 4.5) failures.Add($"{mode} {state} badge contrast is {ratio:F2}:1.");
                }
            }
            File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-task-badge-contrast.json"), JsonSerializer.Serialize(new
            { ok = failures.Count == 0, failures, samples, reference = "opaque selected row; same task switches theme" }));
            if (failures.Count > 0) throw new IOException(string.Join("; ", failures));
        }
        finally
        {
            list.ItemsSource = previousTasks;
            list.SelectedItem = previousSelection;
            TransparencyLevelHint = hints;
            SelectTheme(theme, persist: false);
            ShowPage(page);
        }
    }
}
