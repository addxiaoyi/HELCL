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
using System.Windows.Threading;
using HelL;
using HelL.Core;
using HelL.Core.Launcher.Ai.Minecraft;
using HelL.Pages;
using HelL.Pages.Ai;
using HelL.Services;

internal static class ExtensionsPreview
{
    private static int _checks;
    internal static async Task Capture(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("HELL_FEIYU_LINK", Path.Combine(folder, "isolated-link.json"));
        Motion.Enabled = false;
        window.Navigate("ai", instant: true); await Program.WaitForPage(window, "ai");
        var ai = Walk(window).OfType<AiPage>().Single();
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 900; window.Height = 620; await Layout(window);
            Check(!Walk(ai).OfType<Button>().Any(button => button.IsVisible && Name(button) == "AI 玩家"), "Chat header does not expose optional gameplay as a core action");
            Program.SaveSnapshot(window, Path.Combine(folder, "ai-" + (dark ? "dark" : "light") + "-900.png"));
        }
        Named(ai, "拓展").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(window);
        var extensions = Walk(window).OfType<ExtensionsPanel>().Single(); await extensions.EnsureLoadedAsync();
        var toggle = Walk(extensions).OfType<CheckBox>().Single();
        Check(toggle.IsChecked == false && !MinecraftExtensions.FeiyuEnabled, "Feiyu extension is disabled by default in UI and backend");
        Check(!File.Exists(FeiyuLink.DiscoveryPath), "Opening extensions does not start the link listener");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); await Layout(window);
            Program.SaveSnapshot(window, Path.Combine(folder, "extensions-" + (dark ? "dark" : "light") + ".png"));
        }
        toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Until(() => toggle.IsEnabled && MinecraftExtensions.FeiyuEnabled, "Extension preference is saved");
        Check(!MinecraftSession.Running && !File.Exists(FeiyuLink.DiscoveryPath), "Enabling the extension grants no connection or control by itself");
        Program.SaveSnapshot(window, Path.Combine(folder, "extensions-enabled.png"));
        toggle.IsChecked = false; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Until(() => toggle.IsEnabled && !MinecraftExtensions.FeiyuEnabled, "Extension can be disabled again");
        Named(extensions, "打开 AI 玩家").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(window);
        var player = Walk(Dlg.Host!).OfType<MinecraftPanel>().Single(); await player.EnsureLoadedAsync();
        Check(AutomationProperties.GetName(Read<TextBox>(player, "_host")) == "服务器地址", "Connection fields have persistent accessible labels");
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            foreach (var size in new[] { (900d, 620d), (1180d, 740d) })
            {
                window.Width = size.Item1; window.Height = size.Item2; await Layout(window);
                CheckBounds(player.Actions, window, "Player footer fits " + size);
                Check(!Walk(player).OfType<Button>().Any(button => button.IsVisible && Name(button) == "交给大肥鱼"), "Player connection flow has no mandatory Feiyu action");
                Program.SaveSnapshot(window, Path.Combine(folder, "player-connect-" + (dark ? "dark" : "light") + "-" + size.Item1 + ".png"));
            }
        }
        window.Width = 900; window.Height = 620;
        Check(((SolidColorBrush)Ui.Res("B.Popup")).Opacity == 1, "Dialog reading surface is opaque after theme changes");
        var host = Read<TextBox>(player, "_host"); var port = Read<TextBox>(player, "_port");
        host.Text = "127.0.0.1"; port.Text = "bad-port";
        Named(player, "检测服务器").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => FieldState.GetHasError(port), "Invalid port has an inline field error");
        Check(port.Text == "bad-port", "Invalid input remains available to correct");
        port.Text = "25565";
        Check(!FieldState.GetHasError(port), "Editing the field clears its validation state");
        Named(Dlg.Host!, "安装游戏组件").Focus();
        ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Check(Keyboard.FocusedElement is DependencyObject focused && Walk(Dlg.Host!).Contains(focused), "Tab navigation stays in the player dialog");
        Named(player, "高级").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(window);
        var autoStart = Read<CheckBox>(player, "_autoStart");
        Check(autoStart.IsChecked == false, "Independent model startup is off by default");
        autoStart.IsChecked = true; autoStart.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Until(() => MinecraftExtensions.ModelAutoStart && autoStart.IsEnabled, "Model startup is an explicit preference");
        autoStart.IsChecked = false; autoStart.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await Until(() => !MinecraftExtensions.ModelAutoStart && autoStart.IsEnabled, "Model startup can be disabled without preparing a model");
        foreach (var scroll in Walk(Dlg.Host!).OfType<ScrollViewer>()) scroll.ScrollToBottom();
        await Layout(window);
        CheckBounds(player.Actions, window, "Player footer remains visible while advanced content scrolls");
        Program.SaveSnapshot(window, Path.Combine(folder, "player-advanced.png"));

        using var captured = JsonDocument.Parse(File.ReadAllText("D:/qwq/qdq/TestResults/feiyu-link-20261108/dashboard.json"));
        var snapshot = (PyDict)PyJson.Loads(captured.RootElement.GetRawText());
        var connecting = snapshot.Copy(); connecting["phase"] = "connecting"; connecting["connected"] = false; connecting["tools_enabled"] = false; connecting["controller"] = "none";
        Apply(player, connecting);
        Check(Named(Dlg.Host!, "断开连接").IsEnabled && !Read<Button>(player, "_primary").IsEnabled, "Connecting can be cancelled while repeated connection attempts are disabled");
        snapshot["runtime_ready"] = true; snapshot["tools_enabled"] = true; snapshot["controller"] = "launcher";
        Apply(player, snapshot);
        foreach (var scroll in Walk(Dlg.Host!).OfType<ScrollViewer>()) scroll.ScrollToTop();
        Named(player, "世界状态").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); await Layout(window);
            CheckBounds(player.Actions, window, "Stop remains visible with world state");
            Check(Named(Dlg.Host!, "立即停止并断开").IsEnabled, "Stop stays available independently of the primary action");
            Program.SaveSnapshot(window, Path.Combine(folder, "player-world-" + (dark ? "dark" : "light") + ".png"));
        }
        foreach (string label in new[] { "背包", "动作记录" })
        {
            Named(player, label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(window);
            Program.SaveSnapshot(window, Path.Combine(folder, label + ".png"));
        }
        snapshot["phase"] = "error"; snapshot["connected"] = false; snapshot["tools_enabled"] = false;
        snapshot["error"] = "无法连接服务器，请检查地址和端口后重试。";
        Apply(player, snapshot); Named(player, "连接").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(window);
        Check(Named(Dlg.Host!, "重新连接").IsEnabled, "Connection failure exposes a retry action");
        Program.SaveSnapshot(window, Path.Combine(folder, "player-error.png"));
        CloseDialog(window); await Layout(window);
        Named(extensions, "返回对话").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Layout(window);
        Check(Named(ai, "拓展").IsVisible, "Closing extensions restores the existing conversation");
        I18n.Load("en");
        Check(I18n.L("高级") == "Advanced", "Packaged English translations load without an external locale folder");
        try
        {
            var english = new MinecraftPanel();
            Dlg.Panel("AI player", english, 660, footer: english.Actions); await english.EnsureLoadedAsync();
            window.Width = 900; window.Height = 620; await Layout(window);
            CheckBounds(english.Actions, window, "English player footer fits at the narrow width");
            Program.SaveSnapshot(window, Path.Combine(folder, "player-english.png"));
            Named(english, "Advanced").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(window); CheckBounds(english.Actions, window, "Expanded English settings keep the footer visible");
            CloseDialog(window);
        }
        finally { I18n.Load("zh_CN"); }
        Console.WriteLine("[EXTENSIONS UI] " + _checks + " checks passed; connected states replay saved real-game observations.");
    }
    private static void Apply(MinecraftPanel player, PyDict snapshot)
    {
        using var json = JsonDocument.Parse(PyJson.Dumps(snapshot));
        typeof(MinecraftPanel).GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, new object[] { json.RootElement.Clone() });
        Read<DispatcherTimer>(player, "_timer").Stop();
    }
    private static async Task Until(Func<bool> ready, string message)
    { for (int i = 0; i < 100; i++) { if (ready()) return; await Task.Delay(50); } throw new TimeoutException(message); }
    private static T Read<T>(object source, string name) => (T)source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
    private static string Name(Button button) => AutomationProperties.GetName(button) is string name && name.Length > 0 ? name : button.Content as string ?? "";
    private static Button Named(DependencyObject root, string name) => Walk(root).OfType<Button>().First(button => button.IsVisible && Name(button) == name);
    private static void CheckBounds(UIElement footer, Window window, string message)
    {
        var element = (FrameworkElement)footer; var p = element.TranslatePoint(new Point(), window);
        Check(p.X >= 0 && p.Y >= 0 && p.X + element.ActualWidth <= window.ActualWidth && p.Y + element.ActualHeight <= window.ActualHeight, message);
    }
    private static void CloseDialog(Window window) => Dlg.Host!.Children[Dlg.Host.Children.Count - 1].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static Task Layout(Window window) { window.UpdateLayout(); return Task.Delay(220); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root) { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Walk(child)) yield return nested; } }
}
