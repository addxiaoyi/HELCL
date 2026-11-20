using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelL.Portable;
using SkiaSharp;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckDesktopRefinementAsync()
    {
        string folder = Path.Combine(Program.DataRoot, "refine");
        Directory.CreateDirectory(folder);
        string theme = CurrentTheme, glass = CurrentGlass, page = currentPage;
        double width = Width, height = Height;
        var hints = TransparencyLevelHint;
        var requestedTheme = Application.Current!.RequestedThemeVariant;
        var failures = new List<string>();
        var samples = new List<object>();
        void Check(bool passed, string label) { if (!passed) failures.Add(label); }
        try
        {
            // This comparison exercises the real no-compositor path on every host.
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            var transparencyWait = System.Diagnostics.Stopwatch.StartNew();
            while (ActualTransparencyLevel != WindowTransparencyLevel.None && transparencyWait.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(25);
            Check(ActualTransparencyLevel == WindowTransparencyLevel.None, "the compositor did not enter the requested opaque comparison mode");
            SelectGlassOpacity("standard", persist: false);
            foreach (string mode in new[] { "light", "dark" })
            {
                SelectTheme(mode, persist: false);
                foreach (int viewport in new[] { 800, 1180 })
                {
                    Width = viewport; Height = 760;
                    ShowPage("library");
                    var list = LibraryView.FindControl<ListBox>("LibraryList")!;
                    var selectedEntry = list.SelectedItem as LibraryInstance;
                    var ordered = list.Items.OfType<LibraryInstance>().OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
                    list.ItemsSource = ordered;
                    list.SelectedItem = ordered.FirstOrDefault(entry => entry.Name == selectedEntry?.Name) ?? ordered.FirstOrDefault();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    await Task.Delay(100);
                    string image = Path.Combine(folder, $"desktop-{mode}-{viewport}.png");
                    SaveIconWindow(image);
                    var inspector = LibraryView.FindControl<Border>("InspectorCard")!;
                    var split = LibraryView.FindControl<Grid>("WorkspaceSplit")!;
                    var label = LibraryView.FindControl<TextBlock>("SelectionMeta")!;
                    var selected = Navigation.Children.OfType<Button>().Single(button => (string?)button.Tag == "library");
                    var title = selected.GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Text == "我的实例");
                    Point countAt = InstanceCount.TranslatePoint(default, selected) ?? default;
                    Point titleAt = title.TranslatePoint(default, selected) ?? default;
                    double badgeGap = countAt.X - titleAt.X - title.Bounds.Width;
                    Point surfaceAt = inspector.TranslatePoint(new Point(8, 8), this) ?? default;
                    using var pixels = SKBitmap.Decode(image) ?? throw new IOException("The refinement screenshot could not be decoded.");
                    SKColor surface = pixels.GetPixel((int)surfaceAt.X, (int)surfaceAt.Y);
                    Color text = (label.Foreground as ISolidColorBrush)?.Color ?? Colors.Transparent;
                    double contrast = ReadabilityRatio(text, surface);
                    byte backdropAlpha = (WindowShell.Background as ISolidColorBrush)?.Color.A ?? 0;
                    Check(backdropAlpha == byte.MaxValue, "the no-blur backdrop remains translucent in " + mode);
                    Check(badgeGap >= 8, "the instance count touches its navigation label at " + viewport);
                    Check(contrast >= 4.5, "small inspector text lacks readable contrast in " + mode);
                    if (viewport == 1180)
                        Check(inspector.Bounds.Height < split.Bounds.Height - 20, "the sparse inspector stretches across unused vertical space");
                    Check(inspector.GetLogicalDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "打开启动页").IsEffectivelyVisible,
                        "the primary inspector action is hidden");
                    samples.Add(new { mode, viewport, backdropAlpha, actualTransparency = ActualTransparencyLevel.ToString(), badgeGap, contrast,
                        inspectorHeight = inspector.Bounds.Height, workspaceHeight = split.Bounds.Height });
                }
            }
            var library = LibraryView.FindControl<ListBox>("LibraryList")!;
            var original = library.Items.OfType<LibraryInstance>().First();
            const string longName = "中文生存世界 · 与好友一起探索的光影建筑和机械动力长期整合包";
            LibraryInstance longEntry = original with { Name = longName, Pinned = true };
            foreach (int viewport in new[] { 800, 1180 })
            {
                Width = viewport;
                library.ItemsSource = new[] { longEntry };
                library.SelectedItem = longEntry;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(80);
                TextBlock name = library.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == longName);
                Button[] actions = library.GetVisualDescendants().OfType<Button>().Where(button => button.Tag?.ToString() == longName).ToArray();
                double nameRight = (name.TranslatePoint(new Point(name.Bounds.Width, 0), library) ?? default).X;
                double actionsLeft = actions.Min(button => (button.TranslatePoint(default, library) ?? default).X);
                Check(nameRight <= actionsLeft - 4, "a long pinned name overlaps its row actions at " + viewport);
                Check(actions.Length == 3 && actions.All(button => button.IsEffectivelyVisible
                    && (button.TranslatePoint(new Point(button.Bounds.Width, 0), library) ?? default).X <= library.Bounds.Width),
                    "a long pinned name pushes row actions beyond the viewport at " + viewport);
                SaveIconWindow(Path.Combine(folder, $"desktop-long-name-{viewport}.png"));
            }
            SelectGlassOpacity("ultra", persist: false);
            Check(CurrentGlass == "ultra", "the no-blur fallback overwrote the user's glass preference");
            SelectTheme("system", persist: false);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Color followedLight = ((ISolidColorBrush)WindowShell.Background!).Color;
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Color followedDark = ((ISolidColorBrush)WindowShell.Background!).Color;
            Check(CurrentTheme == "system" && followedLight.R > 200 && followedDark.R < 40,
                "the window tint does not follow a propagated actual-theme change");
            Check(followedLight.A == byte.MaxValue && followedDark.A == byte.MaxValue,
                "theme propagation restores translucency without compositor blur");
        }
        finally
        {
            TransparencyLevelHint = hints;
            SelectGlassOpacity(glass, persist: false);
            SelectTheme(theme, persist: false);
            Application.Current!.RequestedThemeVariant = requestedTheme;
            Width = width; Height = height;
            LibraryView.Reload(SelectedName);
            ShowPage(page);
        }
        File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-refine.json"), System.Text.Json.JsonSerializer.Serialize(new
        { ok = failures.Count == 0, failures, samples, comparison = "opaque fallback without compositor blur", nativeScreenReaderVerified = false }));
        if (failures.Count > 0) throw new IOException("Desktop refinement check: " + string.Join("; ", failures));
    }

    private static double ReadabilityRatio(Color foreground, SKColor background)
    {
        static double Channel(byte value)
        {
            double channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        double ink = 0.2126 * Channel(foreground.R) + 0.7152 * Channel(foreground.G) + 0.0722 * Channel(foreground.B);
        double paper = 0.2126 * Channel(background.Red) + 0.7152 * Channel(background.Green) + 0.0722 * Channel(background.Blue);
        return (Math.Max(ink, paper) + 0.05) / (Math.Min(ink, paper) + 0.05);
    }
}
