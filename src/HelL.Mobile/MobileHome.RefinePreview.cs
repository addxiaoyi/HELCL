using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HelL.Portable;
using SkiaSharp;

namespace HelL.Mobile;

public sealed partial class MobileHome
{
    private async Task CheckMobileRefinementAsync()
    {
        var failures = new List<string>();
        var checks = new List<string>();
        var samples = new List<object>();
        void Check(bool passed, string label)
        {
            checks.Add(label);
            if (!passed) failures.Add(label);
        }
        int theme = ThemePicker.SelectedIndex;
        string page = currentPage;
        string[] history = pageHistory.ToArray();
        var rows = Projects.ItemsSource;
        object? selection = Projects.SelectedItem;
        bool filters = SearchFilters.IsVisible, search = SearchPanel.IsVisible, details = DetailsPanel.IsVisible;
        Vector offset = ResourcesPage.Offset;
        Vector readingOffset = resourceListOffset;
        var fixture = new CatalogProject("refine-fixture", "refine-fixture", "modrinth", "resourcepack",
            "手机资源列表 · 很长的中文项目名称", "Mobile fixture", "验收夹具，不是上游资源",
            "检查中文换行、触控布局和文件选择。", "Mobile layout fixture; no unrelated image is used.", "", 0);
        TextBlock FindTitle(string value) => Projects.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == value && text.IsEffectivelyVisible);
        async Task<TextBlock> WaitTitle(string value)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Projects.UpdateLayout();
                TextBlock? title = Projects.GetVisualDescendants().OfType<TextBlock>()
                    .SingleOrDefault(text => text.Text == value && text.IsEffectivelyVisible);
                if (title is not null) { await SettleRefinementAsync(); return title; }
                await Task.Delay(25);
            }
            throw new IOException("The expected resource row did not become visible: " + value);
        }
        static Button FindAcquire(TextBlock title) => title.GetVisualAncestors().OfType<ListBoxItem>().First()
            .GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "获取");
        try
        {
            foreach (var (index, name) in new[] { (0, "dark"), (1, "light") })
            {
                ThemePicker.SelectedIndex = index;
                ShowPage("home", remember: false);
                HomePage.Offset = Vector.Zero;
                await SettleRefinementAsync();
                var group = InstanceContent.Children.OfType<Border>().Single();
                var tones = ReadSurfaceTones(group);
                int variation = Math.Max(Math.Abs(tones.Top.Red - tones.Bottom.Red),
                    Math.Max(Math.Abs(tones.Top.Green - tones.Bottom.Green), Math.Abs(tones.Top.Blue - tones.Bottom.Blue)));
                Color surface = Color.FromRgb(tones.Top.Red, tones.Top.Green, tones.Top.Blue);
                double bodyContrast = Contrast(BrushColor(SelectedVersionDetail.Foreground), surface);
                double labelContrast = Contrast(BrushColor(InstancePickerLabel.Foreground), surface);
                double actionContrast = Contrast(BrushColor(LaunchButton.Foreground), BrushColor(LaunchButton.Background));
                Check(tones.Top.Alpha == 255 && tones.Bottom.Alpha == 255, name + " surface contrast is measured against opaque rendered pixels");
                Check(variation <= 2, name + " grouped surface stays quiet across its height");
                Check(bodyContrast >= 4.5 && labelContrast >= 4.5, name + " instance value and label retain normal-text contrast");
                Check(actionContrast >= 4.5, name + " primary action keeps normal-text contrast");
                Check(SelectedVersionDetail.FontSize > InstancePickerLabel.FontSize,
                    name + " the selected instance is more prominent than its field label");
                foreach (Button button in new[] { InstancePicker, HomeAccountButton, LaunchButton })
                    Check(button.Bounds.Height >= 44, name + " home action retains a full touch target: " + button.Name);
                Point action = LaunchButton.TranslatePoint(default, HomePage) ?? default;
                bool actionVisible = action.Y >= 0 && action.Y + LaunchButton.Bounds.Height <= HomePage.Bounds.Height;
                Check(actionVisible, name + " primary instance action remains in the initial home viewport");
                bool homeStacked = !wide && EffectiveTextScale >= 1.5;
                Point accountPosition = HomeAccountLabel.TranslatePoint(default, InstanceContent) ?? default;
                Point instancePosition = SelectedVersionDetail.TranslatePoint(default, InstanceContent) ?? default;
                Point accountTitlePosition = HomeAccountTitle.TranslatePoint(default, InstanceContent) ?? default;
                double accountRight = accountPosition.X + HomeAccountLabel.Bounds.Width;
                double instanceRight = instancePosition.X + SelectedVersionDetail.Bounds.Width;
                bool accountAligned = homeStacked
                    ? Math.Abs(accountPosition.X - accountTitlePosition.X) < 1
                        && Math.Abs(accountPosition.X - instancePosition.X) < 1 && Grid.GetRow(HomeAccountLabel) == 1
                    : Math.Abs(accountRight - instanceRight) < 1
                        && Math.Abs(HomeAccountLabel.Bounds.Width - HomeAccountLabel.TextLayout.WidthIncludingTrailingWhitespace) < 1;
                Check(accountAligned, name + (homeStacked
                    ? " stacked account and instance values align with the field leading edge"
                    : " single-row account and instance values share the trailing column edge"));
                await Capture("refine-home-" + name);
                string? instanceTitle = SelectedVersionDetail.Text;
                SelectedVersionDetail.Text = new string('长', 48);
                await SettleRefinementAsync();
                Point valuePosition = SelectedVersionDetail.TranslatePoint(default, InstancePickerLayout) ?? default;
                Check(InstancePickerLabel.Bounds.Width >= 20 && valuePosition.X >= 0
                    && valuePosition.X + SelectedVersionDetail.Bounds.Width <= InstancePickerLayout.Bounds.Width + 1,
                    name + " a long instance name preserves its label and stays within the picker");
                await Capture("refine-long-name-" + name);
                SelectedVersionDetail.Text = instanceTitle;
                await SettleRefinementAsync();

                Projects.ItemsSource = new[] { fixture };
                Projects.SelectedIndex = 0;
                SearchPanel.IsVisible = true;
                SearchFilters.IsVisible = DetailsPanel.IsVisible = false;
                ShowPage("resources", remember: false);
                ResourcesPage.Offset = Vector.Zero;
                await SettleRefinementAsync();
                TextBlock title = FindTitle(fixture.Title);
                var row = title.GetVisualAncestors().OfType<ListBoxItem>().First();
                Button acquire = FindAcquire(title);
                Check(ResourcesTitle.FontSize >= ResourcesSubtitle.FontSize * 1.5,
                    name + " page title and provider caption have a clear size hierarchy");
                Check(title.FontSize >= ResourcesTitle.FontSize * 0.6 && acquire.FontSize >= ResourcesSubtitle.FontSize,
                    name + " dynamically created resource text respects the active text scale");
                Check(acquire.Bounds.Height >= 44 && acquire.Bounds.Width >= 44,
                    name + " resource acquisition retains its touch target");
                Point acquirePosition = acquire.TranslatePoint(default, row) ?? default;
                Check(acquirePosition.X >= 0 && acquirePosition.X + acquire.Bounds.Width <= row.Bounds.Width + 1,
                    name + " resource action stays within its row");
                Check(title.Bounds.Width >= 40 && row.Bounds.Height >= acquire.Bounds.Height,
                    name + " resource title retains useful reading space beside its action");
                bool stacked = !wide && EffectiveTextScale >= 1.5;
                Point titlePosition = title.TranslatePoint(default, row) ?? default;
                Check(!stacked || title.Bounds.Width >= row.Bounds.Width * 0.6 && title.TextLayout.TextLines.Count >= 2
                    && acquirePosition.Y >= titlePosition.Y + title.Bounds.Height,
                    name + " large narrow text gets a readable multi-line title before its action");
                double titleSize = title.FontSize, actionSize = acquire.FontSize;
                bool stable = true;
                for (int refresh = 0; refresh < 3; refresh++)
                {
                    Projects.ItemsSource = new[] { fixture with { Id = "refine-refresh-" + refresh } };
                    Projects.SelectedIndex = 0;
                    await SettleRefinementAsync();
                    TextBlock refreshed = FindTitle(fixture.Title);
                    Button refreshedAction = FindAcquire(refreshed);
                    stable &= Math.Abs(refreshed.FontSize - titleSize) < 0.01 && Math.Abs(refreshedAction.FontSize - actionSize) < 0.01;
                }
                Check(stable, name + " repeated resource refreshes keep the same text scale");
                ShowPage("home", remember: false);
                var arriving = fixture with { Id = "refine-arriving", Title = "返回后显示的资源" };
                Projects.ItemsSource = new[] { fixture, arriving };
                await SettleRefinementAsync();
                ShowPage("resources", remember: false);
                ResourcesPage.Offset = Vector.Zero;
                await SettleRefinementAsync();
                Projects.ScrollIntoView(arriving);
                TextBlock lateTitle = await WaitTitle(arriving.Title);
                Check(Math.Abs(lateTitle.FontSize - titleSize) < 0.01,
                    name + " returning to results created while hidden applies the active text scale");
                Projects.ItemsSource = new[] { fixture };
                Projects.SelectedIndex = 0;
                ResourcesPage.Offset = Vector.Zero;
                await SettleRefinementAsync();
                await Capture("refine-resources-" + name);
                title = FindTitle(fixture.Title);
                acquire = FindAcquire(title);
                if (stacked)
                {
                    title.GetVisualAncestors().OfType<ListBoxItem>().First().BringIntoView();
                    await SettleRefinementAsync();
                    await Capture("refine-resource-row-" + name);
                }
                double narrowTitleWidth = title.Bounds.Width;
                bool? widthRoundTrip = null;
                if (stacked && TopLevel.GetTopLevel(this) is Window window)
                {
                    double originalWidth = window.Width, originalHeight = window.Height;
                    try
                    {
                        window.Width = 1180;
                        window.Height = 740;
                        await Task.Delay(250);
                        await SettleRefinementAsync();
                        TextBlock expanded = FindTitle(fixture.Title);
                        Button expandedAction = FindAcquire(expanded);
                        Check(Grid.GetRow(expandedAction) == 0 && expanded.Bounds.Width > narrowTitleWidth + 50
                            && Math.Abs(expanded.FontSize - titleSize) < 0.01,
                            name + " a wider viewport restores the inline action without shrinking text");
                        await Capture("refine-resources-wide-" + name);
                    }
                    finally
                    {
                        window.Width = originalWidth;
                        window.Height = originalHeight;
                        await Task.Delay(250);
                        await SettleRefinementAsync();
                    }
                    title = FindTitle(fixture.Title);
                    acquire = FindAcquire(title);
                    widthRoundTrip = Grid.GetRow(acquire) == 1 && Math.Abs(title.Bounds.Width - narrowTitleWidth) < 1;
                    Check(widthRoundTrip == true, name + " returning narrow restores the stacked resource action and reading width");
                }
                samples.Add(new { theme = name, surface = surface.ToString(), variation, bodyContrast, labelContrast,
                    actionContrast, actionVisible, accountAligned, homeStacked,
                    accountTextLeft = accountPosition.X, accountTextRight = accountRight,
                    instanceTextLeft = instancePosition.X, instanceTextRight = instanceRight,
                    headingSize = ResourcesTitle.FontSize, captionSize = ResourcesSubtitle.FontSize,
                    resourceTitleSize = title.FontSize, resourceActionSize = acquire.FontSize,
                    resourceAction = new { width = acquire.Bounds.Width, height = acquire.Bounds.Height }, titleWidth = title.Bounds.Width,
                    resourceTitleLines = title.TextLayout.TextLines.Count, stacked, widthRoundTrip });
            }
        }
        finally
        {
            ThemePicker.SelectedIndex = theme;
            Projects.ItemsSource = rows;
            Projects.SelectedItem = selection;
            SearchFilters.IsVisible = filters;
            SearchPanel.IsVisible = search;
            DetailsPanel.IsVisible = details;
            ShowPage(page, remember: false);
            await SettleRefinementAsync();
            resourceListOffset = readingOffset;
            ResourcesPage.Offset = offset;
            pageHistory.Clear();
            foreach (string previous in history.Reverse()) pageHistory.Push(previous);
        }
        File.WriteAllText(Path.Combine(EvidenceRoot, "mobile-refinement.json"), JsonSerializer.Serialize(new
        {
            ok = failures.Count == 0, failures, checks, samples, systemTextScale = MobileRuntime.SystemTextScale,
            desktopHosted = !OperatingSystem.IsAndroid(), physicalDeviceVerified = false, talkBackVerified = false
        }));
        if (failures.Count > 0) throw new IOException("Mobile visual refinement failed: " + string.Join("; ", failures));
    }

    private static Color BrushColor(IBrush? brush) => brush is ISolidColorBrush solid
        ? solid.Color : throw new IOException("A text/action contrast sample did not resolve to a solid foreground.");

    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value)
            {
                double channel = value / 255d;
                return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        double light = Luminance(first), dark = Luminance(second);
        return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
    }

    private static (SKColor Top, SKColor Bottom) ReadSurfaceTones(Border surface)
    {
        int width = Math.Max(1, (int)Math.Ceiling(surface.Bounds.Width));
        int height = Math.Max(1, (int)Math.Ceiling(surface.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(surface);
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, new PngBitmapEncoderOptions());
        encoded.Position = 0;
        using var pixels = SKBitmap.Decode(encoded) ?? throw new IOException("The grouped surface sample could not be decoded.");
        return (pixels.GetPixel(8, 16), pixels.GetPixel(8, height - 16));
    }

    private static async Task SettleRefinementAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(80);
    }
}
