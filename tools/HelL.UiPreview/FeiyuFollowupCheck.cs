using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Minecraft;
using HelL.Models;
using HelL.Services;

internal static class FeiyuFollowupCheck
{
    internal static async Task Run(MainWindow window, string folder, int port, Func<string, Task> command)
    {
        string link = Path.Combine(folder, "private-link.json"); Environment.SetEnvironmentVariable("HELL_FEIYU_LINK", link);
        var api = AppServices.Client;
        string modelTask = await api.StartTaskAsync("minecraft_prepare_model");
        var prepared = await api.CallAsync<OpResult>("wait_task", new { task_id = modelTask, timeout = 1200 });
        Check(prepared?.Ok == true, "Independent Qwen3 1.7B is prepared for both applications");
        await command("gamemode survival HelLAI"); await command("clear HelLAI"); await command("tp HelLAI 0.5 64 0.5");
        await command("kill @e[type=item]");
        await command("summon item 4.5 64 -3.5 {Item:{id:\"minecraft:apple\",count:2},PickupDelay:0s}");
        await Task.Delay(500);
        var picked = await Tool("pickup_items", PyDict.Of("radius", 8L, "limit", 2L));
        File.WriteAllText(Path.Combine(folder, "pickup.json"), PyJson.Dumps(picked, 2));
        Check(Py.Int(picked.Get("collected")) >= 2, "Pickup counts real items added to the survival inventory");
        var guide = new McpClient("guide", MinecraftRuntime.FindNode(), Py.L(MinecraftRuntime.Script), PyDict.Of("HELL_MC_HOST", "127.0.0.1", "HELL_MC_PORT", port, "HELL_MC_NAME", "HelLGuide", "HELL_MC_AUTH", "offline", "HELL_MC_VERSION", "1.21.1", "HELL_MC_PROFILE", Path.Combine(folder, "guide-login")));
        try
        {
            await Task.Run(guide.Connect); await Task.Run(() => guide.CallTool("connect", new PyDict()));
            for (int i = 0; i < 100; i++)
            {
                var state = (PyDict)((PyDict)PyJson.Loads(await Task.Run(() => guide.CallTool("status", new PyDict())))).Get("data");
                if (Py.Truthy(state.Get("connected"))) break; await Task.Delay(100);
            }
            await command("tp HelLGuide 2.5 64 2.5"); await Task.Delay(400);
            var follow = Tool("follow_player", PyDict.Of("player", "HelLGuide", "seconds", 4L, "distance", 2.0));
            await Task.Delay(700); await command("tp HelLGuide 6.5 64 2.5");
            var followed = await follow;
            Check(Py.Float(followed.Get("distance")) <= 3.5, "The AI player follows a second real player that changes position");
        }
        finally { guide.Abort(); }
        await command("tp HelLAI 0.5 64 0.5");
        await command("give HelLAI iron_axe");
        await command("fill 2 64 0 2 66 0 oak_log"); await command("fill 1 67 -1 3 67 1 oak_leaves[persistent=true]");
        await Task.Delay(700);
        var before = MinecraftSession.SharedStatus();
        Check(Py.ListOr((before.Get("map") as PyDict)?.Get("tiles") ?? new List<object>()).Count == 289, "Dashboard exposes an actual 17 by 17 loaded terrain grid");
        Check(Py.ListOr(before.Get("history")).Count > 0 && Py.ListOr(before.Get("inventory")).Count > 0, "Dashboard contains real action receipts and inventory");
        await api.CallAsync("minecraft_extension_set", new { extension = "feiyu", enabled = true });
        await api.CallAsync("minecraft_allow_feiyu");
        Check(MinecraftSession.Schemas().Count == 0, "Feiyu ownership removes launcher chat tools");
        bool rejected = false;
        try { await Tool("move_to", PyDict.Of("x", 1L, "y", 64L, "z", 1L)); } catch (McpError) { rejected = true; }
        Check(rejected, "Launcher calls cannot compete with the delegated controller");
        var discovery = (PyDict)Utils.ReadJson(link);
        using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }))
        {
            var unauthorized = await http.GetAsync(Py.S(discovery.Get("endpoint")) + "/status");
            Check((int)unauthorized.StatusCode == 403, "A missing link token cannot read or control the bot");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + Py.S(discovery.Get("token")));
            var status = (PyDict)PyJson.Loads(await http.GetStringAsync(Py.S(discovery.Get("endpoint")) + "/status"));
            Check(!((PyDict)status.Get("data")).ContainsKey("login") && !((PyDict)status.Get("data")).ContainsKey("options"), "Shared state excludes authentication codes and connection settings");
        }
        using var probe = Process.Start(new ProcessStartInfo("py", Utils.List2CmdLine(new[] { "-3.11", "-X", "utf8", "M:/大肥鱼/live/tests/hell_live_probe.py", Path.Combine(folder, "feiyu-verification.json") }))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = "M:/大肥鱼/live", RedirectStandardOutput = true, RedirectStandardError = true }) ?? throw new IOException("Feiyu probe failed to start");
        var stdout = probe.StandardOutput.ReadToEndAsync(); var stderr = probe.StandardError.ReadToEndAsync();
        for (int i = 0; i < 1800 && !probe.HasExited; i++)
        {
            if (i == 30)
            {
                foreach (var scroll in Walk(Dlg.Host!).OfType<ScrollViewer>()) scroll.ScrollToBottom();
                await Task.Delay(400); window.UpdateLayout();
                Program.SaveSnapshot(window, Path.Combine(folder, "feiyu-dashboard.png"));
                foreach (string tab in new[] { "背包", "动作记录" })
                {
                    var button = Walk(Dlg.Host!).OfType<Button>().First(control => control.Content as string == tab);
                    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); await Task.Delay(250);
                    Program.SaveSnapshot(window, Path.Combine(folder, tab + ".png"));
                }
            }
            await Task.Delay(100);
        }
        if (!probe.HasExited) { probe.Kill(); throw new TimeoutException("Feiyu integration timed out"); }
        File.WriteAllText(Path.Combine(folder, "feiyu-probe.log"), await stdout + "\n" + await stderr);
        Check(probe.ExitCode == 0, "Feiyu GameController harvests, moves via the shared local model and sends its emergency stop");
        for (int n = 0; n < 20 && MinecraftSession.Running; n++) await Task.Delay(100);
        Check(!MinecraftSession.Running && !File.Exists(link), "Feiyu emergency revokes the link and disconnects the same player");
        File.WriteAllText(Path.Combine(folder, "dashboard.json"), PyJson.Dumps(before, 2));
        MinecraftModels.Stop();
    }
    private static async Task<PyDict> Tool(string name, PyDict args) => (PyDict)((PyDict)PyJson.Loads(await Task.Run(() => MinecraftSession.Call(MinecraftSession.Prefix + name, args)))).Get("data");
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("[PASS] " + message); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root) { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Walk(child)) yield return nested; } }
}
