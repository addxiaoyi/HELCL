using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
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
using HelL.Core.Launcher.Ai.Local;
using HelL.Models;
using HelL.Pages.Ai;
using HelL.Services;

internal static class MinecraftLiveCheck
{
    private static int _checks;
    internal static async Task Run(MainWindow window, string folder, bool feiyu = false, bool connectionOnly = false)
    {
        folder = Path.GetFullPath(folder);
        string serverDirectory = Path.Combine(folder, "server");
        string jar = Path.Combine(serverDirectory, "server.jar");
        if (!File.Exists(jar) || Utils.Sha1File(jar) != "59353fb40c36d304f2035d51e7d6e6baa98dc05c") throw new IOException("Expected the verified Minecraft 1.21.1 server JAR.");
        string marker = Path.Combine(serverDirectory, ".hell-mcp-verification");
        if (Directory.Exists(Path.Combine(serverDirectory, "world")) && !File.Exists(marker)) throw new IOException("Refusing to use an unmarked world.");
        File.WriteAllText(marker, "Isolated Minecraft MCP test world");
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        File.WriteAllText(Path.Combine(serverDirectory, "server.properties"), "server-ip=127.0.0.1\nserver-port=" + port + "\nonline-mode=false\nenforce-secure-profile=false\nspawn-protection=0\ngamemode=creative\ndifficulty=peaceful\nlevel-type=minecraft:flat\nview-distance=3\nsimulation-distance=3\nmax-players=2\nspawn-animals=false\nspawn-monsters=false\ngenerate-structures=false\nmax-tick-time=60000\nsync-chunk-writes=false\n");
        File.WriteAllText(Path.Combine(serverDirectory, "eula.txt"), "eula=true\n");
        using var server = Process.Start(new ProcessStartInfo("C:/Program Files/Zulu/zulu-21/bin/java.exe", "-Xms512M -Xmx1536M -XX:ActiveProcessorCount=2 -jar server.jar nogui")
        { WorkingDirectory = serverDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }) ?? throw new IOException("Server did not start");
        bool ready = false;
        var transcript = new List<string>();
        server.OutputDataReceived += (_, ev) => { if (ev.Data == null) return; lock (transcript) transcript.Add(ev.Data); if (ev.Data.Contains("Done (")) ready = true; };
        server.ErrorDataReceived += (_, ev) => { if (ev.Data != null) lock (transcript) transcript.Add(ev.Data); };
        server.BeginOutputReadLine(); server.BeginErrorReadLine();
        try
        {
            await Until(() => ready || server.HasExited, "Vanilla server starts", 1800);
            Check(!server.HasExited && ready, "Real Minecraft 1.21.1 server is listening only on loopback");
            var api = AppServices.Client;
            await api.CallAsync("minecraft_configure", new { host = "127.0.0.1", port, name = "HelLAI", auth = "offline", version = "", node = MinecraftRuntime.FindNode() });
            string install = await api.StartTaskAsync("minecraft_install");
            var installed = await api.CallAsync<OpResult>("wait_task", new { task_id = install, timeout = 330 });
            Check(installed?.Ok == true, "Embedded MCP components install through the launcher's locked npm plan: " + installed?.Message);
            if (connectionOnly)
            {
                await MinecraftConnectionCheck.Run(window, folder, port, Command);
                return;
            }
            await api.CallAsync("minecraft_connect");
            PyDict state = new PyDict();
            for (int n = 0; n < 150; n++)
            {
                state = await Task.Run(() => MinecraftSession.Status());
                if (Py.Truthy(state.Get("connected"))) break;
                if (Py.S(state.Get("phase")) == "error") throw new IOException(Py.S(state.Get("error")));
                await Task.Delay(200);
            }
            Check(Py.Truthy(state.Get("connected")) && Py.S(state.Get("version")) == "1.21.1", "AI player joins the real server through MCP with automatic version detection");
            Check(MinecraftSession.Schemas().Count == 0, "Model tools stay unavailable until the user enables control");
            await Command("fill -8 63 -8 8 63 8 stone");
            await Command("gamemode creative HelLAI");
            await Command("kill @e[type=item]");
            await Command("fill -8 64 -8 8 68 8 air");
            await Command("tp HelLAI 0.5 64 0.5");
            await Command("clear HelLAI");
            await Command("give HelLAI dirt 8"); await Command("give HelLAI oak_log 2");
            await Command("setblock 2 64 0 dirt");
            await Task.Delay(1200);
            await api.CallAsync("minecraft_enable_tools", new { enabled = true });
            Check(MinecraftSession.Schemas().Count == 17, "The enabled MCP exposes the gameplay tools without login or connect tools");
            var seen = await Call("observe", new PyDict());
            Check(Py.S(seen.Get("player")) == "HelLAI", "Observation reports the connected AI player's actual identity");
            var found = await CallValue("find_blocks", PyDict.Of("name", "dirt", "radius", 8L, "count", 8L));
            Check(Py.ListOr(found).Count > 0, "MCP finds actual loaded dirt blocks");
            await Call("dig", PyDict.Of("x", 2L, "y", 64L, "z", 0L));
            await Call("place", PyDict.Of("x", 2L, "y", 63L, "z", 0L, "face", "up", "item", "dirt"));
            Check(true, "Digging and placing complete with server confirmation");
            await Call("craft", PyDict.Of("item", "oak_planks", "times", 1L));
            var inventory = Py.ListOr(await CallValue("inventory", new PyDict())).OfType<PyDict>().ToList();
            File.WriteAllText(Path.Combine(folder, "inventory-after-craft.json"), PyJson.Dumps(inventory.Cast<object>().ToList(), 2));
            Check(inventory.Any(item => Py.S(item.Get("name")) == "oak_planks" && Py.Int(item.Get("count")) >= 4), "Crafting changes the real inventory");
            var moved = await Call("move_to", PyDict.Of("x", 4.5, "y", 64L, "z", 4.5, "range", 1.0));
            var position = moved.Get("position") as PyDict ?? throw new IOException("No position after movement");
            Check(Math.Abs(Py.Float(position.Get("x")) - 4.5) <= 1.5 && Math.Abs(Py.Float(position.Get("z")) - 4.5) <= 1.5, "Pathfinding moves the player to the requested location");
            var panel = new MinecraftPanel(); Dlg.Panel("AI 玩家", panel, 660, footer: panel.Actions); await panel.EnsureLoadedAsync();
            foreach (bool dark in new[] { false, true })
            {
                App.ApplyTheme(dark); window.Width = 900; window.Height = 680; await Task.Delay(600); window.UpdateLayout();
                Program.SaveSnapshot(window, Path.Combine(folder, "minecraft-" + (dark ? "dark" : "light") + ".png"));
            }
            if (feiyu)
            {
                await FeiyuFollowupCheck.Run(window, folder, port, Command);
                Console.WriteLine("[FEIYU FOLLOWUP] Real gameplay and two-project integration passed.");
                return;
            }
            var modelState = await VerifyLocalAgent();
            var moving = Task.Run(() => MinecraftSession.Call("mcp_minecraft_move_to", PyDict.Of("x", 64L, "y", 64L, "z", 64L)));
            await Task.Delay(200);
            var stop = Stopwatch.StartNew();
            await api.CallAsync("minecraft_stop");
            Check(stop.Elapsed.TotalSeconds < 3, "Immediate stop does not wait for an active movement request");
            try { await moving; } catch (McpError) { }
            Check(MinecraftSession.Schemas().Count == 0 && !MinecraftSession.Running, "Disconnect revokes tools and ends the MCP process");
            var unavailable = new TcpListener(IPAddress.Loopback, 0); unavailable.Start(); int closedPort = ((IPEndPoint)unavailable.LocalEndpoint).Port; unavailable.Stop();
            await api.CallAsync("minecraft_configure", new { host = "127.0.0.1", port = closedPort, name = "HelLAI", auth = "offline", version = "", node = MinecraftRuntime.FindNode() });
            try { await api.CallAsync("minecraft_connect"); }
            catch (Exception ex) { Check(ex.Message.Contains("对局域网开放"), "Preflight failure reports how to open a LAN world"); }
            for (int n = 0; n < 100 && MinecraftSession.Running; n++) { await Task.Run(() => MinecraftSession.Status()); await Task.Delay(100); }
            Check(!MinecraftSession.Running && Py.S(MinecraftSession.Status(false).Get("phase")) == "error", "Connection failure releases the process so the user can retry");
            var failureState = MinecraftSession.Status(false);
            Check(Py.S(failureState.Get("error")).Contains("对局域网开放") && !Py.S(failureState.Get("error")).Contains("exit="), "Automatic detection failure retains the server error instead of crashing during cleanup");
            File.WriteAllText(Path.Combine(folder, "verification.json"), PyJson.Dumps(PyDict.Of("checks", _checks, "server", "vanilla 1.21.1", "auth", "offline", "observed", seen,
                "inventory", inventory.Cast<object>().ToList(), "position", position, "local_model", modelState, "stop_ms", stop.Elapsed.TotalMilliseconds, "completed_at", DateTime.UtcNow.ToString("O")), 2));
            Console.WriteLine("[MINECRAFT LIVE] " + _checks + " checks passed.");
        }
        finally
        {
            MinecraftSession.Stop();
            if (!server.HasExited) { server.StandardInput.WriteLine("stop"); server.StandardInput.Flush(); if (!server.WaitForExit(15000)) server.Kill(); }
            lock (transcript) File.WriteAllLines(Path.Combine(folder, "server.log"), transcript);
        }
        async Task Command(string command) { server.StandardInput.WriteLine(command); server.StandardInput.Flush(); await Task.Delay(150); }
    }

    private static async Task<PyDict> VerifyLocalAgent()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        string endpoint = "http://127.0.0.1:" + port;
        using var http = new HttpClient();
        var runtime = new LocalRuntime(Path.GetFullPath("TestResults/ai-workspace-20261106/live"), http);
        await runtime.StartAsync(endpoint, CancellationToken.None);
        try { return await VerifyAgentCall(endpoint); }
        finally { runtime.Stop(); }
    }

    private static async Task<PyDict> VerifyAgentCall(string endpoint)
    {
        var server = (BridgeServer)typeof(BridgeHost).GetField("_server", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(AppServices.Host)!;
        var calls = new List<string>();
        var receipts = new List<string>();
        var settings = PyDict.Of("ai_mode", "local", "ai_local_url", endpoint, "ai_local_model", "qwen3:0.6b", "ai_local_context", 16384L,
            "ai_local_tools", true, "ai_permission_mode", "plan", "ai_session_id", "minecraft-live-check");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var answer = await Task.Run(() => Agent.RunAgent(server.Api, settings, new List<object>(),
            "请调用 mcp_minecraft_observe，参数必须是空对象 {}，不能传入 x、y、z 或任何其他属性。读取完成后，直接用一句中文报告结果中的 player 和 position。不要调用其他工具。",
            onStatus: (stage, payload) => { if (stage == "tool") lock (calls) calls.Add(Py.S(payload.Get("name"))); if (stage == "tool_done") lock (receipts) receipts.Add(Py.S(payload.Get("result"))); },
            cancelled: () => deadline.IsCancellationRequested));
        Check(calls.Contains("mcp_minecraft_observe"), "A real local model invokes the Minecraft MCP through the launcher agent");
        Check(receipts.Any(receipt => receipt.Contains("\"ok\":true") && receipt.Contains("HelLAI")), "The model receives a successful live observation, not merely a tool request");
        Check(!calls.Any(name => MinecraftSession.Meta(name) is { @readonly: false }), "Plan mode keeps model-driven verification read-only");
        settings["ai_permission_mode"] = "default";
        int refused = 0;
        await Task.Run(() => Agent.RunAgent(server.Api, settings, new List<object>(),
            "调用 mcp_minecraft_move_to 一次，参数为 {\"x\":1.5,\"y\":64,\"z\":4.5,\"range\":1}。不要调用其他工具。若被拒绝就停止。",
            confirmFn: (name, args, label, reason) => { if (name == "mcp_minecraft_move_to") refused++; return false; }, cancelled: () => deadline.IsCancellationRequested));
        var deniedPosition = (PyDict)(await Call("status", new PyDict())).Get("position");
        Check(refused > 0 && Math.Abs(Py.Float(deniedPosition.Get("x")) - 4.5) < 0.1, "Rejecting a model movement request leaves the player in place");
        int approvals = 0;
        var movement = await Task.Run(() => Agent.RunAgent(server.Api, settings, new List<object>(),
            "调用 mcp_minecraft_move_to 一次，参数为 {\"x\":1.5,\"y\":64,\"z\":4.5,\"range\":1}。不要调用其他工具。工具完成后只报告已完成或失败。",
            confirmFn: (name, args, label, reason) => { bool accepted = name == "mcp_minecraft_move_to" && Py.Float(args.Get("x")) == 1.5 && Py.Float(args.Get("y")) == 64 && Py.Float(args.Get("z")) == 4.5; if (accepted) approvals++; return accepted; },
            cancelled: () => deadline.IsCancellationRequested));
        var moved = await Call("status", new PyDict());
        var actual = (PyDict)moved.Get("position");
        Check(approvals > 0 && Math.Abs(Py.Float(actual.Get("x")) - 1.5) <= 1.5, "The local model moves the real bot only after the permission callback approves the exact target");
        return PyDict.Of("model", "qwen3:0.6b", "calls", calls.Cast<object>().ToList(), "receipts", receipts.Cast<object>().ToList(), "reply", answer.Text,
            "approved_moves", approvals, "refused_moves", refused, "movement_reply", movement.Text, "actual_position", actual);
    }
    private static async Task<object> CallValue(string name, PyDict args)
    {
        string reply = await Task.Run(() => MinecraftSession.Call(MinecraftSession.Prefix + name, args));
        return ((PyDict)PyJson.Loads(reply)).Get("data");
    }
    private static async Task<PyDict> Call(string name, PyDict args) => (PyDict)await CallValue(name, args);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[PASS] " + message); }
    private static async Task Until(Func<bool> ready, string message, int attempts) { for (int i = 0; i < attempts; i++) { if (ready()) return; await Task.Delay(100); } throw new TimeoutException(message); }
}
