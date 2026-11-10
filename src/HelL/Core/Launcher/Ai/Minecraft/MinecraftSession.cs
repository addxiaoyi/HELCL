using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftSession
{
    public const string Prefix = "mcp_minecraft_";
    private static readonly object Gate = new object();
    private static readonly HashSet<string> ReadOnly = new HashSet<string> { "status", "observe", "inventory", "find_blocks", "stop", "disconnect" };
    private static readonly HashSet<string> Allowed = new HashSet<string>(ReadOnly.Concat(new[] { "move_to", "look_at", "dig", "place", "equip", "craft", "consume", "interact", "follow_player", "collect_wood", "pickup_items" }));
    private static McpClient? _client;
    private static List<object> _schemas = new List<object>();
    private static PyDict _state = PyDict.Of("phase", "disconnected", "connected", false);
    private static bool _toolsEnabled;
    private static bool _starting;
    private static bool _maintenance;
    private static bool _feiyu;
    private static int _activeCalls;
    private static long _generation;
    private static CancellationTokenSource? _connectionCancel;
    private static Timer? _monitor;
    private static int _polling;
    private static PyDict? _lastServer;
    private static string _historyWarning = "";
    private static string OptionsFile => Path.Combine(Utils.ROOT, "ai", "minecraft", "connection.json");

    public static PyDict Options() => Utils.ReadJson(OptionsFile) as PyDict ?? PyDict.Of("host", "127.0.0.1", "port", 25565L, "name", "HelLAI", "auth", "offline", "version", "", "node", MinecraftRuntime.FindNode());
    public static void SaveOptions(PyDict options)
    {
        lock (Gate)
        {
            if (Running) throw new InvalidOperationException("请先断开 AI 玩家，再修改连接设置。");
            Utils.WriteJson(OptionsFile, options); _lastServer = null;
            _state = PyDict.Of("phase", "disconnected", "connected", false);
        }
    }
    public static bool Enabled { get { lock (Gate) return _toolsEnabled && _client != null; } }
    public static bool Running { get { lock (Gate) return _client != null || _starting || _maintenance; } }
    public static void Repair(string node, TaskContext context)
    {
        lock (Gate)
        {
            if (Running) throw new InvalidOperationException("请先断开 AI 玩家，再修复组件。");
            _maintenance = true;
        }
        try
        {
            MinecraftRuntime.Install(node, context);
            lock (Gate)
            {
                var options = Options().Copy(); options["node"] = node;
                Utils.WriteJson(OptionsFile, options);
                _state = PyDict.Of("phase", "disconnected", "connected", false);
            }
        }
        finally { lock (Gate) _maintenance = false; }
    }
    public static bool ControlledByFeiyu { get { lock (Gate) return _feiyu; } }
    public static List<object> Schemas() { lock (Gate) return Enabled ? new List<object>(_schemas) : new List<object>(); }
    public static ToolMetaInfo? Meta(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        string tool = name.Substring(Prefix.Length);
        if (!Allowed.Contains(tool)) return null;
        return new ToolMetaInfo(name, ReadOnly.Contains(tool), ReadOnly.Contains(tool) ? "read" : tool is "dig" or "collect_wood" ? "delete" : "game_action", tool is "dig" or "place" or "interact" or "collect_wood" ? "high" : "medium", false);
    }
    public static string Label(string name, PyDict args)
    {
        string tool = name.Substring(Prefix.Length);
        string label = tool switch { "observe" => "观察 AI 玩家周围", "status" => "读取 AI 玩家状态", "inventory" => "查看 AI 玩家背包", "find_blocks" => "寻找方块",
            "move_to" => "移动 AI 玩家", "look_at" => "调整 AI 玩家视角", "dig" => "挖掘方块", "place" => "放置方块", "equip" => "装备物品", "craft" => "合成物品",
            "consume" => "食用物品", "interact" => "使用方块", "follow_player" => "跟随玩家", "collect_wood" => "采集天然原木", "pickup_items" => "拾取掉落物", _ => "停止并断开 AI 玩家" };
        string target = args.ContainsKey("x") ? Py.S(args.Get("x")) + ", " + Py.S(args.Get("y")) + ", " + Py.S(args.Get("z")) : Py.S(args.Get("item") ?? args.Get("name"));
        return I18nCore.Tr(label) + (target.Length > 0 ? " · " + target : "");
    }
    public static void SetToolsEnabled(bool enabled)
    {
        if (!enabled) { Stop(); return; }
        lock (Gate)
        {
            if (_feiyu) throw new InvalidOperationException("大肥鱼正在接管，请先停止联动再允许对话控制。");
            if (_client == null || !Py.Truthy(_state.Get("connected"))) throw new InvalidOperationException("AI 玩家尚未进入世界。");
            _toolsEnabled = true;
        }
    }

    public static PyDict Start(PyDict options)
    {
        options = options.Copy();
        var cancel = new CancellationTokenSource();
        McpClient? client = null;
        long generation;
        lock (Gate)
        {
            if (Running) { cancel.Dispose(); throw new InvalidOperationException("已有连接或检测正在进行，请先停止。"); }
            generation = ++_generation; _connectionCancel = cancel; _starting = true;
            _state = PyDict.Of("phase", "checking", "connected", false); _toolsEnabled = false; _feiyu = false; _lastServer = null;
        }
        try
        {
            var server = MinecraftServerProbe.CheckAsync(Py.S(options.Get("host")), (int)Py.Int(options.Get("port")), Py.S(options.Get("version")), cancel.Token).GetAwaiter().GetResult();
            lock (Gate) { cancel.Token.ThrowIfCancellationRequested(); if (_generation != generation) throw new OperationCanceledException(); _lastServer = server; }
            if (!Py.Truthy(server.Get("can_connect"))) throw new MinecraftConnectionError(Py.S(server.Get("code")), Py.S(server.Get("recovery")), Py.S(server.Get("message")));
            var runtime = MinecraftRuntime.Diagnose(Py.S(options.Get("node")));
            if (!Py.Truthy(runtime.Get("ready"))) throw new MinecraftConnectionError(Py.S(runtime.Get("code")), Py.S(runtime.Get("recovery")), Py.S(runtime.Get("message")));
            var env = PyDict.Of("HELL_MC_HOST", options.Get("host"), "HELL_MC_PORT", options.Get("port"), "HELL_MC_NAME", options.Get("name"),
                "HELL_MC_AUTH", options.Get("auth"), "HELL_MC_VERSION", server.Get("compatible_version"), "HELL_MC_PROFILE", MinecraftRuntime.Profiles);
            client = new McpClient("minecraft", Py.S(runtime.Get("node")), Py.L(MinecraftRuntime.Script), env);
            lock (Gate)
            {
                cancel.Token.ThrowIfCancellationRequested(); if (_generation != generation) throw new OperationCanceledException();
                _client = client; _state = PyDict.Of("phase", "connecting", "connected", false);
            }
            client.Connect();
            var tools = Mcp.McpToolSchemas(new List<McpClient> { client }).OfType<PyDict>()
                .Where(schema => Meta(Py.S((schema.Get("function") as PyDict)?.Get("name"))) != null).Cast<object>().ToList();
            var state = Decode(client.CallTool("connect", new PyDict()));
            lock (Gate)
            {
                if (!ReferenceEquals(client, _client) || _generation != generation) throw new OperationCanceledException("连接已停止。");
                _schemas = tools; _state = state; _starting = false;
                _monitor = new Timer(_ => Monitor(), null, 2000, 2000);
            }
            return state;
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                if (_generation == generation)
                {
                    _client = null; _starting = false; _schemas.Clear(); _toolsEnabled = false; _feiyu = false;
                    _state = Failure(ex);
                }
            }
            client?.Abort(); throw;
        }
        finally { lock (Gate) { if (ReferenceEquals(_connectionCancel, cancel)) _connectionCancel = null; } cancel.Dispose(); }
    }

    private static void Monitor()
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        try { if (Running) Status(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Minecraft connection monitor: " + ex.Message); }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private static PyDict Failure(Exception error)
    {
        string code = error is MinecraftConnectionError connection ? connection.Code : "connection_error";
        string recovery = error is MinecraftConnectionError failure ? failure.Recovery : error.Message.Contains("组件") ? "repair" : "retry";
        return PyDict.Of("phase", "error", "connected", false, "error", error.Message, "error_code", code, "recovery", recovery);
    }

    public static PyDict Status(bool refresh = true)
    {
        McpClient? client; lock (Gate) client = _starting ? null : _client;
        if (refresh && client != null)
        {
            try
            {
                var latest = Decode(client.CallTool("connection_info", new PyDict()));
                bool ended = Py.S(latest.Get("phase")) is "error" or "dead" or "disconnected";
                lock (Gate)
                {
                    if (ReferenceEquals(client, _client))
                    {
                        _state = latest;
                        if (ended) ReleaseConnection();
                    }
                }
                if (ended) client.Abort();
            }
            catch (Exception ex)
            {
                lock (Gate) { if (ReferenceEquals(client, _client)) { _state = Failure(ex); ReleaseConnection(); } }
                client.Abort();
            }
        }
        lock (Gate)
        {
            var state = _state.Copy(); state["tools_enabled"] = _toolsEnabled; state["runtime_ready"] = MinecraftRuntime.Ready;
            try { state["history"] = MinecraftActionHistory.Snapshot(); }
            catch (Exception ex) { state["history_warning"] = "无法读取动作记录，请检查数据目录。"; System.Diagnostics.Trace.TraceWarning("Minecraft action history: " + ex.Message); }
            if (_historyWarning.Length > 0) state["history_warning"] = _historyWarning;
            state["server"] = _lastServer;
            state["options"] = Options(); state["controller"] = _feiyu ? "feiyu" : _toolsEnabled ? "launcher" : "none"; return state;
        }
    }

    // Called under Gate so a late status reply cannot close a newer connection's link.
    private static void ReleaseConnection()
    {
        _client = null; _schemas.Clear(); _toolsEnabled = false; _feiyu = false;
        _monitor?.Dispose(); _monitor = null; FeiyuLink.Close();
        InterruptHistory();
    }

    public static string Call(string name, PyDict args) => CallOwned(name, args, false);
    public static string FeiyuCall(string name, PyDict args) => CallOwned(name, args, true);
    public static string RetryAction(string id)
    {
        try { return CallOwned("", new PyDict(), false, id); }
        catch (McpError error) { throw new InvalidOperationException(ActionError(error), error); }
    }
    private static string CallOwned(string name, PyDict args, bool feiyu, string retryOf = "")
    {
        McpClient client; long generation; string receipt = "";
        lock (Gate)
        {
            if (_client == null || (feiyu ? !_feiyu : !Enabled || _feiyu)) throw new McpError("当前调用方没有游戏控制权，请在连接面板授权。");
            if (retryOf.Length > 0)
            {
                if (_activeCalls > 0) throw new InvalidOperationException("请等待当前动作结束后再重试。");
                var entry = MinecraftActionHistory.Retry(retryOf, Options());
                name = Prefix + Py.S(entry.Get("action")); args = entry.Get("arguments") as PyDict ?? new PyDict();
            }
            if (Meta(name) == null) throw new McpError("未知的 Minecraft 工具。");
            if (!ReadOnly.Contains(name.Substring(Prefix.Length))) receipt = MinecraftActionHistory.Begin(name.Substring(Prefix.Length), args, Options(), retryOf);
            client = _client; generation = _generation; _activeCalls++;
        }
        try
        {
            if (name == Prefix + "disconnect" || name == Prefix + "stop") { Stop(); return PyJson.Dumps(PyDict.Of("ok", true, "data", PyDict.Of("connected", false))); }
            string response = client.CallTool(name.Substring(Prefix.Length), args);
            lock (Gate)
            {
                if (_generation != generation || !ReferenceEquals(client, _client)) throw new McpError("连接已停止，请重新观察动作结果。");
                if (receipt.Length > 0) FinishReceipt(receipt, "completed", "动作已完成。");
            }
            return response;
        }
        catch (Exception ex)
        {
            if (receipt.Length > 0) { lock (Gate) FinishReceipt(receipt, generation == _generation && ReferenceEquals(client, _client) ? "failed" : "stopped", ActionError(ex)); }
            throw;
        }
        finally { lock (Gate) _activeCalls--; }
    }
    private static void FinishReceipt(string id, string state, string detail)
    {
        try { MinecraftActionHistory.Finish(id, state, detail); _historyWarning = ""; }
        catch (Exception ex) { _historyWarning = "动作记录无法保存，请检查数据目录空间和权限。"; System.Diagnostics.Trace.TraceWarning("Minecraft receipt save: " + ex.Message); }
    }
    private static string ActionError(Exception error)
    {
        if (error.Message.StartsWith("{", StringComparison.Ordinal))
        {
            try { if (PyJson.Loads(error.Message) is PyDict reply && reply.Get("error") is string detail) return detail; }
            catch (PyException) { /* Non-JSON process errors keep their original explanation. */ }
        }
        return error.Message;
    }
    private static void InterruptHistory()
    {
        try { MinecraftActionHistory.Interrupt(); }
        catch (Exception ex) { _historyWarning = "动作记录无法保存，请检查数据目录空间和权限。"; System.Diagnostics.Trace.TraceWarning("Minecraft receipt interruption: " + ex.Message); }
    }
    public static void Stop()
    {
        McpClient? client;
        CancellationTokenSource? cancel;
        lock (Gate) { ++_generation; client = _client; _client = null; cancel = _connectionCancel; _connectionCancel = null; _monitor?.Dispose(); _monitor = null; _starting = false; _schemas.Clear(); _toolsEnabled = false; _feiyu = false; _state = PyDict.Of("phase", "disconnected", "connected", false); FeiyuLink.Close(); InterruptHistory(); }
        try { cancel?.Cancel(); } catch (ObjectDisposedException) { }
        client?.Abort();
    }
    public static void AllowFeiyu()
    {
        if (!MinecraftExtensions.FeiyuEnabled) throw new InvalidOperationException("请先在拓展中开启大肥鱼联动。");
        lock (Gate)
        {
            if (_client == null || !Py.Truthy(_state.Get("connected"))) throw new InvalidOperationException("请先连接 AI 玩家。");
            if (_activeCalls > 0) throw new InvalidOperationException("请等待当前动作结束再切换控制方。");
            _toolsEnabled = false; _feiyu = true;
            try { FeiyuLink.Open(); }
            catch { _feiyu = false; throw; }
        }
    }
    public static PyDict SharedStatus()
    {
        var state = Status(); state.Remove("login"); state.Remove("options"); state["local_model"] = MinecraftModels.Status(); return state;
    }
    private static PyDict Decode(string json)
    {
        var reply = PyJson.Loads(json) as PyDict ?? throw new McpError("游戏组件响应无效。");
        return reply.Get("data") as PyDict ?? throw new McpError("游戏组件没有返回连接状态。");
    }
}
