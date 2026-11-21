using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HelL.Design;
using SkiaSharp;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckMobileIconsAsync()
    {
        var failures = new List<string>();
        int checks = 0;
        void Check(bool passed, string label)
        {
            checks++;
            if (!passed) failures.Add(label);
        }
        LauncherIcon? Drawing(Button button) => button.GetLogicalDescendants().OfType<LauncherIcon>().FirstOrDefault();
        string Symbol(Button button) => Drawing(button)?.Kind ?? "";
        Button Tab(Panel panel, string tag) => panel.Children.OfType<Button>().Single(button => (string?)button.Tag == tag);

        ShowPage("more");
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Check(Symbol(Tab(BottomNavigation, "more")) == "CogOutline", "settings keeps a settings symbol while selected");
        Check(ChromeTitle.Text == "设置", "settings navigation and page title agree");
        Check(AutomationProperties.GetName(Tab(BottomNavigation, "home")) == "启动", "launch accessible name matches its visible label");
        Check(ToolTip.GetTip(Tab(RailNavigation, "more"))?.ToString() == "设置", "settings tooltip matches its destination");
        ShowPage("account");
        Check(Tab(RailNavigation, "account").Classes.Contains("current") && !Tab(RailNavigation, "more").Classes.Contains("current"),
            "account keeps its own selected side navigation entry");
        Check(Tab(BottomNavigation, "more").Classes.Contains("current"), "account remains under settings in the phone tab bar");
        foreach (var (tag, symbol) in new[] { ("home", "PlayCircleOutline"), ("library", "FolderOutline"),
            ("resources", "CompassOutline"), ("ai", "CreationOutline"), ("more", "CogOutline") })
        {
            ShowPage(tag);
            Button button = Tab(BottomNavigation, tag);
            Check(Symbol(button) == symbol && Drawing(button)?.Active == true, "selected " + tag + " retains its semantic drawing");
            Check(BottomNavigation.Children.OfType<Button>().Where(other => !ReferenceEquals(other, button))
                .All(other => Drawing(other)?.Active == false), "only the current tab has an active drawing for " + tag);
        }
        var samples = new List<object>();
        foreach (string symbol in new[] { "PlayCircleOutline", "FolderOutline", "CompassOutline", "CreationOutline", "CogOutline", "AccountOutline", "Magnify", "Plus" })
            foreach (int size in new[] { 16, 20, 24 })
            {
                var icon = new LauncherIcon { Kind = symbol, Width = size, Height = size, Foreground = Brushes.Black };
                IconPixels pixels = RenderIconSample(icon, size);
                Check(pixels.Ink > 4 && pixels.Ink < size * size / 2, symbol + " renders a readable outline at " + size);
                Check(!icon.Focusable && !icon.IsHitTestVisible && AutomationProperties.GetAccessibilityView(icon) == AccessibilityView.Raw,
                    symbol + " remains decorative inside its action");
                samples.Add(new { symbol, size, pixels.Ink, pixels.Left, pixels.Top, pixels.Right, pixels.Bottom });
            }
        var activeIcon = new LauncherIcon { Kind = "CogOutline", Width = 24, Height = 24, Foreground = Brushes.Black };
        int restingInk = RenderIconSample(activeIcon, 24).Ink;
        activeIcon.Active = true;
        Check(RenderIconSample(activeIcon, 24).Ink > restingInk, "active navigation increases its drawing weight without changing its symbol");
        int originalTheme = ThemePicker.SelectedIndex;
        foreach (var (theme, name) in new[] { (0, "dark"), (1, "light") })
        {
            ThemePicker.SelectedIndex = theme;
            ShowPage("more");
            await Capture("icons-settings-" + name);
            Check(Drawing(Tab(BottomNavigation, "more"))?.Foreground?.ToString() == Tab(BottomNavigation, "more").Foreground?.ToString(),
                "navigation icon inherits readable " + name + " text color");
            Check((LibraryAddButton.Content as LauncherIcon)?.Foreground?.ToString() == LibraryAddButton.Foreground?.ToString(),
                "the primary add icon keeps its " + name + " action contrast");
        }
        ThemePicker.SelectedIndex = originalTheme;
        var backButtons = new Control[] { DetailsPanel, CreatePage, ManagePage }
            .SelectMany(page => page.GetLogicalDescendants().OfType<Button>().Where(button => button.Classes.Contains("back")))
            .ToArray();
        Check(backButtons.Length == 3, "mobile detail and instance pages keep one back action each");
        Check(backButtons.Length == 3 && backButtons.All(button => Symbol(button) == "ChevronLeft"),
            "mobile back actions use the shared left-chevron drawing");
        Check(backButtons.Length == 3
            && AutomationProperties.GetName(backButtons[0]) == "返回资源搜索"
            && backButtons.Skip(1).All(button => AutomationProperties.GetName(button) == "返回版本管理"),
            "mobile back actions expose destination-specific accessible names");
        ShowPage("home");
        await Capture("icons-home");
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-icons.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, checks, samples, vectorDrawings = true, desktopHosted = !OperatingSystem.IsAndroid(),
            physicalDeviceVerified = false, talkBackVerified = false
        }));
        if (failures.Count > 0) throw new IOException("Mobile icon checks failed: " + string.Join("; ", failures));
    }

    private sealed record IconPixels(int Ink, int Left, int Top, int Right, int Bottom);

    private static IconPixels RenderIconSample(LauncherIcon icon, int size)
    {
        icon.Measure(new Size(size, size));
        icon.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(icon);
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, new PngBitmapEncoderOptions());
        encoded.Position = 0;
        using var pixels = SKBitmap.Decode(encoded) ?? throw new IOException("The icon sample could not be decoded.");
        int ink = 0, left = size, top = size, right = -1, bottom = -1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (pixels.GetPixel(x, y).Alpha < 80) continue;
                ink++;
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
        return new IconPixels(ink, left, top, right, bottom);
    }
}
