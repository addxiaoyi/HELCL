using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HelL;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;
using Path = System.IO.Path;

internal static class DesktopWorkflowPreview
{
    private static int _checks;
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object owner, string key) => (T)owner.GetType().GetField(key, Hidden)!.GetValue(owner)!;
    private static object? Invoke(object owner, string key, params object[] args) => owner.GetType().GetMethod(key, Hidden)!.Invoke(owner, args);
    private static IEnumerable<DependencyObject> Walk(DependencyObject owner) => CustomizationPreview.Walk(owner);
    private static void Click(DependencyObject root, string label) => CustomizationPreview.Named(root, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); _checks++; Console.WriteLine("[PASS] " + label); }
    private static async Task Until(Func<bool> ready, string label, int timeout = 20000)
    { var deadline = DateTime.UtcNow.AddMilliseconds(timeout); while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(100); Check(ready(), label); }
    private static async Task Layout(MainWindow window) { window.UpdateLayout(); await Task.Delay(160); window.UpdateLayout(); }

    internal static void CopyAppearance(string source, string home)
    {
        using var original = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "config.json")));
        var allowed = new HashSet<string> { "ui_dark", "ui_accent", "ui_skin", "ui_glass_tint", "ui_glass_transparency", "ui_glass_frost", "ui_wallpaper_video_optimize", "ui_sidebar_width", "ui_font_family", "ui_wallpaper_mode", "ui_wallpaper_engine_config", "ui_wallpaper_monitor", "ui_background", "ui_background_folder", "ui_background_blur", "ui_background_dim", "ui_nav_style", "ui_nav_groups", "ui_nav_order", "ui_nav_pinned", "ui_nav_hidden", "ui_home_sidebar", "ui_home_sidebar_modules" };
        allowed.UnionWith(new[] { "ui_glass_cards", "ui_glass_controls", "ui_glass_popups" });
        var settings = original.RootElement.EnumerateObject().Where(property => allowed.Contains(property.Name)).ToDictionary(property => property.Name, property => (object)property.Value.Clone());
        settings["first_run"] = false; settings["feedback_consent"] = false; settings["feedback_heartbeat"] = false; settings["auto_check_update"] = false; settings["ui_wallpaper_engine_live"] = false;
        File.WriteAllText(Path.Combine(home, "config.json"), JsonSerializer.Serialize(settings));
        if (File.Exists(Path.Combine(source, "wpf-ui.json")))
        {
            using var ui = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "wpf-ui.json")));
            var prefs = ui.RootElement.EnumerateObject().Where(property => new[] { "SideWidth", "SideCollapsed", "LibraryView", "Dark", "SideFrost", "SideFrostRadius", "Expanded" }.Contains(property.Name)).ToDictionary(property => property.Name, property => property.Value.Clone());
            File.WriteAllText(Path.Combine(home, "wpf-ui.json"), JsonSerializer.Serialize(prefs));
        }
        Console.WriteLine("[APPEARANCE] Copied only allowlisted visual preferences; accounts, game paths and tokens excluded");
    }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch"); await Wallpaper.ReloadAsync();
        var launch = Walk(window).OfType<LaunchPage>().Single();
        await Until(() => Field<NewsPanel>(launch, "_news").LoadedImages > 0, "home loads genuine news artwork");
        var top = (Panel)window.FindName("NavHost"); var bottom = (Panel)window.FindName("PinnedHost");
        var footer = Walk(bottom).OfType<ToggleButton>().Select(button => button.Tag as string).ToList();
        Check(footer.SequenceEqual(new[] { "account", "tasks", "settings" }), "account, downloads and settings occupy the footer");
        Check(Walk(top).OfType<ToggleButton>().First().Tag as string == "launch", "launch is the first navigation action");
        Check(NavModel.NavItemsFromConfig(window.NavConfig).All(entry => entry.Kind != NavEntryKind.Header), "stock navigation removes redundant group labels");
        var custom = new NavConfig { Groups = new() { new NavGroup { Title = "自定义工具", Keys = new() { "plugins", "servers" } } } };
        Check(NavModel.GroupedLayout(custom)[0].Keys.Take(2).SequenceEqual(new[] { "plugins", "servers" }), "user-defined navigation order remains intact");
        var legacy = new NavConfig { Groups = new() {
            new() { Title = "账户", Keys = new() { "account" } }, new() { Title = "游戏", Keys = new() { "launch", "instance", "download" } },
            new() { Title = "通用", Keys = new() { "settings", "multiplayer", "ai", "more", "tasks" } } } };
        Check(NavModel.NavItemsFromConfig(legacy).All(entry => entry.Kind != NavEntryKind.Header), "untouched legacy navigation migrates to the desktop structure");
        var more = Walk(top).OfType<ToggleButton>().First(button => button.Tag as string == "more");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); await Layout(window);
            var wash = (Rectangle)window.FindName("PageWash"); var brush = (SolidColorBrush)wash.Fill;
            Check(wash.Opacity == 1 && MaterialPreview.WallpaperContrast() >= 4.5, "wallpaper reading surface retains contrast in " + (dark ? "dark" : "light"));
            Check(Wallpaper.Active, "actual Wallpaper Engine project preview is loaded");
            foreach (int width in new[] { 1180, 900 })
            {
                window.Width = width; await Layout(window);
                Check(Walk(bottom).OfType<ToggleButton>().All(button => button.IsVisible && button.TranslatePoint(new Point(), window).Y + button.ActualHeight <= window.ActualHeight), "footer stays visible at " + width);
                Program.SaveSnapshot(window, Path.Combine(folder, "home-" + (dark ? "dark" : "light") + "-" + width + ".png"));
            }
        }
        more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(window); more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var selectors = new[] { "原版", "Fabric", "Forge" }; var combo = Ui.Combo(selectors, "原版");
        var note = Ui.Input("输入名称"); var content = Ui.V(12, Ui.Field("名称", note), Ui.Field("加载器", combo));
        var confirmation = Dlg.Ask("安装选项 · 界面验收", content, "保存"); await Layout(window);
        note.Focus(); Check(note.IsKeyboardFocused, "dialog accepts keyboard focus");
        combo.IsDropDownOpen = true; await Layout(window);
        Check(combo.IsDropDownOpen, "custom selector opens inside dialog");
        var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
        Check(popup.Child is Border frame && ((SolidColorBrush)frame.Background).Opacity == 1, "selector popup uses solid reading surface");
        Program.SaveSnapshot(window, Path.Combine(folder, "dialog.png"));
        combo.IsDropDownOpen = false; Click(Dlg.Host!, "取消"); Check(!await confirmation, "dialog cancellation preserves the expected result");
        await InstallChecks(window, folder);
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch"); window.Width = 1180; window.Height = 740; await Layout(window);
        DpiPreview.Capture(window, Path.Combine(folder, "dpi"));
        await SnapChecks(window, folder);
        Console.WriteLine("[DESKTOP WORKFLOW] " + _checks + " checks passed");
    }

    private static async Task InstallChecks(MainWindow window, string folder)
    {
        window.Navigate("version", instant: true); await Program.WaitForPage(window, "version");
        var page = Walk(window).OfType<VersionPage>().Single(); var list = Field<ListBox>(page, "_list");
        await Until(() => list.Items.Count > 0, "installer loads the real version manifest");
        Click(page, "最新正式版");
        Check(Field<TextBlock>(page, "_versionHint").Text.Contains("推荐首次游玩"), "latest stable version has clear recommendation");
        Check(Field<ComboBox>(page, "_loader").SelectedIndex == 0 && !Field<ComboBox>(page, "_loader").IsVisible, "first installation defaults to vanilla with optional loader settings collapsed");
        string selected = Field<TextBlock>(page, "_selectedVersion").Text;
        Invoke(page, "ApplyFilter"); Check(Field<TextBlock>(page, "_selectedVersion").Text == selected, "manifest filtering preserves a valid selected version");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (int width in new[] { 1180, 900 })
            {
                window.Width = width; await Layout(window);
                var install = Field<Button>(page, "_install"); var point = install.TranslatePoint(new Point(), page);
                Check(point.X >= 0 && point.X + install.ActualWidth <= page.ActualWidth + 1 && point.Y + install.ActualHeight < page.ActualHeight, "installer action fits first viewport at " + width);
                Program.SaveSnapshot(window, Path.Combine(folder, "install-" + (dark ? "dark" : "light") + "-" + width + ".png"));
            }
        }
        const string progressTask = "desktop-progress-fixture";
        TaskStore.Handle(new BridgeEvent { Event = "task_added", TaskId = progressTask, Title = "安装状态测试" });
        Invoke(page, "TrackInstall", progressTask, Field<ComboBox>(page, "_inst").Str(), "");
        TaskStore.Handle(new BridgeEvent { Event = "progress", TaskId = progressTask, Current = 46, Total = 100, Message = "正在校验资源" });
        Check(Field<ProgressBar>(page, "_installProgress").Value == 46 && Field<TextBlock>(page, "_installStatus").Text.Contains("46%"), "inline progress reflects task status and percent");
        Check(!list.IsEnabled && !Field<ComboBox>(page, "_inst").IsEnabled && !Field<Button>(page, "_install").IsVisible, "active install prevents duplicate starts and target changes");
        await Layout(window);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)list.ActualWidth, (int)list.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(list);
        byte[] corner = new byte[4]; bitmap.CopyPixels(new Int32Rect(1, 1, 1, 1), corner, 4, 0);
        Check(corner[3] == 0, "disabled dark list keeps a transparent surface instead of the Windows white fallback");
        Check(((Border)window.FindName("DockHost")).Visibility != Visibility.Visible, "installer does not repeat its own task in the floating dock");
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        Check(((Border)window.FindName("DockHost")).Visibility == Visibility.Visible, "other pages still show the active download dock");
        TaskStore.Handle(new BridgeEvent { Event = "progress", TaskId = progressTask, Current = 74, Total = 100, Message = "正在下载依赖" });
        window.Navigate("version", instant: true); await Program.WaitForPage(window, "version");
        Check(Field<ProgressBar>(page, "_installProgress").Value == 74, "returning to installer restores current task state");
        await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "install-progress.png"));
        TaskStore.Handle(new BridgeEvent { Event = "finished", TaskId = progressTask, Success = true, Message = "fixture complete" });
        Check(Field<Button>(page, "_openGame").IsVisible, "successful state exposes return-to-launch action");
        Check(!Field<Button>(page, "_install").IsVisible, "completed installation does not present a duplicate install action");

        var game = new Instance("desktop-retry-fixture", true); game.Create();
        string failed = await AppServices.Client.StartTaskAsync("repair_version", new { instance = game.name, version = "not-installed" });
        var outcome = await AppServices.Client.CallAsync<OpResult>("wait_task", new { task_id = failed, timeout = 15 });
        Check(outcome?.Ok == false, "isolated missing-version repair produces a real failed task");
        await Until(() => TaskStore.Get(failed)?.Finished == true, "failed task event arrives");
        Invoke(page, "TrackInstall", failed, game.name, "not-installed");
        Check(Field<Button>(page, "_retryInstall").IsVisible, "failure exposes inline retry");
        await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "install-failed.png"));
        Click(page, "重试安装");
        await Until(() => Field<string>(page, "_installTask") != failed, "retry starts a new backend task");
        string next = Field<string>(page, "_installTask");
        Check(DownloadRetries.Find(next)?.ArgumentsJson.Contains("not-installed") == true && DownloadRetries.Find(failed) == null, "retry preserves original target and retires old retry action");
        await AppServices.Client.CallAsync("wait_task", new { task_id = next, timeout = 15 });
        await Until(() => TaskStore.Get(next)?.Finished == true, "retried failure is reflected in the installer");
        Click(page, "选择其他版本");
        Check(Field<Button>(page, "_install").IsVisible && Field<SPanel>(page, "_installActivity").Visibility == Visibility.Collapsed, "choosing another version exits the finished task view");

        const string invalidVersion = "codex-invalid-install-version";
        object invalid = typeof(VersionPage).GetMethod("Map", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { new VersionRow { Version = invalidVersion, Type = "release" } })!;
        list.ItemsSource = new[] { invalid }; list.SelectedIndex = 0;
        var targets = Field<ComboBox>(page, "_inst"); targets.Items.Add(game.name); targets.SelectedItem = game.name;
        var starting = (Task)Invoke(page, "InstallAsync")!;
        var duplicate = (Task)Invoke(page, "InstallAsync")!;
        await Task.WhenAll(starting, duplicate);
        string started = Field<string>(page, "_installTask"); var request = DownloadRetries.Find(started);
        Check(request?.Method == "install_game" && request.ArgumentsJson.Contains(invalidVersion) && request.ArgumentsJson.Contains(game.name), "install action submits the selected version and instance through the real backend");
        await Until(() => TaskStore.Rows.Count(task => task.Title.Contains(invalidVersion)) > 0, "actual install task reaches task store");
        Check(TaskStore.Rows.Count(task => task.Title.Contains(invalidVersion)) == 1, "rapid repeated install invocation creates only one task");
        var invalidOutcome = await AppServices.Client.CallAsync<OpResult>("wait_task", new { task_id = started, timeout = 20 });
        Check(invalidOutcome?.Ok == false, "invalid version is rejected by the real installer without downloading a game");
        Console.WriteLine("[INSTALL EVIDENCE] Progress/success use explicit UI fixtures; failure/retry use actual isolated backend requests; no game installation or launch claimed");
    }

    private static async Task SnapChecks(MainWindow window, string folder)
    {
        var work = SystemParameters.WorkArea; double width = Math.Max(window.MinWidth, work.Width / 2);
        var before = new Rect(window.Left, window.Top, window.Width, window.Height);
        foreach (bool right in new[] { false, true })
        {
            window.Left = right ? work.Right - width : work.Left; window.Top = work.Top; window.Width = width; window.Height = work.Height;
            await Layout(window);
            var caption = (FrameworkElement)window.FindName("CaptionHost");
            Check(caption.TranslatePoint(new Point(), window).X + caption.ActualWidth <= window.ActualWidth, "caption remains reachable at " + (right ? "right" : "left") + " work-area edge");
            Program.SaveSnapshot(window, Path.Combine(folder, "edge-" + (right ? "right" : "left") + ".png"));
        }
        window.Left = before.Left; window.Top = before.Top; window.Width = before.Width; window.Height = before.Height;
        Console.WriteLine("[EDGE LAYOUT] Work-area placement checked; native Snap hit testing is covered by NativeShellChecks");
    }
}
