using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using HelL;
using HelL.Services;

internal static class NativeScaleAudit
{
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr handle);
    internal static async Task Run(MainWindow window, int expected, string folder)
    {
        Directory.CreateDirectory(folder); Motion.Enabled = false;
        window.Title = "HelL · 系统缩放 " + expected + " DPI";
        window.Width = 900; window.Height = 660;
        await MaterialControlsPreview.Layout(window);
        uint native = GetDpiForWindow(new WindowInteropHelper(window).Handle);
        double wpf = VisualTreeHelper.GetDpi(window).PixelsPerInchX;
        Console.WriteLine($"[NATIVE SCALE] requested={expected}; Win32={native}; WPF={wpf}; no SetRootDpi override");
        if (native != expected || Math.Abs(wpf - expected) > .1) throw new InvalidOperationException("Windows display scale differs from requested scale");
        await TypographyChecks.Run(window);
        window.Width = 900; window.Height = 660;
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark);
            var layer = TypographyAudit.ShowControls(); await MaterialControlsPreview.Layout(window);
            TypographyAudit.Capture(window, Path.Combine(folder, "controls-" + dark + ".png"));
            var combo = RichUiAudit.Find<ComboBox>(layer.Card).Single();
            combo.IsDropDownOpen = true; await MaterialControlsPreview.Layout(window);
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            double popupDpi = VisualTreeHelper.GetDpi(popup.Child).PixelsPerInchX;
            Console.WriteLine($"[NATIVE POPUP] requested={expected}; actual={popupDpi}");
            if (Math.Abs(popupDpi - expected) > .1) throw new InvalidOperationException("Native popup scale differs from parent display");
            TypographyAudit.Capture((FrameworkElement)popup.Child, Path.Combine(folder, "selector-" + dark + ".png"));
            combo.IsDropDownOpen = false;
            var menu = new ContextMenu { PlacementTarget = combo, Placement = PlacementMode.Bottom };
            menu.Items.Add(new MenuItem { Header = "打开实例目录", Icon = Ui.Glyph("\ue8b7") });
            menu.Items.Add(new MenuItem { Header = "刷新列表", InputGestureText = "F5", Icon = Ui.Glyph("\ue72c") });
            menu.IsOpen = true; await MaterialControlsPreview.Layout(window);
            TypographyAudit.Capture(menu, Path.Combine(folder, "menu-" + dark + ".png"));
            menu.IsOpen = false; layer.Dismiss(); await MaterialControlsPreview.Layout(window);
        }
        App.ApplyTheme(false); TypographyAudit.ShowControls(); window.Activate();
        Console.WriteLine("[NATIVE SCALE PASS] Native window, controls, selector and context menu captured at " + expected + " DPI.");
    }
}
