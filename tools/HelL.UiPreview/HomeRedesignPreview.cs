using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class HomeRedesignPreview
{
    private static int _checks;
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static async Task Layout(MainWindow window) { window.UpdateLayout(); await Task.Delay(250); window.UpdateLayout(); }
    private static void Click(DependencyObject root, string name) => CustomizationPreview.Named(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        var page = CustomizationPreview.Walk(window).OfType<LaunchPage>().Single();
        var dash = Field<DashHost>(page, "_dash");
        var until = DateTime.UtcNow.AddSeconds(25);
        while (Field<NewsPanel>(page, "_news").LoadedImages == 0 && DateTime.UtcNow < until) await Task.Delay(100);
        Check(Field<NewsPanel>(page, "_news").LoadedImages > 0, "default home loads an actual Minecraft news image");
        await Capture("empty");
        Check(!Field<ComboBox>(page, "_inst").IsVisible, "empty library hides unusable launch selectors");
        Check(!CustomizationPreview.Walk(page).OfType<TextBlock>().Any(text => text.Text.Contains("继续你的冒险")), "empty library has no contradictory continue heading");
        Check(Field<Button>(page, "_installBtn").IsEnabled && Field<Button>(page, "_installBtn").IsVisible, "empty library exposes enabled Minecraft installation");
        Click(page, "安装 Minecraft"); await Program.WaitForPage(window, "version");
        Check(CustomizationPreview.Walk(window).OfType<VersionPage>().Any(), "installation action opens the real version installer");
        window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        string saved = dash.Layout.ToJson();
        Click(page, "编辑布局"); await Layout(window);
        Check(dash.IsVisible && dash.EditMode && dash.Children.OfType<DashHost.CardView>().All(card => card.IsVisible), "editing opens all original canvas modules");
        Click(page, "完成编辑"); await Layout(window);
        Check(!dash.IsVisible && saved == dash.Layout.ToJson(), "leaving unmodified edit mode restores desktop layout without changing coordinates");
        dash.ApplyPreset(DashLayout.Preset("focus")); await Layout(window);
        Check(dash.IsVisible, "custom preset keeps the editable canvas");
        dash.Undo(); await Layout(window); Check(!dash.IsVisible && saved == dash.Layout.ToJson(), "undoing a preset restores the default desktop layout");

        foreach (string name in new[] { "青山生存 · 测试实例", "建筑世界 · 中文长名称布局测试" })
        {
            await AppServices.Client.CallAsync("create_instance", new { name });
            var game = new Instance(name); string version = Path.Combine(game.path, "versions", "1.21.1"); Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\",\"type\":\"release\",\"mainClass\":\"net.minecraft.client.main.Main\"}");
            VersionSettings.Save(game, "1.21.1", PyDict.Of("isolation", "none"));
            string shots = Path.Combine(game.path, "screenshots"); Directory.CreateDirectory(shots);
            AxolotlPreview.WriteFixture(Path.Combine(shots, "fixture.png"), 1); Playtime.RecordSession(name, "1.21.1", 1800);
        }
        await AppServices.Client.CallAsync("invalidate_instances"); await page.RefreshAsync(); await Layout(window);
        Check(Field<ComboBox>(page, "_inst").IsVisible && Field<ComboBox>(page, "_ver").SelectedItem as string == "1.21.1", "installed library restores the exact launch controls and version");
        Check(Field<Button>(page, "_launchBtn").IsVisible && !Field<Button>(page, "_installBtn").IsVisible, "installed library exposes launch instead of first-install action");
        await Capture("installed");
        Click(page, "高级启动设置"); await Layout(window);
        Check(Field<ComboBox>(page, "_java").IsVisible, "advanced Java controls remain accessible");
        Click(page, "收起高级设置");
        Click(page, "编辑布局"); dash.AddCard("notes"); await Layout(window); Click(page, "完成编辑"); await Layout(window);
        Check(dash.IsVisible && CustomizationPreview.Walk(page).OfType<TextBox>().Any(box => box.Tag as string == "记录坐标、计划或待办…"), "adding a module keeps the custom layout and its editable content");
        Console.WriteLine("[HOME REDESIGN] " + _checks + " checks passed");

        async Task Capture(string state)
        {
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark);
                foreach (int width in new[] { 1180, 900, 800 })
                {
                    window.Width = width; window.Height = 740; await Layout(window);
                    var primary = Field<Button>(page, state == "empty" ? "_installBtn" : "_launchBtn");
                    Point point = primary.TranslatePoint(new Point(), page);
                    if (point.Y >= 220 || point.X < 0 || point.X + primary.ActualWidth > page.ActualWidth)
                    {
                        Program.SaveSnapshot(window, Path.Combine(folder, state + "-action-overflow-" + width + ".png"));
                        Console.WriteLine($"[ACTION BOUNDS] origin={point} size={primary.RenderSize} page={page.RenderSize}");
                    }
                    Check(point.Y < 220 && point.X >= 0 && point.X + primary.ActualWidth <= page.ActualWidth, state + " primary action stays in the first viewport at " + width);
                    Check(!dash.IsVisible, state + " uses content flow instead of saved canvas dimensions");
                    if (state == "installed" && width <= 900)
                        Check(Field<ComboBox>(page, "_acc").TranslatePoint(new Point(), page).Y < Field<FrameworkElement>(page, "_librarySection").TranslatePoint(new Point(), page).Y, "narrow workspace keeps account selection before the recent-instance list");
                    Program.SaveSnapshot(window, Path.Combine(folder, state + "-" + (dark ? "dark" : "light") + "-" + width + ".png"));
                }
            }
        }
    }
}
