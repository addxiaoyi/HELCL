using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Minecraft;
using HelL.Pages.Ai;
using HelL.Services;

internal static class MinecraftRecoveryCheck
{
    internal static async Task Run(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        string runtimeRoot = File.ReadAllText(Path.Combine(folder, "prepared-runtime-root.txt")).Trim();
        Config.CONFIG.Set("minecraft_runtime_root", runtimeRoot);
        Check(MinecraftRuntime.Ready, "Corrected component bundle is installed and discoverable");
        var diagnosis = await Task.Run(() => MinecraftRuntime.Diagnose(MinecraftRuntime.FindNode(), true));
        Check(Py.Truthy(diagnosis.Get("ready")), "The delivered component passes its file and live MCP startup checks");
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var api = AppServices.Client;
        await api.CallAsync("minecraft_configure", new { host = "127.0.0.1", port, name = "HelLAI", auth = "offline", version = "", node = MinecraftRuntime.FindNode() });
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try { await api.CallAsync("minecraft_connect"); }
            catch (Exception ex) { Check(ex.Message.Contains("对局域网开放"), "Preflight failure includes the original connection guidance"); }
            PyDict state = new PyDict();
            for (int n = 0; n < 80; n++)
            {
                state = await Task.Run(() => MinecraftSession.Status());
                if (Py.S(state.Get("phase")) == "error") break;
                await Task.Delay(100);
            }
            Check(Py.S(state.Get("error")).Contains("对局域网开放") && !Py.S(state.Get("error")).Contains("exit="), "Refused connection gives a recoverable error on attempt " + (attempt + 1));
            Check(!MinecraftSession.Running, "Failed session releases its component process");
        }
        var panel = new MinecraftPanel(); Dlg.Panel("AI 玩家", panel, 660, footer: panel.Actions); await panel.EnsureLoadedAsync();
        window.Width = 900; window.Height = 620; window.UpdateLayout(); await Task.Delay(400);
        Program.SaveSnapshot(window, Path.Combine(folder, "refused-connection.png"));
        string script = Path.Combine(folder, "missing-module.mjs");
        File.WriteAllText(script, "import 'hell-missing-fixture-package';");
        var broken = new McpClient("minecraft", MinecraftRuntime.FindNode(), Py.L(script));
        try
        {
            var error = await Task.Run(() => { try { broken.Connect(); return ""; } catch (McpError ex) { return ex.Message; } });
            Check(error.Contains("组件文件缺失") && error.Contains("修复游戏组件"), "A genuine startup failure explains how to repair the missing component");
        }
        finally { broken.Abort(); MinecraftSession.Stop(); }
        Console.WriteLine("[MCP RECOVERY] Nine recovery checks passed; no live world was used.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("[PASS] " + message); }
}
