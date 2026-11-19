using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Plugins;
using HelL.Pages;
using HelL.Services;

internal static class AxolotlPreview
{
    private static int _checks;
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        const string version = "1.21.1";
        foreach (string name in new[] { "青山生存 · 测试实例", "红石实验室 · 测试实例", "建筑世界 · 长名称与中文排版测试" })
        {
            await AppServices.Client.CallAsync("create_instance", new { name });
            var instance = new Instance(name); string versions = Path.Combine(instance.path, "versions", version); Directory.CreateDirectory(versions);
            File.WriteAllText(Path.Combine(versions, version + ".json"), "{\"id\":\"1.21.1\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
            VersionSettings.Save(instance, version, PyDict.Of("isolation", "none"));
            string shots = Path.Combine(instance.path, "screenshots"); Directory.CreateDirectory(shots);
            for (int i = 0; i < 4; i++) WriteFixture(Path.Combine(shots, "fixture-" + i + ".png"), i);
            Playtime.RecordSession(name, version, 900 + name.Length);
        }
        await AppServices.Client.CallAsync("invalidate_instances");
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        var page = CustomizationPreview.Walk(window).OfType<LaunchPage>().Single();
        await page.RefreshAsync(); await Invoke(page, "LoadHomeLibrary");
        var dash = Field<DashHost>(page, "_dash");
        Check(CustomizationPreview.Walk(page).OfType<Button>().Count(button => System.Windows.Automation.AutomationProperties.GetName(button) == "继续游戏") == 3, "recent instance rows expose three continue actions");
        var records = await AppServices.Client.CallAsync<List<DashboardInstance>>("dashboard_instances", new { limit = 3 });
        Check(records?.Count == 3 && records.All(row => row.Cover.Length > 0 && row.Version == version && row.LastPlayed > 0), "home library resolves installed versions, session dates and actual local image paths");
        Check(Field<ComboBox>(page, "_ver").SelectedItem as string == version, "empty default instance yields to a playable recent instance");
        var launchTarget = records![1];
        typeof(LaunchPage).GetField("_resuming", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
        typeof(LaunchPage).GetField("_homeLaunchTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, launchTarget);
        var validateTarget = typeof(LaunchPage).GetMethod("ValidateResumeTarget", BindingFlags.Instance | BindingFlags.NonPublic)!;
        validateTarget.Invoke(page, new object[] { launchTarget.Instance, launchTarget.Version });
        bool rejected = false;
        try { validateTarget.Invoke(page, new object[] { "changed-instance", launchTarget.Version }); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { rejected = true; }
        Check(rejected, "each recent row validates its own launch target and rejects a changed selection");
        typeof(LaunchPage).GetField("_resuming", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, false);
        typeof(LaunchPage).GetField("_homeLaunchTarget", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, null);
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (int width in new[] { 1180, 900 })
            {
                window.Width = width; window.UpdateLayout(); await Task.Delay(300);
                Program.SaveSnapshot(window, Path.Combine(folder, $"home-{(dark ? "dark" : "light")}-{width}.png"));
                if (dash.IsVisible) Bounds(dash);
                else Check(Field<Button>(page, "_launchBtn").IsVisible && Field<Button>(page, "_launchBtn").TranslatePoint(new Point(), page).Y < 220, "desktop layout keeps the launch action near the top");
            }
        }
        window.Width = 1180; App.ApplyTheme(false); window.UpdateLayout();
        var noteSettings = System.Text.Json.JsonDocument.Parse("{\"text\":\"保留我的自定义内容\"}");
        dash.AddCard("notes"); var note = dash.Layout.Items.Last(); note.Settings = noteSettings.RootElement.Clone();
        string before = dash.Layout.ToJson(); dash.ApplyPreset(DashLayout.Preset("studio")); window.UpdateLayout(); await Task.Delay(300);
        Check(dash.Layout.Items.Single(card => card.Id == note.Id).Settings!.Value.GetProperty("text").GetString() == "保留我的自定义内容", "changing preset preserves custom module content and identity");
        Check(dash.Layout.Items.Any(card => card.Type == "screenshots") && dash.Layout.Items.Any(card => card.Type == "servers"), "creator preset contains screenshot and server modules");
        dash.Undo(); Check(dash.Layout.ToJson() == before, "preset can be undone without losing content");
        dash.ApplyPreset(DashLayout.Preset("studio")); window.UpdateLayout();
        var instancePicker = Field<ComboBox>(page, "_inst"); instancePicker.SelectedItem = "青山生存 · 测试实例";
        using var status = new StatusFixture();
        await AppServices.Client.CallAsync("add_server", new { instance = instancePicker.SelectedItem as string, name = "隔离状态测试服", ip = "127.0.0.1", port = status.Port });
        await Task.Delay(300);
        foreach (var module in CustomizationPreview.Walk(page).Where(child => child.GetType().Name == "DashboardLiveModule").ToList()) await Invoke(module, "Refresh");
        Check(CustomizationPreview.Walk(page).OfType<TextBlock>().Any(text => text.Text.Contains("7 人在线")), "server widget reads a real local Java status response");
        Check(CustomizationPreview.Walk(page).OfType<LocalPicture>().Count(picture => picture.Ready) >= 7, "instance and screenshot widgets decode their files");
        dash.ScrollHost!.ScrollToVerticalOffset(300); window.UpdateLayout(); await Task.Delay(200);
        Program.SaveSnapshot(window, Path.Combine(folder, "creator-modules.png"));
        var ordered = dash.Layout.Items.OrderBy(card => card.Y).ThenBy(card => card.X).ToList(); var target = ordered[1]; string orderBefore = dash.Layout.ToJson();
        dash.MoveInOrder(target, -1); Check(dash.Layout.Items.Single(card => card.Id == target.Id).Y <= ordered[0].Y, "module ordering changes placement"); dash.Undo(); Check(dash.Layout.ToJson() == orderBefore, "module reorder can be undone");
        dash.ScrollHost.ScrollToTop(); window.UpdateLayout();
        var scroll = dash.ScrollHost; Point bottom = scroll.TranslatePoint(new Point(20, scroll.ActualHeight - 4), dash);
        typeof(DashHost).GetMethod("ScrollWhileDragging", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dash, new object[] { bottom }); window.UpdateLayout();
        Check(scroll.VerticalOffset > 0, "dragging near the viewport edge scrolls the canvas");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_font_family"] = "Microsoft YaHei UI" } });
        await Typography.Load(); Check(Typography.Family == "Microsoft YaHei UI" && ((FontFamily)Application.Current.Resources["F.Ui"]).Source.StartsWith("Microsoft YaHei UI"), "selected system font round-trips through settings and applies immediately");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_font_family"] = "" } }); await Typography.Load();
        var builtin = PluginCatalog.Builtins()[0]; LauncherPlugins.Store.InstallBuiltin(builtin.Manifest);
        window.Navigate("plugins", instant: true); await Program.WaitForPage(window, "plugins");
        var market = CustomizationPreview.Walk(window).OfType<PluginsPage>().Single();
        var update = PluginCatalog.Builtins()[0]; update.Manifest.Version = "1.1.0";
        typeof(PluginsPage).GetField("_catalog", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(market, new List<PluginListing> { update });
        CustomizationPreview.Named(market, "可更新").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
        Check(CustomizationPreview.Walk(market).OfType<TextBlock>().Any(text => text.Text.Contains("1.0.0 → 1.1.0")), "installed plugins show available versions in updates view");
        CustomizationPreview.Named(market, "更新").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(200);
        Check(LauncherPlugins.Installed().Single(plugin => plugin.Manifest.Id == "desk-clock").Manifest.Version == "1.1.0", "update action replaces the installed plugin with the catalog version");
        Program.SaveSnapshot(window, Path.Combine(folder, "plugin-updates.png"));
        window.Navigate("launch", instant: true); await Task.Delay(200);
        await VerifyDynamicFallback();
        Console.WriteLine("[AXOLOTL UI] " + _checks + " checks passed");
    }
    private static async Task VerifyDynamicFallback()
    {
        string config = WallpaperEngine.DiscoverConfigs().First();
        var selection = WallpaperEngine.Read(config).First(paper => !paper.Direct);
        string before = File.ReadAllText(config);
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "engine", ["ui_wallpaper_engine_config"] = config, ["ui_wallpaper_monitor"] = selection.Monitor, ["ui_wallpaper_engine_live"] = true } });
        await Wallpaper.ReloadAsync();
        Check(Wallpaper.EngineLive || (Wallpaper.SyncStatus.Contains("动态") && Wallpaper.CurrentSource == selection.Preview), "dynamic wallpaper either provides a live frame or a labelled recoverable fallback");
        Console.WriteLine("[DYNAMIC WALLPAPER] " + Wallpaper.SyncStatus);
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "local", ["ui_wallpaper_engine_live"] = false } }); await Wallpaper.ReloadAsync();
        Check(!Wallpaper.EngineLive, "switching wallpaper mode releases capture resources");
        var oldSelected = WallpaperEngine.ParseSelections(before); var current = WallpaperEngine.Read(config);
        Check(oldSelected.All(paper => current.Any(other => other.Monitor == paper.Monitor && other.File.Replace('\\', '/') == paper.File.Replace('\\', '/'))), "dynamic test preserves the user's desktop wallpaper choices");
    }
    private static void Bounds(DashHost dash)
    {
        var rectangles = dash.Children.OfType<DashHost.CardView>().Select(view => new Rect(view.TranslatePoint(new Point(), dash), view.RenderSize)).ToList();
        Check(rectangles.All(rect => rect.Left >= 0 && rect.Right <= dash.ActualWidth + 1), "home modules fit the viewport");
        for (int i = 0; i < rectangles.Count; i++) for (int j = i + 1; j < rectangles.Count; j++) Check(!rectangles[i].IntersectsWith(rectangles[j]), "home module pair does not overlap");
    }
    private static Task Invoke(object owner, string name) => (Task)owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null)!;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    internal static void WriteFixture(string path, int index)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(68 + index * 18), (byte)(101 + index * 15), (byte)(94 + index * 10))), null, new Rect(0, 0, 600, 340));
            var text = new FormattedText("本地截图解码测试 " + (index + 1), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 26, Brushes.White, 1);
            drawing.DrawText(text, new Point(50, 145));
        }
        var bitmap = new RenderTargetBitmap(600, 340, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class StatusFixture : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public StatusFixture() { _listener.Start(); _ = Listen(); }
        private async Task Listen()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync(); using var stream = client.GetStream();
                    var request = new byte[1024]; await stream.ReadAsync(request, 0, request.Length);
                    byte[] json = Encoding.UTF8.GetBytes("{\"version\":{\"name\":\"1.21.1\",\"protocol\":767},\"players\":{\"online\":7,\"max\":20},\"description\":\"隔离状态测试服\"}");
                    using var body = new MemoryStream(); body.WriteByte(0); VarInt(body, json.Length); body.Write(json, 0, json.Length);
                    using var packet = new MemoryStream(); VarInt(packet, (int)body.Length); body.WriteTo(packet); byte[] bytes = packet.ToArray(); await stream.WriteAsync(bytes, 0, bytes.Length);
                }
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }
        private static void VarInt(Stream stream, int value) { do { byte part = (byte)(value & 127); value >>= 7; stream.WriteByte((byte)(part | (value > 0 ? 128 : 0))); } while (value > 0); }
        public void Dispose() => _listener.Stop();
    }
}
