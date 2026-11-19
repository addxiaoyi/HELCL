using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

internal static class WorkspaceRefinementCheck
{
    private static int _checks;
    private static IEnumerable<T> Find<T>(DependencyObject root) => CustomizationPreview.Walk(root).OfType<T>();
    private static T Field<T>(object owner, string name) => MaterialControlsPreview.Field<T>(owner, name);
    private static object? Invoke(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    private static void Click(DependencyObject root, string name) => Find<ButtonBase>(root).First(button => AutomationProperties.GetName(button) == name && button.IsVisible).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); _checks++; Console.WriteLine("[PASS] " + label); }

    internal static async Task Seed()
    {
        foreach (string name in new[] { "山谷生存 · 验收实例", "红石实验室 · 验收实例", "好友联机 · 验收实例", "中文长名称与版本信息排版 · 验收实例" })
        {
            await AppServices.Client.CallAsync("create_instance", new { name });
            var game = new Instance(name); string version = Path.Combine(game.path, "versions", "1.21.1"); Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
            game.SetMeta("mc_version", "1.21.1");
            string screenshot = Path.Combine(Environment.CurrentDirectory, ".design", "resource-polish-2026-11-10", "resource-pack-enabled.png");
            if (File.Exists(screenshot)) { Directory.CreateDirectory(Path.Combine(game.path, "screenshots")); File.Copy(screenshot, Path.Combine(game.path, "screenshots", "游戏实测.png"), true); }
            await AppServices.Client.CallAsync("set_instance_group", new { instance = name, group = name.Contains("红石") ? "创作" : "生存联机" });
        }
        await AppServices.Client.CallAsync("invalidate_instances");
    }

    internal static async Task Run(MainWindow window, string folder, bool live)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        await AppServices.Client.CallAsync("save_settings", new { data = new { ui_glass_transparency = 38, ui_glass_cards = 52, ui_glass_controls = 24, ui_glass_popups = 0, ui_glass_frost = 30 } });
        await Wallpaper.ReloadAsync();
        await Seed(); window.Navigate("instance", instant: true); await Program.WaitForPage(window, "instance");
        var library = Find<InstancePage>(window).Single(); await library.RefreshAsync();
        var groups = Field<ComboBox>(library, "_groupFilter");
        groups.SelectedItem = groups.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == "创作");
        Check(Field<Panel>(library, "_list").Children.Count == 1, "custom group narrows the instance library");
        await library.RefreshAsync(); Check((groups.SelectedItem as ComboBoxItem)?.Tag as string == "创作", "group selection survives refresh");
        await (Task)Invoke(library, "EditGroup", "红石实验室 · 验收实例")!; await MaterialControlsPreview.Layout(window);
        var name = Find<ComboBox>(Dlg.Host!).Single(combo => AutomationProperties.GetName(combo) == "实例分组名称"); name.Text = "共享创作";
        Click(Dlg.Host!, "保存分组"); await MaterialControlsPreview.Until(() => Dlg.Host!.Children.Count == 0, "group editor saves and closes");
        Check(new Instance("红石实验室 · 验收实例").Meta().Get("group") as string == "共享创作", "group editor writes instance metadata");
        groups.SelectedIndex = 0;
        Invoke(library, "Select", ".minecraft");
        foreach (bool dark in new[] { false, true })
        foreach (int width in new[] { 1180, 800 })
        {
            App.ApplyTheme(dark); window.Width = width; await MaterialControlsPreview.Layout(window);
            foreach (bool covers in new[] { true, false })
            {
                Invoke(library, "SetLibraryView", covers); await MaterialControlsPreview.Layout(window);
                Find<ScrollViewer>(library).First().ScrollToHome(); await MaterialControlsPreview.Layout(window);
                Check(Field<Panel>(library, "_list").Children.Count == 5, "view switch preserves all instances");
                Check(groups.ActualWidth >= 100 && groups.IsVisible, "group filter remains usable at " + width);
                Program.SaveSnapshot(window, Path.Combine(folder, $"library-{dark}-{width}-{covers}.png"));
            }
        }
        window.Width = 1180; await MaterialControlsPreview.Layout(window);
        var toggle = Field<ToggleButton>(library, "_detailsToggle"); toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await MaterialControlsPreview.Layout(window);
        Check(Field<Border>(library, "_inspector").Visibility == Visibility.Collapsed && !window.Prefs.LibraryDetails, "details collapse expands the library and saves preference");
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); await MaterialControlsPreview.Layout(window);
        Check(Field<Border>(library, "_inspector").IsVisible, "details can be restored");
        window.Navigate("resourcepack", instant: true); await Program.WaitForPage(window, "resourcepack");
        var catalog = Find<CatalogPage>(window).Single(); catalog.OnHidden();
        catalog.GetType().GetField("_searchToken", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(catalog, 9999);
        var fixture = new List<CatalogItem> {
            new() { Name = "Fresh Animations", ChineseName = "生动动画", Slug = "fresh-animations", Author = "FreshLX", Downloads = 10, Updated = "2026-10-01", Source = "modrinth", Description = "验收夹具：改善原版生物动作", OriginalDescription = "Animation resource pack" },
            new() { Name = "Faithful", ChineseName = "忠实材质", Slug = "faithful", Author = "Faithful Team", Downloads = 90, Updated = "2026-11-01", Source = "modrinth", Description = "验收夹具：保留原版风格的高清资源包", OriginalDescription = "Faithful resource pack" }
        };
        Invoke(catalog, "ShowRows", fixture);
        var order = Field<ComboBox>(catalog, "_resultOrder"); order.SelectedIndex = 1; await MaterialControlsPreview.Layout(window);
        Check(((CatalogItem)Field<ListBox>(catalog, "_results").Items[0]).Name == "Faithful", "download sorting changes result order without a network query");
        order.SelectedIndex = 0;
        Check(((CatalogItem)Field<ListBox>(catalog, "_results").Items[0]).Name == "Fresh Animations", "relevance sorting restores original order");
        foreach (int width in new[] { 1180, 800 })
        foreach (bool dark in new[] { false, true })
        {
            window.Width = width; App.ApplyTheme(dark); await MaterialControlsPreview.Layout(window);
            Check(Field<Grid>(catalog, "_listHost").ActualHeight >= 240, "compact filters preserve result space at " + width);
            Check(order.ActualWidth >= 110, "result sort remains readable at " + width);
            Program.SaveSnapshot(window, Path.Combine(folder, $"catalog-{dark}-{width}.png"));
        }
        await Appearance(window, folder);
        Glass.SetMaterial(100, 0); await AppearanceDialog.Show(); await MaterialControlsPreview.Layout(window);
        UIElement parent = Dlg.Host!.Children[0];
        Click(Dlg.Host!, "保存当前方案…"); await MaterialControlsPreview.Layout(window);
        Check(parent.Opacity == 0, "fully transparent nested prompt hides the parent dialog text");
        Click(Dlg.Host!, "取消"); await MaterialControlsPreview.Layout(window);
        Check(parent.Opacity == 1 && ((UIElement)window.FindName("PageHost")).Opacity == 0, "closing nested prompt restores only its parent");
        Click(Dlg.Host!, "关闭"); await MaterialControlsPreview.Layout(window);
        Check(((UIElement)window.FindName("PageHost")).Opacity == 1, "closing the last transparent dialog restores the page");
        if (live)
        {
            window.Width = 1180; window.Navigate("instance", instant: true); await Program.WaitForPage(window, "instance");
            window.Title = "HelL · 工作区与透明菜单实测"; Glass.SetMaterial(100, 0);
            File.WriteAllText(Path.Combine(folder, "live-ready.txt"), "isolated window ready");
        }
        Console.WriteLine("[WORKSPACE REFINEMENT] " + _checks + " checks passed");
    }

    private static async Task Appearance(MainWindow window, string folder)
    {
        await AppServices.Client.CallAsync("apply_appearance_preset", new { preset = "wallpaper-glass" });
        await window.ReloadAppearanceAsync(); await AppearanceDialog.Show(); await MaterialControlsPreview.Layout(window);
        int cards = Glass.CardTransparency;
        Click(Dlg.Host!, "桌面直透"); await MaterialControlsPreview.Until(() => Backdrop.ClearDesktop, "preset selects desktop background");
        await MaterialControlsPreview.Until(() => Find<Button>(Dlg.Host!).Any(button => AutomationProperties.GetName(button) == "重启应用外观" && button.IsVisible && button.IsEnabled), "preset offers restart when composition differs");
        Click(Dlg.Host!, "撤销上次切换"); await MaterialControlsPreview.Until(() => !Backdrop.ClearDesktop && Glass.CardTransparency == cards, "preset undo restores background and material together");
        Click(Dlg.Host!, "保存当前方案…"); await MaterialControlsPreview.Layout(window);
        Find<TextBox>(Dlg.Host!).Last().Text = "我的通透外观"; Click(Dlg.Host!, "确定");
        await MaterialControlsPreview.Until(() => File.Exists(Path.Combine(Utils.ROOT, "themes", "我的通透外观.json")), "save appearance profile persists a theme file");
        var theme = (PyDict)Utils.ReadJson(Path.Combine(Utils.ROOT, "themes", "我的通透外观.json"));
        Check(theme.ContainsKey("ui_glass_cards") && theme.ContainsKey("ui_wallpaper_mode"), "saved appearance includes material and background mode");
        await MaterialControlsPreview.Layout(window); Program.SaveSnapshot(window, Path.Combine(folder, "appearance-profiles.png"));
        Click(Dlg.Host!, "关闭");
    }
}
