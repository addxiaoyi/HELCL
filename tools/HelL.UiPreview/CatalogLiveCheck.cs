using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class CatalogLiveCheck
{
    private static int _checks;
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        window.Toast("错误提示回归", "验证错误提示可以关闭且不会重复挂载控件。", ToastKind.Error);
        Check(Walk(window).OfType<TextBlock>().Any(text => text.Text == "错误提示回归"), "Error toasts render without reparenting existing controls");
        DismissToasts(window);
        await Task.Delay(400);
        await CheckDependencyInstall(window, folder);
        await CheckScreenshots(window, folder);
        var api = AppServices.Client;
        var verified = new List<object>();
        foreach (string source in new[] { "modrinth", "curseforge" })
        foreach (var kind in CatalogKind.All.Where(kind => source == "curseforge" || kind.Key != "world"))
        {
            var rows = await api.CallAsync<List<CatalogItem>>(kind.SearchMethod, new { query = "", source }) ?? new List<CatalogItem>();
            Check(rows.Count > 0, source + " " + kind.Key + " returns real projects");
            var shown = rows.Take(3).ToList();
            foreach (var project in shown)
            {
                var detail = await api.CallAsync<CatalogItem>("catalog_details", new { source = project.Source, slug = project.Slug ?? "", id = project.IdValue?.ToString() ?? "" }) ?? throw new IOException("No upstream detail");
                Check(project.IconUrl.Length > 0 && project.IconUrl == detail.IconUrl, source + " " + kind.Key + " icon matches its own project: " + project.Name);
                Check(await Thumbs.LoadAsync(project.IconUrl, 64) != null, "The real icon decodes: " + project.Name);
                verified.Add(new { kind = kind.Key, source, name = project.Name, id = project.IdValue, slug = project.Slug, icon = project.IconUrl, galleryCount = detail.Gallery.Count });
            }
            window.Navigate(kind.Key, instant: true);
            await Program.WaitForPage(window, kind.Key);
            var catalog = Walk(window).OfType<CatalogPage>().Single();
            var sourcePicker = (ComboBox)typeof(CatalogPage).GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(catalog)!;
            typeof(CatalogPage).GetField("_sync", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(catalog, true);
            sourcePicker.SelectedItem = source == "curseforge" ? "CurseForge" : "Modrinth";
            typeof(CatalogPage).GetField("_sync", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(catalog, false);
            typeof(CatalogPage).GetMethod("CancelSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(catalog, null);
            typeof(CatalogPage).GetField("_summaryBusy", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(catalog, true);
            typeof(CatalogPage).GetMethod("ShowRows", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(catalog, new object[] { shown });
            await Until(() => Walk(catalog).OfType<ProjectCover>().Count(cover => cover.Ready) >= Math.Min(2, shown.Count), "Visible catalog icons appear");
            App.ApplyTheme(source == "curseforge"); window.Width = 900; window.Height = 740; window.UpdateLayout();
            await Task.Delay(600);
            Program.SaveSnapshot(window, Path.Combine(folder, source + "-" + kind.Key + ".png"));
        }
        File.WriteAllText(Path.Combine(folder, "catalog-artwork.json"), JsonSerializer.Serialize(verified, new JsonSerializerOptions { WriteIndented = true }));
        var cf = await api.CallAsync<CatalogItem>("catalog_details", new { source = "curseforge", id = "238222" });
        Check(cf != null && cf.Gallery.Count > 0, "CurseForge JEI exposes real project screenshots");
        Check(await Thumbs.LoadAsync(cf!.Gallery[0].Url, 600) != null, "CurseForge screenshot bytes decode");
        var compatible = await api.CallAsync<CatalogCompatibility>("catalog_compatibility", new { source = "curseforge", id = "225643", game_version = "1.20.1", loader = "forge" });
        Check(compatible != null && compatible.Error.Length == 0 && compatible.Dependencies.Any(dependency => dependency.ProjectId.Length > 0), "CurseForge Botania resolves Forge compatibility and dependencies");
        File.WriteAllText(Path.Combine(folder, "curseforge-compatibility.json"), JsonSerializer.Serialize(compatible, BridgeClient.JsonOpt));
        Console.WriteLine("[CATALOG LIVE] " + _checks + " checks passed.");
    }

    private static async Task CheckScreenshots(MainWindow window, string folder)
    {
        const string name = "截图压力测试";
        await AppServices.Client.CallAsync("create_instance", new { name });
        string shots = Path.Combine(new Instance(name).path, "screenshots"); Directory.CreateDirectory(shots);
        byte[] image = ScreenshotFixture();
        for (int i = 0; i < 5000; i++) File.WriteAllBytes(Path.Combine(shots, i.ToString("D4") + ".png"), image);
        var timings = new List<double>();
        long before = GC.GetTotalMemory(true);
        ScreenshotPage? page = null;
        foreach (int offset in new[] { 0, 2400, 4992 })
        {
            var watch = Stopwatch.StartNew();
            page = await AppServices.Client.CallAsync<ScreenshotPage>("instance_screenshots", new { instance = name, offset, limit = 24 });
            timings.Add(watch.Elapsed.TotalMilliseconds);
            Check(page?.Total == 5000 && page.Items.Count <= 24, "Five thousand screenshots stay paged at offset " + offset);
        }
        long allocated = GC.GetTotalMemory(true) - before;
        foreach (var item in page!.Items) File.Delete(item.Path);
        var refreshed = await AppServices.Client.CallAsync<ScreenshotPage>("instance_screenshots", new { instance = name, offset = 4992, limit = 24 });
        Check(refreshed?.Offset == 4968 && refreshed.Items.Count == 24, "Deleting the final screenshot page returns to a valid page");
        await InstanceArtworkDialog.Show(name, () => Task.CompletedTask);
        await Until(() => Walk(Dlg.Host!).OfType<LocalPicture>().Count(picture => picture.Ready) == 24, "Full HD screenshot fixtures decode");
        window.UpdateLayout();
        Check(Walk(Dlg.Host!).OfType<LocalPicture>().Count() == 24, "Only one page of thumbnails is mounted");
        long thumbnailBaseline = GC.GetTotalMemory(true);
        using var process = Process.GetCurrentProcess(); process.Refresh(); long privateBaseline = process.PrivateMemorySize64;
        for (int n = 0; n < 10; n++)
        {
            Named(Dlg.Host!, "下一页").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            int pageNumber = n + 2;
            await Until(() => Walk(Dlg.Host!).OfType<TextBlock>().Any(text => text.Text.StartsWith(pageNumber + " / ")) && Walk(Dlg.Host!).OfType<LocalPicture>().Count(picture => picture.Ready) == 24, "Next screenshot page decodes");
        }
        long retainedThumbnails = GC.GetTotalMemory(true) - thumbnailBaseline;
        process.Refresh(); long privateGrowth = process.PrivateMemorySize64 - privateBaseline;
        Check(retainedThumbnails < 64L * 1024 * 1024, "Ten page changes retain less than 64 MiB of additional managed memory");
        foreach (string path in Directory.EnumerateFiles(shots)) File.Delete(path);
        Named(Dlg.Host!, "刷新").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Walk(Dlg.Host!).OfType<TextBlock>().Any(text => text.Text.Contains("0 / 0")), "Empty screenshot folder refreshes");
        Check(Walk(Dlg.Host!).OfType<LocalPicture>().Count() == 0, "Deleted screenshots release the visible thumbnail controls");
        Program.SaveSnapshot(window, Path.Combine(folder, "screenshots-empty-after-refresh.png"));
        Close(window);
        File.WriteAllText(Path.Combine(folder, "screenshot-performance.json"), JsonSerializer.Serialize(new
        { files = 5000, imageWidth = 1920, imageHeight = 1080, pageSize = 24, milliseconds = timings, retainedManagedBytes = allocated,
            tenPageManagedDeltaBytes = retainedThumbnails, tenPagePrivateDeltaBytes = privateGrowth, mountedThumbnails = 24, deletedPageOffset = refreshed!.Offset }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task CheckDependencyInstall(MainWindow window, string folder)
    {
        const string name = "依赖安装测试", version = "1.21.1-fabric";
        var api = AppServices.Client;
        await api.CallAsync("create_instance", new { name });
        var instance = new Instance(name);
        string versions = Path.Combine(instance.VersionsDir(), version); Directory.CreateDirectory(versions);
        File.WriteAllText(Path.Combine(versions, version + ".json"), "{\"id\":\"1.21.1-fabric\",\"inheritsFrom\":\"1.21.1\",\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.16.10\"}]}");
        VersionSettings.Save(instance, version, PyDict.Of("isolation", "all"));
        var compatible = await api.CallAsync<CatalogCompatibility>("catalog_compatibility", new { source = "modrinth", slug = "iris", game_version = "1.21.1", loader = "fabric" });
        var dependency = compatible!.Dependencies.First(row => row.Kind == "required" && row.ProjectType == "mod");
        var details = new ProjectDetailsPanel(new CatalogItem { Name = "Iris", Source = "modrinth", Slug = "iris" }, "1.21.1");
        Dlg.Panel("Iris", details, 760); await details.EnsureLoadedAsync();
        var dependencyRows = (SPanel)typeof(ProjectDetailsPanel).GetField("_dependencies", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(details)!;
        Check(Walk(dependencyRows).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "安装"), "A required mod dependency exposes its install entry");
        Named(dependencyRows, "详情").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Walk(Dlg.Host!).OfType<ProjectDetailsPanel>().Count() == 2, "Dependency detail opens inside the launcher");
        await Walk(Dlg.Host!).OfType<ProjectDetailsPanel>().Last().EnsureLoadedAsync();
        Check(Walk(Dlg.Host!).OfType<ProjectDetailsPanel>().Count() == 2, "Dependency detail preserves the parent resource panel");
        Close(window); await Task.Delay(400); Close(window); await Task.Delay(400);
        var show = DependencyInstallDialog.Show(dependency);
        await Until(() => Walk(Dlg.Host!).OfType<ComboBox>().Any(), "Dependency target choice appears");
        var picker = Walk(Dlg.Host!).OfType<ComboBox>().Last();
        picker.SelectedItem = picker.Items.OfType<DependencyInstallDialog.Target>().Single(target => target.Instance == name && target.Version == version);
        Named(Dlg.Host!, "检查兼容性").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Walk(Dlg.Host!).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "确认安装"), "Concrete dependency confirmation appears");
        Check(!Directory.Exists(Path.Combine(versions, "mods")), "Reading the install plan writes no mod files");
        await Task.Delay(600); window.UpdateLayout();
        Program.SaveSnapshot(window, Path.Combine(folder, "dependency-confirm.png"));
        Named(Dlg.Host!, "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await show;
        Check(!Directory.Exists(Path.Combine(versions, "mods")), "Cancel leaves the target unchanged");
        var plan = await api.CallAsync<DependencyPlan>("prepare_dependency", new { source = dependency.Source, project = dependency.ProjectId, pinned_version = dependency.VersionId, relation = dependency.Kind, instance = name, version });
        var confirmed = DependencyInstallDialog.Show(dependency);
        await Until(() => Walk(Dlg.Host!).OfType<ComboBox>().Any(), "Target choice reopens");
        picker = Walk(Dlg.Host!).OfType<ComboBox>().Last();
        picker.SelectedItem = picker.Items.OfType<DependencyInstallDialog.Target>().Single(target => target.Instance == name && target.Version == version);
        Named(Dlg.Host!, "检查兼容性").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Walk(Dlg.Host!).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "确认安装"), "Install confirmation reopens");
        Named(Dlg.Host!, "确认安装").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        string task = await confirmed ?? throw new IOException("Confirmation did not start installation.");
        var outcome = await api.CallAsync("wait_task", new { task_id = task, timeout = 120 });
        Check(File.Exists(plan!.Destination), "A confirmed dependency installs into the isolated version's mods folder");
        Check(new FileInfo(plan.Destination).Length == plan.Bytes, "Installed dependency size matches the verified plan");
        string duplicateTask = await api.StartTaskAsync("install_dependency", new { token = plan.Token });
        var duplicateOutcome = await api.CallAsync<OpResult>("wait_task", new { task_id = duplicateTask, timeout = 20 });
        Check(duplicateOutcome != null && !duplicateOutcome.Ok, "A second prepared install cannot overwrite the existing file");
        bool duplicateRejected = false;
        try { await api.StartTaskAsync("install_dependency", new { token = plan.Token }); }
        catch (Exception) { duplicateRejected = true; }
        Check(duplicateRejected, "An install confirmation cannot be replayed");
        DismissToasts(window);
        File.WriteAllText(Path.Combine(folder, "dependency-install.json"), JsonSerializer.Serialize(new { plan.Name, plan.Version, plan.Filename, plan.GameVersion, plan.Loader, plan.Bytes, sha256 = Utils.Sha256File(plan.Destination), cancelledPlanWroteNothing = true, isolated = true }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DismissToasts(Window window)
    {
        var host = (Panel)window.FindName("ToastHost");
        foreach (var close in Walk(host).OfType<Button>().Where(button => AutomationProperties.GetName(button) == "关闭").ToArray())
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static byte[] ScreenshotFixture()
    {
        var visual = new DrawingVisual();
        using (var canvas = visual.RenderOpen())
        {
            canvas.DrawRectangle(Brushes.ForestGreen, null, new Rect(0, 0, 1920, 1080));
            canvas.DrawText(new FormattedText("ISOLATED 1920 x 1080 IMAGE FIXTURE", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 70, Brushes.White, 1), new Point(100, 100));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream(); encoder.Save(buffer); return buffer.ToArray();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static async Task Until(Func<bool> ready, string message) { for (int i = 0; i < 1200; i++) { if (ready()) return; await Task.Delay(100); } throw new TimeoutException(message); }
    private static Button Named(DependencyObject root, string name) => Walk(root).OfType<Button>().Last(button => button.IsVisible && AutomationProperties.GetName(button) == name);
    private static void Close(Window window) => Named(Dlg.Host!, "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Walk(child)) yield return nested; } }
}
