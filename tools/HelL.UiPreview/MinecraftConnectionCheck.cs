using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HelL;
using HelL.Core;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Minecraft;
using HelL.Models;
using HelL.Pages.Ai;
using HelL.Services;

internal static class MinecraftConnectionCheck
{
    private static readonly List<object> Checks = new List<object>();

    internal static async Task Run(MainWindow window, string folder, int port, Func<string, Task> command)
    {
        var api = AppServices.Client;
        var probe = await api.CallAsync("minecraft_probe", new { host = "127.0.0.1", port });
        Check(probe.GetProperty("can_connect").GetBoolean() && probe.GetProperty("protocol").GetInt32() == 767, "Real Java status reports protocol 767 and compatibility before login");
        var discovered = await api.CallAsync("minecraft_discover");
        Check(discovered.GetProperty("servers").EnumerateArray().Any(row => row.GetProperty("port").GetInt32() == port), "Windows Java listener discovery finds the randomly allocated server port");

        var scan = MinecraftLanDiscovery.DiscoverAsync();
        using (var sender = new UdpClient(AddressFamily.InterNetwork))
        {
            sender.MulticastLoopback = true;
            sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.Loopback.GetAddressBytes());
            byte[] announcement = Encoding.UTF8.GetBytes("[MOTD]隔离世界[/MOTD][AD]" + port + "[/AD]");
            for (int n = 0; n < 10; n++) { await sender.SendAsync(announcement, announcement.Length, new IPEndPoint(IPAddress.Parse("224.0.2.60"), 4445)); await Task.Delay(120); }
        }
        var broadcast = await scan;
        var matches = Py.ListOr(broadcast.Get("servers")).OfType<PyDict>().Where(row => Py.Int(row.Get("port")) == port && Py.S(row.Get("host")) == "127.0.0.1").ToList();
        Check(matches.Count == 1 && Py.S(matches[0].Get("source")) == "lan", "Repeated real multicast advertisements are discovered and deduplicated");

        var panel = new MinecraftPanel(); Dlg.Panel("AI 玩家", panel, 660, footer: panel.Actions); await panel.EnsureLoadedAsync();
        var host = Field<TextBox>(panel, "_host"); var targetPort = Field<TextBox>(panel, "_port"); var version = Field<TextBox>(panel, "_version");
        Click(panel, "查找局域网");
        await Until(() => !Field<bool>(panel, "_working") && Field<SPanel>(panel, "_servers").Children.Count > 0, "UI discovery completes");
        Click(panel, "使用此世界");
        await Idle(panel);
        Check(targetPort.Text == port.ToString() && Field<TextBlock>(panel, "_serverInfo").Text.Contains("版本兼容"), "Choosing a discovered world fills its real address and rechecks the server");
        version.Text = "1.20.4"; Click(panel, "检测服务器"); await Idle(panel);
        Check(Field<string>(panel, "_recovery") == "auto_version", "A mismatched manual version exposes automatic version recovery");
        Field<Button>(panel, "_fix").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(panel);
        Check(version.Text == "" && Field<TextBlock>(panel, "_serverInfo").Text.Contains("版本兼容"), "Automatic version recovery clears the override and rechecks compatibility");
        targetPort.Text = "1";
        Check(Field<TextBlock>(panel, "_serverInfo").Visibility == Visibility.Collapsed && Field<Button>(panel, "_fix").Visibility == Visibility.Collapsed, "Editing the target invalidates previous probe results and recovery actions");
        host.Text = "127.0.0.1"; targetPort.Text = port.ToString();

        await VerifyRepair(folder, panel);
        Click(panel, "连接"); host.Text = "127.0.0.1"; targetPort.Text = port.ToString();
        Field<Button>(panel, "_primary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Field<bool>(panel, "_connected"), "AI player connects via the panel");
        Check(MinecraftSession.Schemas().Count == 0, "Connecting never grants game control automatically");
        Field<Button>(panel, "_primary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle(panel);
        var observation = await Task.Run(() => MinecraftSession.Call("mcp_minecraft_observe", new PyDict()));
        Check(observation.Contains("HelLAI"), "Authorized observation returns the real player's identity");
        await command("fill -4 63 -4 36 63 36 stone");
        await command("fill -4 64 -4 36 66 36 air");
        await command("tp HelLAI 0.5 64 0.5");
        await Task.Delay(500);
        try { await Task.Run(() => MinecraftSession.Call("mcp_minecraft_move_to", PyDict.Of("x", 200L, "y", 64L, "z", 0L))); }
        catch (McpError) { }
        var failedAction = MinecraftActionHistory.Snapshot().OfType<PyDict>().Last();
        Check(Py.S(failedAction.Get("state")) == "failed" && Py.Truthy(failedAction.Get("retryable")), "A bounded movement failure produces a retryable receipt");
        await Task.Delay(2200); Click(panel, "世界状态"); Click(panel, "动作记录");
        Click(panel, "确认后重试");
        await Until(() => Walk(Dlg.Host!).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "执行"), "Retry confirmation shows the exact action arguments");
        Click(Dlg.Host!, "执行");
        await Until(() => MinecraftActionHistory.Snapshot().OfType<PyDict>().Any(row => Py.S(row.Get("retry_of")) == Py.S(failedAction.Get("id"))), "Confirmed retry reaches the game component");
        await Idle(panel);
        Check(!Field<TextBlock>(panel, "_error").Text.Contains("\"ok\"") && Field<TextBlock>(panel, "_error").Text.Contains("128"), "Retry errors display readable game guidance instead of raw JSON");
        Program.SaveSnapshot(window, Path.Combine(folder, "action-history.png"));
        Check(MinecraftActionHistory.Snapshot().OfType<PyDict>().Any(row => Py.S(row.Get("retry_of")) == Py.S(failedAction.Get("id"))), "Explicit retry creates a new receipt linked to the original action");
        var moving = Task.Run(() => MinecraftSession.Call("mcp_minecraft_move_to", PyDict.Of("x", 30L, "y", 64L, "z", 30L)));
        await Task.Delay(200); MinecraftSession.Stop();
        try { await moving; } catch (McpError) { }
        Check(Py.S(MinecraftActionHistory.Snapshot().OfType<PyDict>().Last().Get("state")) == "stopped", "Immediate disconnect records an interrupted live action");
        await api.CallAsync("minecraft_connect");
        await Until(() => Py.Truthy(MinecraftSession.Status(false).Get("connected")), "Reconnect after action interruption");
        Check(MinecraftActionHistory.Snapshot().Count >= 3 && !MinecraftSession.Enabled, "Reconnection preserves receipts but does not restore control");
        await api.CallAsync("minecraft_enable_tools", new { enabled = true });
        foreach (bool dark in new[] { false, true })
        {
            App.ApplyTheme(dark); window.Width = 900; window.Height = 620; window.UpdateLayout(); await Task.Delay(250);
            Program.SaveSnapshot(window, Path.Combine(folder, "connection-" + (dark ? "dark" : "light") + ".png"));
        }
        Field<Button>(panel, "_stop").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => !MinecraftSession.Running && !Field<bool>(panel, "_connected"), "Panel disconnect completes");
        Check(MinecraftSession.Schemas().Count == 0 && !MinecraftSession.ControlledByFeiyu, "Disconnect revokes tools and the optional extension's control");
        Field<Button>(panel, "_primary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(() => Field<bool>(panel, "_connected"), "Panel reconnect completes");
        Check(MinecraftSession.Schemas().Count == 0, "Reconnect requires a fresh explicit control grant");
        await api.CallAsync("minecraft_enable_tools", new { enabled = true });
        // Close the UI first; cleanup must come from the session monitor, not its polling timer.
        panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        await command("kick HelLAI isolated-monitor-check");
        await Until(() => !MinecraftSession.Running, "Background monitor releases a kicked player with the panel closed");
        Check(!MinecraftSession.Enabled && MinecraftSession.Schemas().Count == 0, "Unexpected disconnection clears game control without an open panel");
        await api.CallAsync("minecraft_connect");
        await Until(() => Py.Truthy(MinecraftSession.Status(false).Get("connected")), "Reconnect after server kick");
        Check(!MinecraftSession.Enabled, "Recovery from an unexpected disconnect also requires a new grant");
        MinecraftSession.Stop();
        File.WriteAllText(Path.Combine(folder, "connection-verification.json"), PyJson.Dumps(PyDict.Of("checks", Checks, "server", "vanilla 1.21.1", "port", port, "discovered", PyJson.Loads(discovered.GetRawText()), "broadcast", broadcast, "runtime", MinecraftRuntime.DirectoryPath, "completed_at", DateTime.UtcNow.ToString("O")), 2));
        Console.WriteLine("[CONNECTION LIVE] " + Checks.Count + " checks passed.");
    }

    private static async Task VerifyRepair(string folder, MinecraftPanel panel)
    {
        var api = AppServices.Client;
        string node = MinecraftRuntime.FindNode();
        string fake = Path.Combine(folder, "failing-node"); Directory.CreateDirectory(Path.Combine(fake, "node_modules", "npm", "bin"));
        string fakeNode = Path.Combine(fake, "node.exe"); File.Copy(node, fakeNode, true);
        string npm = Path.Combine(fake, "node_modules", "npm", "bin", "npm-cli.js");
        File.WriteAllText(npm, "process.exit(9);");
        string original = File.ReadAllText(MinecraftRuntime.Script);
        string task = await api.StartTaskAsync("minecraft_install", new { node = fakeNode });
        var failed = await api.CallAsync<OpResult>("wait_task", new { task_id = task, timeout = 30 });
        Check(failed?.Ok == false && failed.Message.Contains("9") && MinecraftRuntime.HealthyFiles() && File.ReadAllText(MinecraftRuntime.Script) == original, "A failed npm repair leaves the previous working component intact");

        File.WriteAllText(npm, "setTimeout(() => process.exit(0), 30000);");
        task = await api.StartTaskAsync("minecraft_install", new { node = fakeNode });
        await Until(() => MinecraftSession.Running, "Maintenance reservation begins");
        bool refused = false;
        try { await api.CallAsync("minecraft_connect"); } catch (Exception ex) { refused = ex.Message.Contains("正在进行"); }
        Check(refused, "Connection cannot race an in-progress component replacement");
        await api.CallAsync("cancel_task", new { task_id = task });
        var cancelled = await api.CallAsync<OpResult>("wait_task", new { task_id = task, timeout = 30 });
        Check(cancelled?.Ok == false && !MinecraftSession.Running && MinecraftRuntime.HealthyFiles(), "Cancelling repair releases maintenance and retains the usable component");

        File.AppendAllText(MinecraftRuntime.Script, "\n// isolated corruption fixture");
        Check(!MinecraftRuntime.HealthyFiles(), "Modified installed payload is detected even when its ready marker exists");
        Click(panel, "高级"); Click(panel, "检测游戏组件"); await Idle(panel);
        Check(!Field<bool>(panel, "_installed") && Field<TextBlock>(panel, "_runtime").Text.Contains("损坏"), "Component diagnosis survives the following status refresh");
        Field<TextBox>(panel, "_host").Text = "https://invalid-host"; Field<TextBox>(panel, "_port").Text = "bad-port";
        Click(panel, "修复游戏组件"); await Idle(panel, 1800);
        Check(Field<bool>(panel, "_installed") && MinecraftRuntime.HealthyFiles(), "One-click repair succeeds independently of invalid connection fields");
        Check(File.ReadAllText(MinecraftRuntime.Script) == original, "Repair restores the exact embedded payload");
    }

    private static async Task Idle(MinecraftPanel panel, int attempts = 400) => await Until(() => !Field<bool>(panel, "_working"), "Panel operation finishes", attempts);
    private static async Task Until(Func<bool> condition, string message, int attempts = 400)
    { for (int i = 0; i < attempts; i++) { if (condition()) return; await Task.Delay(100); } throw new TimeoutException(message); }
    private static T Field<T>(object panel, string field) => (T)panel.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
    private static void Click(DependencyObject root, string label)
    {
        Walk(root).OfType<Button>().First(button => button.IsVisible && (AutomationProperties.GetName(button) == label || button.Content as string == label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        (root as FrameworkElement)?.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { for (int n = 0; n < VisualTreeHelper.GetChildrenCount(root); n++) { var child = VisualTreeHelper.GetChild(root, n); yield return child; foreach (var nested in Walk(child)) yield return nested; } }
    private static void Check(bool passed, string message)
    { if (!passed) throw new InvalidOperationException(message); Checks.Add(message); Console.WriteLine("[PASS] " + message); }
}
