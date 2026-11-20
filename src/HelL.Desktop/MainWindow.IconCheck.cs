using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelL.Design;
using SkiaSharp;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private static readonly string[] DesktopIconKinds =
    [
        "PlayCircleOutline", "FolderOutline", "ArrowDownCircleOutline", "CompassOutline", "PuzzleOutline",
        "Earth", "ServerNetwork", "CreationOutline", "CoffeeOutline", "ClockOutline", "FormatListChecks",
        "AccountOutline", "CubeOutline", "WhiteBalanceSunny", "LayersTripleOutline", "FlashOutline",
        "CogSyncOutline", "Speedometer", "GridLarge", "TrendingUp", "TrashCanOutline", "CogOutline", "Plus",
        "WindowMinimize", "WindowMaximize", "WindowRestore", "WindowClose"
    ];

    private async Task CheckDesktopIconsAsync()
    {
        string folder = Path.Combine(Program.DataRoot, "icons");
        Directory.CreateDirectory(folder);
        string theme = CurrentTheme;
        string page = currentPage;
        double width = Width, height = Height;
        var failures = new List<string>();
        var samples = new List<object>();
        int rendered = 0;
        string fontFamily = "";
        ushort[] chineseGlyphs = [];
        void Check(bool passed, string label)
        {
            if (!passed) failures.Add(label);
        }

        try
        {
            foreach (string kind in IconCatalog.Names)
            {
                Rect bounds = Geometry.Parse(IconCatalog.GetPath(kind)).Bounds.Inflate(2.25 / 2);
                Check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= IconCatalog.Frame && bounds.Bottom <= IconCatalog.Frame,
                    kind + " has active stroke outside the shared frame.");
            }
            foreach (string kind in IconCatalog.Names.Concat(DesktopIconKinds).Distinct())
                foreach (int size in new[] { 14, 16, 20, 24 })
                {
                    var icon = new LauncherIcon { Kind = kind, Width = size, Height = size, Foreground = Brushes.White };
                    byte[] pixels = IconPng(icon, size);
                    using var image = SKBitmap.Decode(pixels) ?? throw new IOException("Icon pixels could not be decoded.");
                    int ink = image.Pixels.Count(pixel => pixel.Alpha > 32);
                    Check(ink > 0, kind + " rendered empty at " + size);
                    rendered++;
                    samples.Add(new { kind, size, ink });
                }

            var changing = new LauncherIcon { Kind = "WindowMaximize", Width = 24, Height = 24, Foreground = Brushes.Red };
            byte[] maximum = IconPng(changing, 24);
            changing.Kind = "WindowRestore";
            byte[] restored = IconPng(changing, 24);
            Check(!maximum.SequenceEqual(restored), "Changing the window state did not redraw its geometry.");
            changing.Active = true;
            byte[] active = IconPng(changing, 24);
            Check(!active.SequenceEqual(restored), "The selected icon state did not redraw its stroke.");
            changing.Foreground = Brushes.Blue;
            byte[] recolored = IconPng(changing, 24);
            Check(!recolored.SequenceEqual(active), "Changing foreground did not redraw the icon.");

            foreach (string mode in new[] { "dark", "light" })
            {
                SelectTheme(mode, persist: false);
                foreach (int viewport in new[] { 800, 1180 })
                {
                    Width = viewport; Height = 760;
                    ShowPage("library");
                    await SettleIconsAsync();
                    var selected = Navigation.Children.OfType<Button>().Single(button => (string?)button.Tag == "library");
                    var selectedIcon = selected.GetLogicalDescendants().OfType<LauncherIcon>().Single();
                    Check(selectedIcon.Active && !selectedIcon.Focusable && !selectedIcon.IsHitTestVisible,
                        "The selected navigation icon lost its state or became an extra input target.");
                    Check(Equals(selectedIcon.Foreground, selected.Foreground), "Navigation icon foreground did not follow its button.");
                    SaveIconWindow(Path.Combine(folder, $"desktop-{mode}-{viewport}.png"));
                }
                await SaveIconAtlas(folder, mode);
            }

            ShowPage("mods");
            await SettleIconsAsync();
            var remove = ModsView.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("icon-btn")).ToArray();
            Check(remove.Length > 0 && remove.All(button => AutomationProperties.GetName(button)?.StartsWith("删除资源 ") == true),
                "Resource-delete icon buttons do not identify their own targets.");
            ShowPage("ai");
            await SettleIconsAsync();
            var aiActions = AiView.GetLogicalDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("icon-btn"))
                .ToArray();
            Check(aiActions.Length == 2
                && aiActions[0].GetLogicalDescendants().OfType<LauncherIcon>().Single().Kind == "CogOutline"
                && aiActions[1].GetLogicalDescendants().OfType<LauncherIcon>().Single().Kind == "TrashCanOutline",
                "AI header actions use the shared settings and clear drawings.");
            Check(aiActions.Length == 2
                && AutomationProperties.GetName(aiActions[0]) == "连接设置"
                && AutomationProperties.GetName(aiActions[1]) == "清空对话",
                "AI header icon actions keep explicit accessible names.");
            var font = new Typeface(InstanceName.FontFamily).GlyphTypeface;
            fontFamily = font.FamilyName;
            chineseGlyphs = "设置实例存档资源".Select(character => font.CharacterToGlyphMap.GetGlyph(character)).ToArray();
            Check(fontFamily == "Noto Sans SC" && chineseGlyphs.All(glyph => glyph != 0),
                "The bundled Chinese font did not resolve usable glyphs.");
        }
        finally
        {
            SelectTheme(theme, persist: false);
            Width = width; Height = height;
            ShowPage(page);
        }
        File.WriteAllText(Path.Combine(Program.DataRoot, "desktop-icons.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, renderedSamples = rendered, samples,
            canonicalCount = IconCatalog.Names.Count, fontFamily, chineseGlyphs,
            themes = new[] { "dark", "light" }, viewports = new[] { 800, 1180 },
            nativeScreenReaderVerified = false
        }));
        if (failures.Count > 0) throw new IOException("Desktop icon check: " + string.Join("; ", failures));
    }

    private static byte[] IconPng(LauncherIcon icon, int size)
    {
        icon.Measure(new Size(size, size));
        icon.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(icon);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private async Task SaveIconAtlas(string folder, string mode)
    {
        if (WindowShell.Child is not Grid root) throw new IOException("The desktop icon atlas needs the window surface.");
        var rows = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, ItemWidth = 175, ItemHeight = 74 };
        foreach (string kind in IconCatalog.Names)
        {
            var sizes = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
            foreach (int size in new[] { 14, 16, 20, 24 })
                sizes.Children.Add(new LauncherIcon { Kind = kind, Width = size, Height = size });
            rows.Children.Add(new StackPanel { Spacing = 8, Children =
            {
                sizes, new TextBlock { Text = kind, FontSize = 10.5, TextWrapping = TextWrapping.Wrap }
            } });
        }
        var atlas = new Border { Padding = new Thickness(24), Child = rows,
            Background = mode == "light" ? Brushes.White : new SolidColorBrush(Color.Parse("#13161B")) };
        root.Children.Add(atlas);
        try
        {
            Width = 1180; Height = Math.Max(760, Math.Ceiling(IconCatalog.Names.Count / 6.0) * 74 + 52);
            await SettleIconsAsync();
            SaveIconWindow(Path.Combine(folder, "atlas-" + mode + ".png"));
        }
        finally { root.Children.Remove(atlas); }
    }

    private void SaveIconWindow(string path)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
        bitmap.Render(this);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }

    private static async Task SettleIconsAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(80);
    }
}
