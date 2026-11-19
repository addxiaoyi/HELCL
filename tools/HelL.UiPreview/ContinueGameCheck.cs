using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Pages;
using HelL.Services;

internal static class ContinueGameCheck
{
    internal static void Configure(string profile, string readyHome)
    {
        readyHome = Path.GetFullPath(readyHome);
        if (!File.Exists(Path.Combine(readyHome, ".hell-verification"))) throw new IOException("Continue test requires a marked isolated game directory.");
        using var verified = JsonDocument.Parse(File.ReadAllText(Path.Combine(readyHome, "verification.json")));
        string java = verified.RootElement.GetProperty("java").GetString()!;
        if (!File.Exists(java)) throw new FileNotFoundException("Verified Java no longer exists.");
        File.WriteAllText(Path.Combine(profile, "config.json"), JsonSerializer.Serialize(new
        {
            first_run = false, feedback_consent = false, feedback_heartbeat = false, auto_check_update = false,
            instances_dir = Path.Combine(readyHome, ".minecraft"), default_instance = "verify-demo", default_java = java,
            memory_mb = 2048, default_isolation = "none", launcher_visibility = "keep", ui_dark = true
        }));
    }

    internal static async Task Run(MainWindow window, string readyHome, bool recovery = false)
    {
        readyHome = Path.GetFullPath(readyHome);
        using var verified = JsonDocument.Parse(File.ReadAllText(Path.Combine(readyHome, "verification.json")));
        string version = verified.RootElement.GetProperty("version").GetString()!;
        string java = verified.RootElement.GetProperty("java").GetString()!;
        var instance = new Instance("verify-demo");
        if (!instance.path.StartsWith(readyHome + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Test instance escaped the prepared directory.");
        var oldSettings = VersionSettings.Load(instance, version).Copy();
        VersionSettings.Save(instance, version, PyDict.Of("game_args", "--demo", "isolation", "none", "java", java, "memory_mb", 2048L));
        if (recovery) VersionSettings.Save(instance, version, PyDict.Of("java", Path.Combine(readyHome, "missing-java.exe")));
        Playtime.RecordSession(instance.name, version, 60);
        var api = AppServices.Client;
        var transcript = new List<string>();
        void OnEvent(object? _, BridgeEvent e)
        {
            if (e.Event == "log" && !string.IsNullOrEmpty(e.Text)) { lock (transcript) transcript.Add(e.Text); }
        }
        api.EventReceived += OnEvent;
        LaunchPage? page = null;
        try
        {
            window.Navigate("launch", instant: true); await Program.WaitForPage(window, "launch");
            page = Walk(window).OfType<LaunchPage>().Single();
            await (Task)typeof(LaunchPage).GetMethod("LoadRecentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
            var resume = Walk(page).OfType<Button>().Single(button => button.IsVisible && AutomationProperties.GetName(button) == "继续游戏");
            resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => Dlg.AnyOpen, "Offline account form did not open", 100);
            var name = Walk(Dlg.Host!).OfType<TextBox>().First(); name.Text = "HelLContinue";
            Walk(Dlg.Host!).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "创建并继续").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Process? game = null;
            bool repaired = false;
            for (int i = 0; i < 1800; i++)
            {
                if (Dlg.AnyOpen)
                {
                    string prompt = string.Join(" | ", Walk(Dlg.Host!).OfType<TextBlock>().Select(text => text.Text));
                    var repair = Walk(Dlg.Host!).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == "自动匹配 Java 并重新检测");
                    if (recovery && !repaired && repair != null)
                    {
                        repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); repaired = true;
                        await Task.Delay(300); continue;
                    }
                    if (!prompt.Contains("创建离线用户")) throw new InvalidOperationException("Launch preflight requested intervention: " + prompt);
                }
                if (i % 20 == 0) game = FindTestGame(readyHome);
                if (game != null)
                {
                    game.Refresh();
                    bool textures;
                    lock (transcript) textures = transcript.Any(line => line.Contains("textures/atlas"));
                    if (game.MainWindowHandle != IntPtr.Zero && textures) break;
                }
                await Task.Delay(100);
            }
            if (game == null || game.MainWindowHandle == IntPtr.Zero) throw new TimeoutException("Continue did not render a game window.");
            if (recovery && (!repaired || !Java.JavaUsableFor(instance.VersionJson(version) as PyDict ?? throw new IOException("Missing test version manifest"), VersionSettings.Load(instance, version).Get("java"))))
                throw new IOException("Launch recovery did not save a verified compatible Java.");
            await Task.Delay(10000);
            if (game.HasExited) throw new IOException("Game exited after first rendering.");
            var accounts = await api.CallAsync("get_account_rows");
            if (!accounts.GetRawText().Contains("HelLContinue")) throw new IOException("UI account creation did not persist.");
            string task = Read<string>(page, "_taskId");
            File.WriteAllText(Path.Combine(readyHome, "continue-verification.json"), JsonSerializer.Serialize(new
            {
                instance = instance.name, version, account = "HelLContinue", accountType = "offline", demo = true,
                java, javaMajor = Java.GetJavaMajor(java), javaRepaired = repaired, viaContinueButton = true, preflightForced = false,
                gamePid = game.Id, windowTitle = game.MainWindowTitle, renderedWindow = true,
                textureAtlasLoaded = transcript.Any(line => line.Contains("textures/atlas")), completedAtUtc = DateTime.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true }));
            Program.SaveSnapshot(window, Path.Combine(readyHome, "continue-launcher.png"));
            Console.WriteLine("[CONTINUE LIVE] Account creation, exact version selection, Java 21 preflight and rendered demo game passed.");
            game.CloseMainWindow();
            await Until(() => game.HasExited, "Game did not close", 150);
            game.Dispose();
        }
        finally
        {
            if (page != null)
            {
                string? task = Read<string?>(page, "_taskId");
                if (!string.IsNullOrEmpty(task)) await api.CallAsync("cancel_task", new { task_id = task });
            }
            api.EventReceived -= OnEvent;
            lock (transcript) File.WriteAllLines(Path.Combine(readyHome, "continue-game.log"), transcript);
            VersionSettings.Save(instance, version, oldSettings);
        }
    }

    private static Process? FindTestGame(string readyHome)
    {
        using var query = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='java.exe' OR Name='javaw.exe'");
        using var found = query.Get();
        foreach (ManagementObject process in found)
        {
            using (process)
            {
                string command = Convert.ToString(process["CommandLine"]) ?? "";
                if (command.IndexOf(readyHome, StringComparison.OrdinalIgnoreCase) >= 0 && command.Contains("HelLContinue") && command.Contains("--demo"))
                    return Process.GetProcessById(Convert.ToInt32(process["ProcessId"]));
            }
        }
        return null;
    }
    private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static async Task Until(Func<bool> ready, string message, int attempts) { for (int n = 0; n < attempts; n++) { if (ready()) return; await Task.Delay(100); } throw new TimeoutException(message); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Walk(child)) yield return nested; } }
}
