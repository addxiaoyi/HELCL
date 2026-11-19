using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HelL;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class CustomizationPreview
{
    private static int _checks;
    internal static async Task Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        var page = Walk(window).OfType<LaunchPage>().Single();
        var dash = (DashHost)typeof(LaunchPage).GetField("_dash", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        dash.ApplyPreset(DashLayout.Preset("focus")); await Layout(window);
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (int width in new[] { 1180, 900 })
            {
                window.Width = width; window.Height = 740; await Layout(window);
                string coordinates = dash.Layout.ToJson(); var normal = Rectangles(dash);
                Program.SaveSnapshot(window, Path.Combine(folder, $"normal-{(dark ? "dark" : "light")}-{width}.png"));
                Click(page, "编辑布局"); await Layout(window); var edited = Rectangles(dash);
                Check(normal.Keys.All(id => SameRect(normal[id], edited[id])), "normal/edit geometry stays identical " + width + "/" + dark);
                Check(coordinates == dash.Layout.ToJson(), "viewport and edit mode preserve stored coordinates");
                CheckBounds(dash);
                Program.SaveSnapshot(window, Path.Combine(folder, $"editing-{(dark ? "dark" : "light")}-{width}.png"));
                var card = Walk(page).OfType<DashHost.CardView>().First();
                var remove = Named(card, "移除模块"); var hit = card.InputHitTest(remove.TranslatePoint(new Point(remove.ActualWidth / 2, remove.ActualHeight / 2), card)) as DependencyObject;
                Check(IsWithin(hit, remove), "editor overlay leaves remove/settings buttons clickable");
                Click(page, "完成编辑"); await Layout(window);
            }
        }
        window.Width = 900; await Layout(window); Click(page, "编辑布局");
        var widths = dash.Layout.Items.ToDictionary(card => card.Id, card => card.W);
        var narrowCard = Cards(dash).First();
        narrowCard.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(narrowCard), 0, Key.Down) { RoutedEvent = Keyboard.KeyDownEvent });
        await Layout(window); Check(dash.Layout.Items.All(card => Math.Abs(card.W - widths[card.Id]) < .00001), "moving in a narrow window preserves saved module widths");
        dash.Undo(); Click(page, "完成编辑");
        window.Width = 1180; App.ApplyTheme(false); await Layout(window); Click(page, "编辑布局");
        dash.AddCard("notes"); dash.AddCard("notes"); dash.AddCard("clock"); await Layout(window);
        await Flush(page);
        Check(dash.CanUndo, "first save creates a custom profile without reloading or clearing editor history");
        var notes = dash.Layout.Items.Where(card => card.Type == "notes").ToList(); Check(notes.Count == 2 && notes[0].Id != notes[1].Id, "multiple notes have independent ids");
        for (int i = 0; i < notes.Count; i++)
        {
            var view = Cards(dash).Single(card => card.Card.Id == notes[i].Id);
            Walk(view).OfType<TextBox>().Single().Text = i == 0 ? "基地坐标 32 / 64 / 12" : "独立的探索计划";
        }
        Check(notes[0].Settings!.Value.GetProperty("text").GetString() != notes[1].Settings!.Value.GetProperty("text").GetString(), "note edits update only their module");
        var moving = Cards(dash).Single(card => card.Card.Id == notes[0].Id); moving.BringIntoView(); await Layout(window);
        string before = dash.Layout.ToJson();
        moving.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(moving), 0, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        await Layout(window); string moved = dash.Layout.ToJson(); Check(before != moved, "keyboard arrow moves a module");
        Click(page, "撤销布局更改"); await Layout(window); Check(dash.Layout.ToJson() == before, "undo restores complete layout");
        Click(page, "重做布局更改"); await Layout(window); Check(dash.Layout.ToJson() == moved, "redo restores movement");
        moving = Cards(dash).Single(card => card.Card.Id == notes[0].Id);
        Click(moving, "设置模块"); await Layout(window);
        var title = Walk(window).OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == "模块名称"); title.Text = "生存世界手记";
        Program.SaveSnapshot(window, Path.Combine(folder, "module-settings.png")); Click(window, "保存"); await Layout(window);
        Check(dash.Layout.Items.Single(card => card.Id == notes[0].Id).Settings!.Value.GetProperty("title").GetString() == "生存世界手记", "module settings apply independently");
        Click(page, "完成编辑"); await Flush(page);
        var saved = await AppServices.Client.CallAsync("get_layout"); var restored = DashLayout.FromJson(saved.GetProperty("doc"));
        Check(restored.Items.Count(card => card.Type == "notes") == 2 && restored.Items.Single(card => card.Id == notes[0].Id).Settings!.Value.GetProperty("text").GetString() == "基地坐标 32 / 64 / 12", "layout and module content round-trip through persistent backend");
        string root = Environment.GetEnvironmentVariable("TIPCL_HOME")!;
        Check(File.ReadAllText(Path.Combine(root, "config.json")).Contains("生存世界手记") || File.ReadAllText(Path.Combine(root, "config.json")).Contains("\\u751F"), "module settings reach on-disk configuration");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_plugin_local_only"] = true } });
        window.Navigate("plugins", instant: true); await Program.WaitForPage(window, "plugins"); await Layout(window);
        var plugins = Walk(window).OfType<PluginsPage>().Single();
        Program.SaveSnapshot(window, Path.Combine(folder, "plugins-market.png"));
        Click(plugins, "安装"); await Until(() => LauncherPlugins.Installed().Count == 1); await Layout(window);
        Check(LauncherPlugins.Installed()[0].Enabled, "market install persists a working plugin");
        Click(plugins, "停用"); await Layout(window); Check(!LauncherPlugins.Installed()[0].Enabled, "plugin disable persists");
        Click(plugins, "启用"); await Layout(window); Check(LauncherPlugins.Installed()[0].Enabled, "plugin can be enabled again");
        window.Navigate("launch", instant: true); await Layout(window); Click(page, "编辑布局");
        dash.AddCard("plugin:desk-clock:main"); await Layout(window);
        var extension = Cards(dash).Single(card => card.Card.Type == "plugin:desk-clock:main"); extension.BringIntoView(); await Layout(window);
        Check(Walk(extension).OfType<TextBlock>().Any(text => text.Text.Contains(DateTime.Now.ToString("HH:mm"))), "installed plugin renders live module");
        Program.SaveSnapshot(window, Path.Combine(folder, "custom-modules.png"));
        Click(page, "完成编辑"); await Flush(page);
        LauncherPlugins.Store.SetEnabled("desk-clock", false); LauncherPlugins.NotifyChanged(); page.OnShown(); await Layout(window);
        Check(Walk(page).OfType<TextBlock>().Any(text => text.Text.Contains("此扩展未安装或已停用")), "disabled plugin leaves a recoverable placeholder");
        LauncherPlugins.Store.SetEnabled("desk-clock", true); LauncherPlugins.NotifyChanged(); page.OnShown(); await Layout(window);
        Check(!Walk(page).OfType<TextBlock>().Any(text => text.Text.Contains("此扩展未安装或已停用")), "reenabled plugin restores its module");
        dash.AddCard("image");
        var invalidImage = dash.Layout.Items.Last();
        dash.ChangeCard(invalidImage, card => { using var invalid = System.Text.Json.JsonDocument.Parse("{\"image\":\"invalid\\u0000path\"}"); card.Settings = invalid.RootElement.Clone(); });
        await Layout(window);
        Check(Walk(page).OfType<TextBlock>().Any(text => text.Text.Contains("此模块暂时无法加载")), "invalid module content is isolated without breaking the dashboard");
        dash.RemoveCard(invalidImage); await Layout(window);
        await WallpaperChecks(window, folder);
        await Flush(page);
        var duplicate = dash.Layout.Clone(); var secondBanner = duplicate.Items.First(card => card.Type == "banner").Clone();
        secondBanner.Id = "banner-imported-duplicate"; duplicate.Items.Add(secondBanner); dash.Load(duplicate); await Layout(window);
        Check(Walk(page).OfType<TextBlock>().Any(text => text.Text.Contains("此内置模块已在启动页中")), "importing duplicate core modules cannot crash WPF");
        dash.RemoveCard(dash.Layout.Items.Single(card => card.Id == secondBanner.Id));
        await Layout(window);
        var closingNote = dash.Layout.Items.First(card => card.Type == "notes");
        var noteView = Cards(dash).Single(card => card.Card.Id == closingNote.Id);
        Walk(noteView).OfType<TextBox>().Single().Text = "关闭窗口前的最后一次编辑";
        var closed = new TaskCompletionSource<bool>(); window.Closed += (_, _) => closed.TrySetResult(true);
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Close(); await Task.WhenAny(closed.Task, Task.Delay(5000)); Check(closed.Task.IsCompleted, "window closes after pending layout edits finish");
        using var onDisk = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config.json")));
        var persistedNote = onDisk.RootElement.GetProperty("ui_layout").GetProperty("items").EnumerateArray().Single(card => card.GetProperty("id").GetString() == closingNote.Id);
        Check(persistedNote.GetProperty("settings").GetProperty("text").GetString() == "关闭窗口前的最后一次编辑", "closing immediately after typing preserves the latest note on disk");
        Console.WriteLine("[CUSTOMIZATION] " + _checks + " checks passed");
    }
    private static async Task WallpaperChecks(MainWindow window, string folder)
    {
        string config = WallpaperEngine.DiscoverConfigs().FirstOrDefault() ?? throw new IOException("No Wallpaper Engine installation available for live checks.");
        string original = Hash(config); var papers = WallpaperEngine.Read(config);
        Console.WriteLine("[ENGINE] " + string.Join(", ", papers.Select(paper => paper.Monitor + ":" + paper.Kind)));
        Check(papers.Count >= 1 && papers.All(paper => paper.DisplayFile.Length > 0), "detects actual display projects and their media");
        var video = papers.First(paper => paper.Kind == "video");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "engine", ["ui_wallpaper_engine_config"] = config, ["ui_wallpaper_monitor"] = video.Monitor, ["ui_background_dim"] = 30 } });
        Check(await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_mode" }) == "engine", "wallpaper mode round-trips through settings API");
        await Wallpaper.ReloadAsync(); await Until(() => Wallpaper.VideoReady || Wallpaper.Error.Length > 0, 15000);
        Check(Wallpaper.VideoReady && Wallpaper.CurrentSource == video.File, "actual Wallpaper Engine video decodes in launcher");
        Wallpaper.SetWindowActive(false); await Task.Delay(100); Wallpaper.SetWindowActive(true);
        await WallpaperDialog.Show(); await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "wallpaper-engine-video.png"));
        Click((DependencyObject)window.FindName("DialogHost"), "关闭"); await Layout(window);
        var scene = papers.FirstOrDefault(paper => !paper.Direct);
        if (scene != null)
        {
            await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_monitor"] = scene.Monitor } });
            await Wallpaper.ReloadAsync(); Check(Wallpaper.CurrentSource == scene.Preview && Wallpaper.SyncStatus.Contains("预览图"), "scene/web fallback is the actual project preview and clearly labelled");
        }
        Check(Hash(config) == original, "Wallpaper Engine configuration remains unchanged");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_background"] = "", ["ui_background_folder"] = "" } });
        await Wallpaper.ReloadAsync(); Check(!Wallpaper.Active, "turning off synchronization clears playback");
    }
    private static string Hash(string file) { using var hash = SHA256.Create(); using var stream = File.OpenRead(file); return BitConverter.ToString(hash.ComputeHash(stream)); }
    private static void CheckBounds(DashHost dash)
    {
        var boxes = Rectangles(dash).Values.ToList();
        Check(boxes.All(box => box.Left >= -.1 && box.Right <= dash.ActualWidth + .5), "all modules fit canvas width");
        for (int i = 0; i < boxes.Count; i++) for (int j = i + 1; j < boxes.Count; j++) Check(!boxes[i].IntersectsWith(boxes[j]), "modules have no overlap " + i + "/" + j);
    }
    private static Dictionary<string, Rect> Rectangles(DashHost dash) => Cards(dash).ToDictionary(view => view.Card.Id, view => new Rect(view.TranslatePoint(new Point(), dash), view.RenderSize));
    private static bool SameRect(Rect a, Rect b) => Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;
    private static IEnumerable<DashHost.CardView> Cards(DashHost dash) => dash.Children.OfType<DashHost.CardView>();
    private static Task Flush(LaunchPage page) => (Task)typeof(LaunchPage).GetMethod("FlushLayoutAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static bool IsWithin(DependencyObject? child, DependencyObject parent) { while (child != null) { if (child == parent) return true; child = VisualTreeHelper.GetParent(child); } return false; }
    private static async Task Layout(MainWindow window) { window.UpdateLayout(); await Task.Delay(180); window.UpdateLayout(); }
    private static async Task Until(Func<bool> predicate, int timeout = 5000) { var end = DateTime.UtcNow.AddMilliseconds(timeout); while (!predicate() && DateTime.UtcNow < end) await Task.Delay(100); if (!predicate()) throw new TimeoutException("Timed out waiting for customization state."); }
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException("[FAIL] " + message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static void Click(DependencyObject root, string name) => Named(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static Button Named(DependencyObject root, string name) => Walk(root).OfType<Button>().First(button => button.IsVisible && AutomationProperties.GetName(button) == name);
    internal static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Walk(child)) yield return next; } }
}
