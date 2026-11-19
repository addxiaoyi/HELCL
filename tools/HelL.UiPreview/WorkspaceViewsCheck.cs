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
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class WorkspaceViewsCheck
{
    private static int _checks;
    private static T Field<T>(object owner, string name) => MaterialControlsPreview.Field<T>(owner, name);
    private static object? Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static IEnumerable<T> Find<T>(DependencyObject root) => CustomizationPreview.Walk(root).OfType<T>();
    private static void Click(DependencyObject root, string name) => Find<ButtonBase>(root).First(button => AutomationProperties.GetName(button) == name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); _checks++; Console.WriteLine("[PASS] " + label); }

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        await Navigation(window, folder);
        await Library(window, folder);
        await Catalog(window, folder);
        Console.WriteLine("[WORKSPACE VIEWS] " + _checks + " checks passed");
    }

    private static async Task Navigation(MainWindow window, string folder)
    {
        if (window.Prefs.SideCollapsed) Click(window, "展开导航");
        var patch = NavModel.ReorderNavConfig(window.NavConfig, "instance", "launch", true)!;
        patch["ui_sidebar_width"] = 224;
        await (Task)Invoke(window, "ApplyNavPatchAsync", patch)!;
        await window.LoadNavFromBridgeAsync();
        string before = JsonSerializer.Serialize(window.NavConfig);
        string[] order = NavModel.SidebarSequence(window.NavConfig).ToArray();
        var expandedGroups = new Dictionary<string, bool>(window.Prefs.Expanded);
        Click(window, "收起导航"); await MaterialControlsPreview.Layout(window);
        Check(window.Prefs.SideCollapsed && ((ColumnDefinition)window.FindName("SideCol")).Width.Value == 68, "navigation collapses into a 68 DIP icon rail");
        Check(JsonSerializer.Serialize(window.NavConfig) == before, "collapsing preserves custom order, groups, pins and hidden entries");
        Check(NavModel.SidebarSequence(window.NavConfig).SequenceEqual(order), "compact navigation retains the original sequence");
        var saved = JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(Path.Combine(Utils.ROOT, "wpf-ui.json")))!;
        Check(saved.SideCollapsed && saved.SideWidth == 224, "compact preference persists without replacing the expanded width");
        var top = (Panel)window.FindName("NavHost");
        foreach (var button in Find<ToggleButton>(top))
        {
            Check(button.ActualWidth >= 44 && button.ActualHeight >= 40, "icon navigation retains a usable click target");
            Check(AutomationProperties.GetName(button).Length > 0 && button.ToolTip != null, "icon navigation keeps its accessible label and tooltip");
        }
        var download = Find<ToggleButton>(top).Single(button => button.Tag as string == "download");
        download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await MaterialControlsPreview.Layout(window);
        var menu = Field<ContextMenu>(window, "_navigationMenu");
        Check(menu.IsOpen && menu.Placement == PlacementMode.Right, "compact download group opens beside the rail");
        var members = NavModel.SectionMembersFromConfig(window.NavConfig)["download"];
        Check(menu.Items.Cast<MenuItem>().Select(item => item.Header.ToString()).SequenceEqual(members.Select(NavModel.NavLabel)), "group flyout follows customized section membership and order");
        var resource = menu.Items.Cast<MenuItem>().Single(item => item.Header.ToString() == NavModel.NavLabel("resource"));
        resource.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
        await Program.WaitForPage(window, "resourcepack");
        Check(download.IsChecked == true, "active child selects the parent icon");
        Program.SaveSnapshot(window, Path.Combine(folder, "compact-navigation.png"));
        var reorder = NavModel.ReorderNavConfig(window.NavConfig, "ai", "launch", true)!;
        await (Task)Invoke(window, "ApplyNavPatchAsync", reorder)!;
        var changedOrder = NavModel.SidebarSequence(window.NavConfig).ToArray();
        Click(window, "展开导航"); await MaterialControlsPreview.Layout(window);
        Check(((ColumnDefinition)window.FindName("SideCol")).Width.Value == 224, "expanding restores the saved width");
        Check(NavModel.SidebarSequence(window.NavConfig).SequenceEqual(changedOrder), "reordering while compact survives expansion");
        Check(expandedGroups.All(pair => window.Prefs.Expanded.TryGetValue(pair.Key, out bool expanded) && expanded == pair.Value), "flyout navigation does not overwrite expanded group preferences");
        await window.LoadNavFromBridgeAsync();
        Check(NavModel.SidebarSequence(window.NavConfig).SequenceEqual(changedOrder), "custom order survives a backend reload");
        await (Task)Invoke(window, "ApplyNavPatchAsync", NavModel.DefaultNavPatch())!;
    }

    private static async Task Library(MainWindow window, string folder)
    {
        string[] names = { "封面验证 · 建筑世界", "封面验证 · 生存世界", "封面验证 · 无截图", "封面验证 · 中文长名称与版本信息排版" };
        string realScreenshot = Path.Combine(Environment.CurrentDirectory, ".design", "resource-polish-2026-11-10", "resource-pack-enabled.png");
        if (!File.Exists(realScreenshot)) throw new FileNotFoundException("The recorded Minecraft screenshot is required for artwork verification.", realScreenshot);
        foreach (string name in names)
        {
            await AppServices.Client.CallAsync("create_instance", new { name });
            var instance = new Instance(name); string version = Path.Combine(instance.path, "versions", "1.21.1"); Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
            VersionSettings.Save(instance, "1.21.1", PyDict.Of("isolation", "none"));
            if (name != names[2])
            {
                string screenshots = Path.Combine(instance.path, "screenshots"); Directory.CreateDirectory(screenshots);
                File.Copy(realScreenshot, Path.Combine(screenshots, "minecraft-validation.png"));
            }
        }
        await AppServices.Client.CallAsync("invalidate_instances");
        var custom = await AppServices.Client.CallAsync<InstanceVisual>("set_instance_cover", new { instance = names[0], path = realScreenshot });
        Check(custom?.Kind == "custom" && File.Exists(custom.Path), "manual cover is stored in the isolated instance");
        window.Navigate("instance", instant: true); await Program.WaitForPage(window, "instance");
        var page = Find<InstancePage>(window).Single(); await page.RefreshAsync();
        await MaterialControlsPreview.Until(() => Find<LocalPicture>(page).Count(picture => picture.Ready) >= 3, "instance covers decode the recorded Minecraft screenshot");
        Invoke(page, "Select", names[1]);
        var search = Field<TextBox>(page, "_search"); search.Text = "封面验证";
        await MaterialControlsPreview.Until(() => Field<Panel>(page, "_list").Children.Count == 4, "library search filters the same instance set");
        var sort = Field<ComboBox>(page, "_sort"); sort.SelectedIndex = 1;
        Click(page, "紧凑列表"); await MaterialControlsPreview.Layout(window);
        Check(Field<string>(page, "_selected") == names[1] && search.Text == "封面验证" && sort.SelectedIndex == 1, "switching to list preserves selection, search and sort");
        var saved = JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(Path.Combine(Utils.ROOT, "wpf-ui.json")))!;
        Check(saved.LibraryView == "list", "library view persists across reopening");
        var reopened = new InstancePage();
        Check(Field<ToggleButton>(reopened, "_listButton").IsChecked == true, "a new library page restores list mode");
        Check((await AppServices.Client.CallAsync<InstanceVisual>("instance_visual", new { instance = names[0] }))?.Kind == "custom", "switching views retains the manually chosen cover");
        foreach (bool dark in new[] { false, true })
        foreach (int width in new[] { 1180, 800 })
        foreach (bool covers in new[] { true, false })
        {
            App.ApplyTheme(dark); window.Width = width;
            Click(page, covers ? "封面视图" : "紧凑列表"); await MaterialControlsPreview.Layout(window);
            Check(Field<string>(page, "_selected") == names[1], "view and size changes retain the selected instance");
            var list = Field<Panel>(page, "_list");
            Check(list.Children.Count == 4, "both views keep all filtered instances");
            foreach (FrameworkElement tile in list.Children)
            {
                Point point = tile.TranslatePoint(new Point(), list);
                Check(point.X >= 0 && point.X + tile.ActualWidth <= list.ActualWidth + 1, "instance presentation fits the available width");
                Check(!Find<Button>(tile).Any(button => button.ActualWidth > tile.ActualWidth), "instance actions remain inside the tile");
            }
            Check(covers ? list.Children[0].RenderSize.Height > 180 : list.Children[0].RenderSize.Height < 110, "cover and compact views have distinct useful densities");
            await MaterialControlsPreview.Until(() => Find<LocalPicture>(list).Count(picture => picture.Ready) == 3, "cover switching retains three real pictures and one missing-picture state");
            Program.SaveSnapshot(window, Path.Combine(folder, $"instances-{covers}-{dark}-{width}.png"));
        }
        Click(page, "固定 " + names[1]);
        await MaterialControlsPreview.Until(() => Field<HashSet<string>>(page, "_pins").Contains(names[1]), "pinning works in the compact view");
        var pinned = Field<CheckBox>(page, "_pinnedOnly"); pinned.IsChecked = true; pinned.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Click(page, "封面视图");
        Check(Field<Panel>(page, "_list").Children.Count == 1 && Field<string>(page, "_selected") == names[1], "cover view preserves pinned-only filtering");
        search.Text = "没有这个实例"; await MaterialControlsPreview.Until(() => Field<Panel>(page, "_list").Visibility == Visibility.Collapsed, "both views retain the empty state");
    }

    private static async Task Catalog(MainWindow window, string folder)
    {
        window.Width = 1180; window.Navigate("resourcepack", instant: true); await Program.WaitForPage(window, "resourcepack");
        var page = Find<CatalogPage>(window).Single();
        var source = Field<ComboBox>(page, "_source"); var version = Field<ComboBox>(page, "_version");
        var targets = Find<Expander>(page).Single(); var results = Field<Grid>(page, "_listHost");
        foreach (int width in new[] { 1180, 800 })
        {
            window.Width = width; await MaterialControlsPreview.Layout(window);
            Check(source.IsVisible && version.IsVisible, "source and game version remain in the top toolbar");
            Check(!targets.IsExpanded, "installation details stay collapsed until requested");
            var disclosure = (ToggleButton)targets.Template.FindName("HeaderSite", targets);
            Check(AutomationProperties.GetName(disclosure) == I18n.L("安装位置"), "installation disclosure exposes a meaningful keyboard and accessibility label");
            Check(results.ActualWidth > page.ActualWidth - 60, "catalog results use the full page width");
            targets.IsExpanded = true; await MaterialControlsPreview.Layout(window);
            Check(Field<ComboBox>(page, "_inst").IsVisible, "installation target is available before installing");
            Check(results.ActualHeight >= 140, "expanded installation controls preserve a usable result area");
            targets.IsExpanded = false;
            Program.SaveSnapshot(window, Path.Combine(folder, "catalog-top-filters-" + width + ".png"));
        }
    }
}
