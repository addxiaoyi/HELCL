using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using HelL;
using HelL.Pages;
using HelL.Services;

internal static class AppearanceRestartCheck
{
    internal static string Prepare(string folder, bool initial)
    {
        folder = Path.GetFullPath(folder);
        string home = Path.Combine(folder, "profile");
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("HELL_APPEARANCE_RESTART", folder);
        if (initial)
        {
            File.WriteAllText(Path.Combine(folder, "phase.txt"), "0");
            File.WriteAllText(Path.Combine(folder, "restart.log"), "");
            File.WriteAllText(Path.Combine(home, "config.json"), "{\"first_run\":false,\"feedback_consent\":false,\"feedback_heartbeat\":false,\"auto_check_update\":false,\"ui_wallpaper_mode\":\"material\",\"ui_glass_transparency\":100,\"ui_glass_frost\":0}");
        }
        return home;
    }

    internal static async Task Run(MainWindow window, string folder)
    {
        folder = Path.GetFullPath(folder);
        window.Title = "HelL · 外观重启回归";
        Motion.Enabled = false;
        int phase = int.Parse(File.ReadAllText(Path.Combine(folder, "phase.txt")));
        int pid = Process.GetCurrentProcess().Id;
        void Pass(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            File.AppendAllText(Path.Combine(folder, "restart.log"), $"[PASS] pid={pid} phase={phase} {label}{Environment.NewLine}");
        }

        try
        {
            await Wallpaper.ReloadAsync();
            await MaterialControlsPreview.Layout(window);
            if (phase > 0)
            {
                int previous = int.Parse(File.ReadAllText(Path.Combine(folder, "parent.txt")));
                Pass(previous != pid, "appearance restart created a new process");
                await MaterialControlsPreview.Until(() => HasExited(previous), "previous process exits after saving layout");
                Pass(true, "previous process released its backend and window");
            }
            Pass(window.AllowsTransparency == (phase == 1), "window composition matches persisted wallpaper mode");
            Pass(Backdrop.ClearAvailable == (phase == 1), "desktop transparency initialized after restart");
            Pass(Glass.Transparency == 100 && Glass.Frost == 0, "material preferences survive process restart");
            if (phase == 2)
            {
                Pass(Backdrop.NativeAvailable && !Backdrop.ClearDesktop, "switching back restores the system material");
                File.WriteAllText(Path.Combine(folder, "completed.txt"), "material -> transparent -> material; 3 processes; passed");
                Application.Current.Shutdown(0);
                return;
            }

            await WallpaperDialog.Show();
            await MaterialControlsPreview.Layout(window);
            var modes = CustomizationPreview.Walk(Dlg.Host!).OfType<ComboBox>()
                .Single(combo => combo.Items.Contains(I18n.L("直接透出桌面 / 其他窗口")));
            modes.SelectedIndex = phase == 0 ? 4 : 3;
            CustomizationPreview.Named(Dlg.Host!, "应用材质").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Button? restart = null;
            await MaterialControlsPreview.Until(() =>
            {
                restart = CustomizationPreview.Walk(Dlg.Host!).OfType<Button>()
                    .FirstOrDefault(button => AutomationProperties.GetName(button) == I18n.L("重启应用外观") && button.IsVisible);
                return restart != null;
            }, "wallpaper mode exposes a restart action");
            Pass(true, "mode saved through the wallpaper dialog");
            if (phase == 0)
            {
                CustomizationPreview.Named(Dlg.Host!, "关闭").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WallpaperDialog.Show();
                await MaterialControlsPreview.Layout(window);
                restart = CustomizationPreview.Walk(Dlg.Host!).OfType<Button>()
                    .First(button => AutomationProperties.GetName(button) == I18n.L("重启应用外观"));
                Pass(restart.IsVisible, "pending restart remains available after reopening wallpaper settings");
            }
            File.WriteAllText(Path.Combine(folder, "parent.txt"), pid.ToString());
            File.WriteAllText(Path.Combine(folder, "phase.txt"), (phase + 1).ToString());
            restart!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(folder, "restart.log"), "[FAIL] " + error + Environment.NewLine);
            throw;
        }
    }

    private static bool HasExited(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }
}
