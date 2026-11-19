using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

internal static class RichDialogAudit
{
    private const string InstanceName = "界面验收 · 中文长名称实例";
    private const string World = "青山生存世界_含中文与很长的建筑项目名称_2026";
    internal static async Task Run(MainWindow window, string folder)
    {
        await AppServices.Client.CallAsync("create_instance", new { name = InstanceName });
        var game = new Instance(InstanceName);
        string version = "1.21.1", versions = Path.Combine(game.path, "versions", version);
        Directory.CreateDirectory(versions);
        File.WriteAllText(Path.Combine(versions, version + ".json"), "{\"id\":\"1.21.1\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
        VersionSettings.Save(game, version, PyDict.Of("isolation", "none"));
        string world = Path.Combine(game.path, "saves", World); Directory.CreateDirectory(world);
        typeof(LibraryWorkspacePreview).GetMethod("WriteLevel", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { Path.Combine(world, "level.dat"), World });
        await AppServices.Client.CallAsync("backup_save", new { instance = InstanceName, name = World });
        Console.WriteLine("[FIXTURE] Isolated instance and NBT metadata, not a user's real world.");
        var project = await AppServices.Client.CallAsync<CatalogItem>("catalog_details", new { source = "modrinth", slug = "iris" }) ?? throw new IOException("No real project details");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 800; window.Height = 620;
            var details = new ProjectDetailsPanel(project, version);
            var layer = Dlg.Panel(project.Name, details, 760);
            await details.EnsureLoadedAsync(); await MaterialControlsPreview.Layout(window);
            await MaterialControlsPreview.Until(()=>RichUiAudit.Find<ProjectCover>(layer.Card).Any(cover=>cover.Ready), "resource gallery loads real upstream image", 25);
            await Capture(window, folder, "resource-detail-" + dark);
            foreach (var scroll in RichUiAudit.Find<SmoothScroll>(layer.Card)) scroll.ScrollToBottom();
            await MaterialControlsPreview.Layout(window);
            await Capture(window, folder, "resource-compatibility-" + dark);
            layer.Dismiss(); await MaterialControlsPreview.Layout(window);

            window.Navigate("mod", instant: true); await Program.WaitForPage(window, "mod");
            var catalog = RichUiAudit.Find<CatalogPage>(window).Single();
            var extra = new Dictionary<string, object?> { ["source"] = "modrinth", ["slug"] = "iris", ["mod_id"] = project.IdValue, ["kind"] = "mod" };
            var pickTask = (Task)RichUiAudit.Invoke(catalog, "PickVersionFileAsync", project, extra)!;
            await MaterialControlsPreview.Until(() => RichUiAudit.Find<Button>(Dlg.Host!).Any(button => button.Content?.ToString() == "安装所选"), "real install file chooser opens", 25);
            await Capture(window, folder, "install-files-" + dark);
            CustomizationPreview.Named(Dlg.Host!, "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pickTask;

            var setup = VersionSetupDialog.ShowAsync(InstanceName, version);
            await MaterialControlsPreview.Until(() => RichUiAudit.Find<TextBlock>(Dlg.Host!).Any(text => text.Text.StartsWith("版本设置")), "version settings opens");
            await Capture(window, folder, "version-settings-" + dark);
            RichUiAudit.Check(RichUiAudit.Find<SmoothScroll>(Dlg.Host!).Count(scroll=>scroll.IsVisible&&scroll.ScrollableHeight>0)==1, "version settings has one scroll region");
            foreach (var scroll in RichUiAudit.Find<SmoothScroll>(Dlg.Host!)) scroll.ScrollToBottom();
            await MaterialControlsPreview.Layout(window);
            await Capture(window, folder, "version-settings-bottom-" + dark);
            Escape(window); await setup;

            await SavesDialog.ShowAsync(InstanceName, "");
            await Capture(window, folder, "saves-" + dark);
            var restore = CustomizationPreview.Named(Dlg.Host!, "还原"); restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await MaterialControlsPreview.Layout(window);
            await Capture(window, folder, "restore-confirm-" + dark);
            CustomizationPreview.Named(Top(), "取消").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await MaterialControlsPreview.Layout(window);
            Escape(window); await MaterialControlsPreview.Layout(window);
        }
    }

    private static DependencyObject Top() => Dlg.Host!.Children[Dlg.Host.Children.Count - 1];
    private static void Escape(MainWindow window) => ((UIElement)Top()).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static async Task Capture(MainWindow window, string folder, string name)
    {
        await MaterialControlsPreview.Layout(window);
        RichUiAudit.Shot(window, folder, name);
        var root = (FrameworkElement)Top();
        foreach (var button in RichUiAudit.Find<Button>(root).Where(button => button.IsVisible))
        {
            Point origin = button.TranslatePoint(new Point(), root);
            RichUiAudit.Check(origin.X >= -1 && origin.X + button.ActualWidth <= root.ActualWidth + 1, "dialog button fits · " + name + " · " + System.Windows.Automation.AutomationProperties.GetName(button));
        }
    }
}
