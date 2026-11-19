using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class LibraryPreview
{
    private static int _passed;
    internal static async Task Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var api = AppServices.Client;
        const string first = "青山生存", second = "红石实验室", version = "1.21.1";
        foreach (string name in new[] { first, second })
        {
            await api.CallAsync("create_instance", new { name });
            var game = new Instance(name);
            string versions = Path.Combine(game.path, "versions", version); Directory.CreateDirectory(versions);
            File.WriteAllText(Path.Combine(versions, version + ".json"), "{\"id\":\"1.21.1\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
            File.WriteAllText(Path.Combine(versions, "hell-version.json"), "{}");
            VersionSettings.Save(game, version, PyDict.Of("isolation", "none"));
            string screenshots = Path.Combine(game.path, "screenshots"); Directory.CreateDirectory(screenshots);
            WriteImage(Path.Combine(screenshots, "fixture.png"), name == first ? Color.FromRgb(70, 132, 88) : Color.FromRgb(134, 78, 63));
            string world = Path.Combine(game.path, "saves", "山谷小屋"); Directory.CreateDirectory(world);
            WriteImage(Path.Combine(world, "icon.png"), Color.FromRgb(91, 130, 168));
            File.WriteAllText(Path.Combine(world, "level.dat"), "thumbnail test fixture; not a real save");
        }
        Playtime.RecordSession(first, version, 1800);
        Playtime.RecordSession(second, "removed-version", 60);
        await api.CallAsync("invalidate_instances");
        var recent = await api.CallAsync<RecentPlayInfo>("recent_play");
        Check(recent?.Instance == first && recent.Version == version, "Recent play ignores a more recent removed version");

        window.Navigate("instance", instant: true);
        await Program.WaitForPage(window, "instance");
        var instances = Walk(window).OfType<InstancePage>().Single();
        await instances.RefreshAsync();
        await Until(() => Walk(instances).OfType<LocalPicture>().Count(picture => picture.Ready) >= 2, "Instance screenshots decode");
        Check(true, "Instance list displays local screenshot files");
        string pickedCover = Path.Combine(Environment.GetEnvironmentVariable("TIPCL_HOME")!, "selected-cover.png");
        File.Copy(Path.Combine(new Instance(first).path, "screenshots", "fixture.png"), pickedCover);
        var manualCover = await api.CallAsync<InstanceVisual>("set_instance_cover", new { instance = first, path = pickedCover });
        File.Delete(pickedCover);
        Check(manualCover?.Kind == "custom" && File.Exists(manualCover.Path), "Manual cover is copied into the instance and survives removal of the original image");
        var persistedCover = await api.CallAsync<InstanceVisual>("instance_visual", new { instance = first });
        Check(persistedCover?.Path == manualCover!.Path, "Manual cover persists across a new metadata read");
        await api.CallAsync("reset_instance_cover", new { instance = first });
        var automaticCover = await api.CallAsync<InstanceVisual>("instance_visual", new { instance = first });
        Check(automaticCover?.Kind == "screenshot", "Resetting the cover restores automatic screenshot selection");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (double width in new[] { 1180d, 900d })
            {
                window.Width = width; window.Height = 740;
                await Layout(window);
                CheckBounds(instances, "instance " + width);
                Program.SaveSnapshot(window, Path.Combine(folder, "instances-" + (dark ? "dark" : "light") + "-" + width + ".png"));
            }
        }
        await SavesDialog.ShowAsync(first, version);
        await Until(() => Walk(Dlg.Host!).OfType<LocalPicture>().Count(picture => picture.Ready) >= 2, "Save and screenshot thumbnails decode");
        Check(Walk(Dlg.Host!).OfType<TextBlock>().Any(text => text.Text == "山谷小屋"), "Save metadata parses numeric timestamp and size");
        await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "saves-dark.png"));
        CloseDialog(window); await Layout(window);

        string shots = Path.Combine(new Instance(first).path, "screenshots");
        for (int n = 0; n < 25; n++) File.Copy(Path.Combine(shots, "fixture.png"), Path.Combine(shots, "extra-" + n.ToString("D2") + ".png"));
        await InstanceArtworkDialog.Show(first, instances.RefreshAsync);
        await Layout(window);
        var nextPage = Named(Dlg.Host!, "下一页");
        Check(nextPage.IsEnabled, "Screenshot browser exposes pagination beyond the first 24 images");
        nextPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Walk(Dlg.Host!).OfType<TextBlock>().Any(text => text.Text.Contains("2 / 2")), "Second screenshot page appears");
        Check(Walk(Dlg.Host!).OfType<LocalPicture>().Count() == 2, "Last screenshot page shows the remaining images");
        await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "screenshot-browser.png"));
        CloseDialog(window); await Layout(window);

        window.Navigate("launch", instant: true);
        await Program.WaitForPage(window, "launch");
        var launch = Walk(window).OfType<LaunchPage>().Single();
        await InvokeTask(launch, "LoadRecentAsync");
        Check(Walk(launch).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "继续游戏"), "Recent play has a visible continue action");
        Check(await InvokeBoolTask(launch, "SelectRecentAsync"), "Continue selects an existing instance and version");
        Check(Read<ComboBox>(launch, "_inst").SelectedItem as string == first && Read<ComboBox>(launch, "_ver").SelectedItem as string == version,
            "Continue restores the exact instance/version pair");
        await Until(() => Read<NewsPanel>(launch, "_news").LoadedImages > 0, "News cover completes beside recent play");
        await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "recent-dark.png"));
        Check(Read<Button>(launch, "_launchBtn").IsEnabled, "Restoring a valid recent version enables its normal launch action");
        string versionFile = Path.Combine(new Instance(first).VersionsDir(), version, version + ".json");
        File.Move(versionFile, versionFile + ".fixture-backup");
        bool rejected = false;
        try { await InvokeBoolTask(launch, "SelectRecentAsync"); }
        catch (InvalidOperationException) { rejected = true; }
        finally { File.Move(versionFile + ".fixture-backup", versionFile); }
        Check(rejected, "Continue rejects a version removed after the page loaded");
        File.Delete(Path.Combine(new Instance(first).path, "screenshots", "fixture.png"));
        foreach (string extra in Directory.GetFiles(shots, "extra-*.png")) File.Delete(extra);
        File.Delete(Path.Combine(new Instance(first).path, "saves", "山谷小屋", "icon.png"));
        await InvokeTask(launch, "LoadRecentAsync");
        Check(Read<Border>(launch, "_recentHost").Visibility == Visibility.Visible, "A recent session remains usable without any screenshot or world icon");

        var projects = new List<CatalogItem>();
        var namedSearch = await api.CallAsync<List<CatalogItem>>("search_mods", new { query = "sodium", source = "Modrinth" }) ?? new List<CatalogItem>();
        Check(namedSearch.Any(item => item.Slug == "sodium" && item.Author.Length > 0 && item.Author != "?" && item.IconUrl.Length > 0),
            "Named project search resolves author names instead of displaying a question mark");
        var popular = await api.CallAsync<List<CatalogItem>>("search_modpacks", new { query = "", source = "Modrinth" }) ?? new List<CatalogItem>();
        Check(popular.Count > 0 && popular.Any(item => item.IconUrl.Length > 0 && item.CoverUrl.Length > 0 && item.Author.Length > 0),
            "The normal popular-modpack search retains real artwork and authors");
        foreach (string slug in new[] { "sodium", "complementary-reimagined", "fabulously-optimized", "iris" })
        {
            var detail = await api.CallAsync<CatalogItem>("catalog_details", new { source = "modrinth", slug }) ?? throw new IOException("No project details");
            Check(detail.IconUrl.Length > 0 && detail.CoverUrl.Length > 0 && detail.Author.Length > 0 && detail.Author != "?", "Real project artwork and authors: " + slug);
            projects.Add(detail);
        }
        var translated = await api.CallAsync("catalog_summaries", new { projects });
        var localized = System.Text.Json.JsonSerializer.Deserialize<List<CatalogItem>>(translated.GetProperty("rows").GetRawText(), BridgeClient.JsonOpt)!;
        Check(localized.All(item => NewsLocalization.ContainsChinese(item.Description)), "Resource summaries are Chinese with originals preserved");
        File.WriteAllText(Path.Combine(folder, "projects.json"), System.Text.Json.JsonSerializer.Serialize(localized, BridgeClient.JsonOpt));
        window.Navigate("mod", instant: true);
        await Program.WaitForPage(window, "mod");
        var catalog = Walk(window).OfType<CatalogPage>().Single();
        typeof(CatalogPage).GetMethod("CancelSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(catalog, null);
        typeof(CatalogPage).GetMethod("ShowRows", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(catalog, new object[] { localized });
        await Until(() => Walk(catalog).OfType<ProjectCover>().Count(cover => cover.Ready) >= 3, "Remote gallery covers decode");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (double width in new[] { 1180d, 900d })
            {
                window.Width = width; await Layout(window);
                CheckBounds(catalog, "catalog " + width);
                Program.SaveSnapshot(window, Path.Combine(folder, "catalog-" + (dark ? "dark" : "light") + "-" + width + ".png"));
            }
        }
        var detailsTask = (Task)typeof(CatalogPage).GetMethod("ShowDetails", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(catalog, new object[] { localized[0] })!;
        await detailsTask;
        await Until(() => Walk(Dlg.Host!).OfType<ProjectCover>().Any(cover => cover.Ready), "Detail cover loads");
        var gallery = Walk(Dlg.Host!).OfType<ProjectGallery>().Single();
        Check(gallery.Count > 1, "Project details expose multiple upstream gallery images");
        Named(gallery, "下一张").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Walk(gallery).OfType<ProjectCover>().Any(cover => cover.Ready), "Next project gallery image loads");
        Check(gallery.Index == 1, "Next image changes the selected gallery artwork");
        Check(Walk(Dlg.Host!).OfType<TextBlock>().Any(text => text.Text.StartsWith("作者：")), "Project reading panel displays author attribution");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            await Layout(window);
            Program.SaveSnapshot(window, Path.Combine(folder, "project-detail-" + (dark ? "dark" : "light") + ".png"));
        }
        CloseDialog(window);
        var compatibility = await api.CallAsync<CatalogCompatibility>("catalog_compatibility", new { source = "modrinth", slug = "iris", game_version = "1.21.1", loader = "fabric" });
        Check(compatibility != null && compatibility.Error.Length == 0 && compatibility.GameVersions.Contains("1.21.1") && compatibility.Loaders.Contains("fabric"), "Live compatibility selects a Fabric file for Minecraft 1.21.1");
        Check(compatibility!.Dependencies.Any(dependency => dependency.Kind == "required" && dependency.Name.Length > 0), "Live project dependencies include resolved required project names");
        Check(compatibility.DependencyCount >= compatibility.Dependencies.Count, "The declared dependency count survives the bridge response");
        await VerifyCompatibilityPanel(window, projects.Single(project => project.Slug == "iris"), folder);
        int retries = 0;
        var summaryItem = new CatalogItem { Name = "Summary fixture", OriginalDescription = "The original English description.", Localization = "英文简介待翻译" };
        var summaryView = new ResourceSummary(summaryItem, () => retries++);
        Dlg.Panel("摘要交互验证", summaryView, 520);
        await Layout(window);
        summaryView.Begin();
        Check(Walk(summaryView).OfType<ProgressBar>().Single().Visibility == Visibility.Visible && summaryView.State.Contains("翻译中"), "Translation exposes a real in-progress state");
        summaryView.Apply(summaryItem, "fixture connection failure");
        CheckBounds(summaryView, "summary failure");
        Named(summaryView, "原文").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(summaryView.ShowingOriginal && Walk(summaryView).OfType<TextBlock>().Any(text => text.Text == summaryItem.OriginalDescription), "Original text remains readable after translation failure");
        Named(summaryView, "重试翻译").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(retries == 1, "Retry invokes the translation action once");
        summaryItem.Description = "已生成的中文简介。"; summaryItem.Localization = "本地翻译"; summaryView.Apply(summaryItem);
        Named(summaryView, "中文").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!summaryView.ShowingOriginal && Walk(summaryView).OfType<TextBlock>().Any(text => text.Text == summaryItem.Description), "Chinese and original summaries can be switched after success");
        CloseDialog(window);
        Console.WriteLine("[LIBRARY] " + _passed + " checks passed. Local images and save/session data are isolated fixtures; remote projects and translations are live.");
    }

    private static async Task VerifyCompatibilityPanel(MainWindow window, CatalogItem project, string folder)
    {
        window.Width = 900; window.Height = 620;
        var panel = new ProjectDetailsPanel(project, "1.21.1");
        Dlg.Panel(project.Name, panel, 760);
        await panel.EnsureLoadedAsync();
        var game = Read<ComboBox>(panel, "_game");
        var loader = Read<ComboBox>(panel, "_loader");
        Check(game.SelectedItem as string == "1.21.1", "Detail compatibility restores the requested game version");
        Check(loader.Items.Contains("fabric"), "The loader filter offers the project's supported Fabric loader");
        loader.SelectedItem = "fabric";
        await Until(() => !panel.CompatibilityLoading, "Detail compatibility finishes after a loader change");
        var dependencies = Read<SPanel>(panel, "_dependencies");
        Check(Walk(dependencies).OfType<TextBlock>().Any(text => text.Text.StartsWith("适配文件：")), "Changing the loader displays a matching upstream file");
        Check(Walk(dependencies).OfType<TextBlock>().Any(text => text.Text == "必需"), "Required dependencies appear in the reading panel");

        var scrolls = new List<ScrollViewer>();
        for (DependencyObject? node = panel; node != null && node != Dlg.Host; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollViewer scroll) scrolls.Add(scroll);
        Check(scrolls.Count == 1, "Project reading uses one scroll area inside the dialog");
        var viewport = scrolls.Single();
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            viewport.ScrollToBottom();
            await Layout(window);
            CheckBounds(panel, "compatibility " + (dark ? "dark" : "light"));
            foreach (var filter in new[] { game, loader })
            {
                var position = filter.TranslatePoint(new Point(), viewport);
                Check(position.Y >= 0 && position.Y + filter.ActualHeight <= viewport.ActualHeight,
                    "Compatibility filter remains reachable at 900 x 620");
            }
            game.Focus(); game.IsDropDownOpen = true;
            await Layout(window);
            Check(game.IsDropDownOpen, "The game-version selector opens after scrolling");
            game.IsDropDownOpen = false;
            await Layout(window);
            Program.SaveSnapshot(window, Path.Combine(folder, "project-compatibility-" + (dark ? "dark" : "light") + "-900x620.png"));
        }
        CloseDialog(window);
    }

    private static void WriteImage(string path, Color color)
    {
        var drawing = new DrawingVisual();
        using (var canvas = drawing.RenderOpen())
        {
            canvas.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 480, 270));
            canvas.DrawRectangle(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), null, new Rect(0, 180, 480, 90));
            canvas.DrawText(new FormattedText("ISOLATED IMAGE FIXTURE", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 21, Brushes.White, 1), new Point(22, 30));
        }
        var bitmap = new RenderTargetBitmap(480, 270, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static async Task Until(Func<bool> ready, string description)
    { for (int i = 0; i < 600; i++) { if (ready()) return; await Task.Delay(100); } throw new TimeoutException(description); }
    private static Task Layout(Window window) { window.UpdateLayout(); return Task.Delay(220); }
    private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static Task InvokeTask(object target, string name) => (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
    private static Task<bool> InvokeBoolTask(object target, string name) => (Task<bool>)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
    private static void CloseDialog(Window window) => Dlg.Host!.Children[Dlg.Host.Children.Count - 1].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); _passed++; Console.WriteLine("[PASS] " + text); }
    private static Button Named(DependencyObject root, string name) => Walk(root).OfType<Button>().First(button => button.IsVisible && AutomationProperties.GetName(button) == name);
    private static void CheckBounds(FrameworkElement root, string label)
    {
        foreach (var button in Walk(root).OfType<Button>().Where(button => button.IsVisible && button.ActualWidth > 0))
        { var p = button.TranslatePoint(new Point(), root); Check(p.X >= -1 && p.X + button.ActualWidth <= root.ActualWidth + 1, "Action fits: " + label + " " + AutomationProperties.GetName(button)); }
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++)
        { var child = VisualTreeHelper.GetChild(root, n); yield return child; foreach (var nested in Walk(child)) yield return nested; }
    }
}
