using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Services;

internal static class TransparentDesktopCheck
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        var board = new Grid();
        board.ColumnDefinitions.Add(new ColumnDefinition()); board.ColumnDefinitions.Add(new ColumnDefinition());
        board.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(235, 221, 199)) });
        var right = new Border { Background = new SolidColorBrush(Color.FromRgb(93, 144, 160)) }; Grid.SetColumn(right, 1); board.Children.Add(right);
        var label = new TextBlock { Text = "桌面透视验证 · 背后窗口", FontSize = 32, Foreground = Brushes.DarkSlateGray,
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(30) };
        Grid.SetColumnSpan(label, 2); board.Children.Add(label);
        var behind = new Window { Title = "HelL · 透明背景验证", Width = 1250, Height = 780, Left = 24, Top = 24,
            WindowStartupLocation = WindowStartupLocation.Manual, Content = board, ShowInTaskbar = false };
        behind.Show(); window.Owner = behind;
        window.Left = 54; window.Top = 54; window.Width = 1180; window.Height = 700; window.Activate();
        window.Title = "HelL · 桌面完全透明实测";
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> {
            ["ui_wallpaper_mode"] = "transparent", ["ui_glass_transparency"] = 100, ["ui_glass_frost"] = 0,
            ["ui_glass_cards"] = 100, ["ui_glass_controls"] = 100, ["ui_glass_popups"] = 100 } });
        await Wallpaper.ReloadAsync(); window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
        await MaterialControlsPreview.Layout(window);
        if (!Backdrop.ClearAvailable || Wallpaper.Active) throw new InvalidOperationException("Desktop transparency did not initialize.");
        foreach (WindowState state in new[] { WindowState.Maximized, WindowState.Normal })
        {
            ((Button)window.FindName("MaxBtn")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await MaterialControlsPreview.Layout(window);
            if (window.WindowState != state) throw new InvalidOperationException("Transparent window cannot toggle to " + state);
            Console.WriteLine("[PASS] transparent window toggles to " + state);
        }
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "material" } });
        await Wallpaper.ReloadAsync();
        if (Backdrop.ClearDesktop || !window.AllowsTransparency) throw new InvalidOperationException("Transparent window did not release desktop mode.");
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_mode"] = "transparent" } });
        await Wallpaper.ReloadAsync();
        if (!Backdrop.ClearAvailable) throw new InvalidOperationException("Transparent window did not restore desktop mode.");
        Console.WriteLine("[PASS] transparent and solid mode round trip preserves the window");
        File.WriteAllText(Path.Combine(folder, "desktop-ready.txt"), $"ClearAvailable={Backdrop.ClearAvailable}; material={Backdrop.Material}; wallpaper={Wallpaper.Active}; dpi={VisualTreeHelper.GetDpi(window).PixelsPerInchX}");
        Console.WriteLine("[DESKTOP READY] Inspect the composited window. Close the preview when finished.");
        foreach (string key in new[] { "B.WindowBase", "B.PageWash", "B.Glass", "B.Card", "B.Popup" })
        {
            var brush = (SolidColorBrush)Ui.Res(key);
            if (brush.Opacity * brush.Color.A != 0) throw new InvalidOperationException("Opaque layer in transparent window: " + key);
            Console.WriteLine("[PASS] " + key + " rendered alpha=0");
        }
        var input = (SolidColorBrush)window.Background;
        if (Math.Abs(input.Opacity * input.Color.A - 1) > .001)
            throw new InvalidOperationException("Desktop transparency needs one alpha step to receive mouse input.");
        Console.WriteLine("[PASS] desktop window retains exactly 1/255 input alpha; verify mouse clicks and dragging separately");
        window.Closed += (_, _) => behind.Close();
    }
}
