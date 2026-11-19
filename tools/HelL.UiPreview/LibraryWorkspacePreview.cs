using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Plugins;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class LibraryWorkspacePreview
{
    private static int _checks;
    private const string First = "青山生存 · 测试实例";
    private const string LongName = "建筑世界 · 长名称与中文排版测试";
    private static IEnumerable<DependencyObject> Walk(DependencyObject root) => CustomizationPreview.Walk(root);
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static object? Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);
    private static void Click(DependencyObject root, string name) => CustomizationPreview.Named(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static async Task Until(Func<bool> ready, string message, int milliseconds = 10000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(100);
        Check(ready(), message);
    }
    private static async Task Layout(MainWindow window) { window.UpdateLayout(); await Task.Delay(200); window.UpdateLayout(); }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        foreach (string name in new[] { First, "红石实验室 · 测试实例", LongName })
        {
            await AppServices.Client.CallAsync("create_instance", new { name });
            var game = new Instance(name); string version = Path.Combine(game.path, "versions", "1.21.1"); Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
            VersionSettings.Save(game, "1.21.1", PyDict.Of("isolation", "none"));
            string shots = Path.Combine(game.path, "screenshots"); Directory.CreateDirectory(shots);
            AxolotlPreview.WriteFixture(Path.Combine(shots, "fixture.png"), name == First ? 0 : 1);
            string save = Path.Combine(game.path, "saves", "测试山谷"); Directory.CreateDirectory(save);
            WriteLevel(Path.Combine(save, "level.dat"), "山谷小屋 · 测试世界");
            File.Copy(Path.Combine(shots, "fixture.png"), Path.Combine(save, "icon.png"));
            Playtime.RecordSession(name, "1.21.1", 1500);
        }
        await AppServices.Client.CallAsync("invalidate_instances");
        window.Navigate("instance", instant: true); await Program.WaitForPage(window, "instance");
        var instances = Walk(window).OfType<InstancePage>().Single(); await instances.RefreshAsync();
        await Until(() => Walk(instances).OfType<LocalPicture>().Count(p => p.Ready) >= 3, "instance rows decode actual fixture files");
        Click(instances, "固定 " + First);
        await Until(() => Field<HashSet<string>>(instances, "_pins").Contains(First), "pin button persists state");
        var options = await AppServices.Client.CallAsync<HomeOptions>("home_options"); Check(options!.PinnedInstances.Contains(First), "pin survives a fresh backend read");
        const string renamed = "青山生存 · 改名测试";
        await AppServices.Client.CallAsync("rename_instance", new { name = First, new_name = renamed });
        options = await AppServices.Client.CallAsync<HomeOptions>("home_options");
        Check(options!.PinnedInstances.Contains(renamed) && !options.PinnedInstances.Contains(First), "renaming an instance preserves its pin");
        await AppServices.Client.CallAsync("rename_instance", new { name = renamed, new_name = First });
        await instances.RefreshAsync();
        var pinnedOnly = Field<CheckBox>(instances, "_pinnedOnly"); pinnedOnly.IsChecked = true; pinnedOnly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Field<Panel>(instances, "_list").Children.Count == 1, "pinned filter shows exactly the fixed instance");
        Check(Field<TextBlock>(instances, "_count").Text == "1 / 4 个", "pinned filter reports visible count and total");
        pinnedOnly.IsChecked = false; pinnedOnly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Invoke(instances, "Select", LongName);
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (int width in new[] { 1180, 900 })
            {
                window.Width = width; await Layout(window);
                Check(Field<Border>(instances, "_inspector").IsVisible == (width == 1180), "instance inspector adapts at " + width);
                Bounds(instances, "instance " + width);
                Program.SaveSnapshot(window, Path.Combine(folder, $"instances-{(dark ? "dark" : "light")}-{width}.png"));
            }
        }
        Invoke(instances, "ShowInstanceDetails", LongName); await Layout(window);
        Check(Walk(Dlg.Host!).OfType<TextBlock>().Any(t => t.Text == LongName), "narrow layout opens the selected instance details");
        Program.SaveSnapshot(window, Path.Combine(folder, "instance-details-narrow.png")); Click(Dlg.Host!, "关闭");
        Field<TextBox>(instances, "_search").Text = "没有这个测试实例";
        await Until(() => Field<Panel>(instances, "_list").Visibility == Visibility.Collapsed, "search empty state is visible");
        Field<TextBox>(instances, "_search").Text = "";

        await AppServices.Client.CallAsync("save_home_sidebar", new { enabled = true, modules = new[] { "clock", "worlds", "tasks", "playtime" } });
        window.Width = 1500; window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        var home = Walk(window).OfType<LaunchPage>().Single(); await home.RefreshAsync(); await Layout(window);
        await Until(() => Walk(Field<Border>(home, "_infoRail")).OfType<Button>().Any(b => AutomationProperties.GetName(b).StartsWith("管理世界 ")), "recent-world sidebar loads NBT metadata");
        Check(Field<Border>(home, "_infoRail").IsVisible, "wide home displays configured sidebar");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); await Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "home-sidebar-" + (dark ? "dark" : "light") + ".png"));
        }
        Click(Field<Border>(home, "_infoRail"), "管理世界 山谷小屋 · 测试世界");
        await Until(() => Dlg.Host!.Visibility == Visibility.Visible && Walk(Dlg.Host).OfType<TextBlock>().Any(t => t.Text == "测试山谷"), "world entry opens the matching saves folder");
        Click(Dlg.Host!, "关闭");
        window.Width = 900; await Layout(window); Check(!Field<Border>(home, "_infoRail").IsVisible, "narrow home collapses the rail");
        Invoke(home, "OpenInfoRail"); await Layout(window); Check(Walk(Dlg.Host!).OfType<HomeInfoPanel>().Any(), "narrow home retains sidebar access in a dialog");
        Click(Dlg.Host!, "关闭");
        Task configuring = (Task)Invoke(home, "ConfigureInfoRail")!; await Layout(window);
        foreach (var check in Walk(Dlg.Host!).OfType<CheckBox>()) check.IsChecked = false;
        Click(Dlg.Host!, "保存"); await configuring;
        options = await AppServices.Client.CallAsync<HomeOptions>("home_options"); Check(!options!.SidebarEnabled && options.SidebarModules.Count == 0, "sidebar dialog saves disabled and empty options");

        window.Navigate("mod", instant: true); await Program.WaitForPage(window, "mod");
        var catalog = Walk(window).OfType<CatalogPage>().Single();
        Field<ComboBox>(catalog, "_source").SelectedItem = "Modrinth";
        Field<TextBox>(catalog, "_search").Text = "sodium";
        await Task.Delay(400); await (Task)Invoke(catalog, "SearchAsync")!;
        Check(Field<ListBox>(catalog, "_results").Items.Count > 0, "resource workspace loads live Modrinth search results");
        await Until(() => Walk(catalog).OfType<ProjectCover>().Any(p => p.Ready), "resource workspace decodes real upstream icons", 15000);
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (int width in new[] { 1180, 900 })
            {
                window.Width = width; await Layout(window); var filters = Walk(catalog).OfType<Expander>().Single();
                Check(!filters.IsExpanded, "installation target stays collapsed until requested at " + width);
                Check(Field<ComboBox>(catalog, "_source").IsVisible && Field<ComboBox>(catalog, "_version").IsVisible, "common resource filters remain directly accessible at " + width);
                if (filters.IsExpanded)
                {
                    var filterContent = (FrameworkElement)filters.Content;
                    Check(Walk(filterContent).OfType<ComboBox>().All(combo => combo.TranslatePoint(new Point(), filterContent).X + combo.ActualWidth <= filterContent.ActualWidth + .5), "filter controls fit without clipping their dropdown arrows");
                }
                Bounds(catalog, "catalog " + width);
                Program.SaveSnapshot(window, Path.Combine(folder, $"catalog-{(dark ? "dark" : "light")}-{width}.png"));
            }
        }
        var collapsed = Walk(catalog).OfType<Expander>().Single(); collapsed.IsExpanded = true; await Layout(window);
        Check(Field<ComboBox>(catalog, "_inst").IsVisible, "narrow resource filters expose installation target");
        Program.SaveSnapshot(window, Path.Combine(folder, "catalog-narrow-filters.png"));
        Console.WriteLine("[LIBRARY WORKSPACE] " + _checks + " checks passed");
    }

    internal static async Task Plugins(MainWindow window, string folder, string source)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Plugin source folder is required.");
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        foreach (string manifestPath in Directory.GetFiles(source, "plugin.json", SearchOption.AllDirectories))
        {
            var manifest = PluginManifest.Parse(File.ReadAllText(manifestPath));
            string package = Path.Combine(folder, manifest.Id + ".hellplugin"); if (File.Exists(package)) File.Delete(package);
            ZipFile.CreateFromDirectory(Path.GetDirectoryName(manifestPath)!, package);
            LauncherPlugins.Store.InstallPackage(package);
        }
        LauncherPlugins.NotifyChanged(); window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        var launch = Walk(window).OfType<LaunchPage>().Single(); var dash = Field<DashHost>(launch, "_dash");
        foreach (var plugin in LauncherPlugins.Installed())
        {
            foreach (var module in plugin.Manifest.Modules)
            {
            dash.Load(new DashLayout { Items = new List<DashCard> { new() { Id = "shot-" + module.Id, Type = "plugin:" + plugin.Manifest.Id + ":" + module.Id, X = 0, Y = 0, W = .65, H = .35 } } });
            dash.ScrollHost?.ScrollToTop();
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark); await Layout(window);
                foreach (var card in dash.Children.OfType<DashHost.CardView>())
                {
                    card.BringIntoView(); await Layout(window);
                    string name = plugin.Manifest.Id + "-" + card.Card.Type.Split(':')[2] + "-" + (dark ? "dark" : "light") + ".png";
                    Program.SaveSnapshot(card, Path.Combine(folder, name), 2);
                    var captured = new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(folder, name)));
                    byte[] pixels = new byte[captured.PixelWidth * captured.PixelHeight * 4]; captured.CopyPixels(pixels, captured.PixelWidth * 4, 0);
                    Check(Enumerable.Range(0, captured.PixelWidth * captured.PixelHeight).Count(index => pixels[index * 4 + 3] > 0) > captured.PixelWidth * captured.PixelHeight / 2, "screenshot contains visible rendered pixels " + name);
                    Check(card.ActualWidth > 300 && card.ActualHeight > 100, "rendered real plugin module " + name);
                    var note = Walk(card).OfType<TextBox>().FirstOrDefault();
                    if (note != null) Check(note.ActualHeight >= card.ActualHeight - 65, "plugin note editor fills available module height");
                }
            }
            }
        }
        Console.WriteLine("[PLUGIN SCREENSHOTS] " + _checks + " checks passed");
    }

    private static void Bounds(FrameworkElement root, string context)
    {
        var controls = Walk(root).OfType<Button>().Where(b => b.IsVisible && b.ActualWidth > 0).ToList();
        Check(controls.All(b => { var point = b.TranslatePoint(new Point(), root); return point.X >= -.5 && point.X + b.ActualWidth <= root.ActualWidth + .5; }), context + " buttons fit horizontally");
    }
    private static void WriteLevel(string path, string name)
    {
        using var file = File.Create(path); using var gzip = new GZipStream(file, CompressionMode.Compress); using var writer = new BinaryWriter(gzip);
        void Text(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write((byte)(bytes.Length >> 8)); writer.Write((byte)bytes.Length); writer.Write(bytes); }
        writer.Write((byte)10); Text(""); writer.Write((byte)10); Text("Data");
        writer.Write((byte)8); Text("LevelName"); Text(name);
        writer.Write((byte)4); Text("LastPlayed"); byte[] time = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60000); if (BitConverter.IsLittleEndian) Array.Reverse(time); writer.Write(time);
        writer.Write((byte)0); writer.Write((byte)0);
    }
}
