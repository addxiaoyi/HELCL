using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftActionHistory
{
    private static readonly object Gate = new object();
    private static string _file = "";
    private static List<PyDict> _entries = new List<PyDict>();
    private const int Limit = 100;

    public static string Begin(string action, PyDict arguments, PyDict target, string retryOf = "")
    {
        lock (Gate)
        {
            Load();
            var fields = new HashSet<string>((action switch
            {
                "move_to" => "x y z range", "look_at" or "dig" or "interact" => "x y z", "place" => "x y z face item",
                "equip" or "consume" => "item", "craft" => "item times", "follow_player" => "player seconds distance",
                "collect_wood" => "name count radius", "pickup_items" => "radius limit", _ => ""
            }).Split(' '));
            var stored = new PyDict();
            foreach (var field in arguments.Where(field => fields.Contains(field.Key))) stored[field.Key] = field.Value;
            var entry = PyDict.Of("id", Guid.NewGuid().ToString("N"), "action", action, "arguments", stored, "state", "running",
                "at", DateTime.UtcNow.ToString("O"), "detail", "", "retry_of", retryOf,
                "target", PyDict.Of("host", target.Get("host"), "port", target.Get("port"), "name", target.Get("name"), "auth", target.Get("auth")));
            _entries.Add(entry); if (_entries.Count > Limit) _entries.RemoveAt(0);
            Save(); return Py.S(entry.Get("id"));
        }
    }

    public static void Finish(string id, string state, string detail)
    {
        lock (Gate)
        {
            Load(); var entry = _entries.FirstOrDefault(row => Py.S(row.Get("id")) == id);
            if (entry == null || Py.S(entry.Get("state")) != "running") return;
            Complete(entry, state, detail); Save();
        }
    }

    public static void Interrupt()
    {
        lock (Gate)
        {
            Load(); var active = _entries.Where(row => Py.S(row.Get("state")) == "running").ToList();
            foreach (var entry in active) Complete(entry, "stopped", "连接已停止。操作可能已部分完成，请先观察世界再安排后续任务。");
            if (active.Count > 0) Save();
        }
    }

    public static List<object> Snapshot()
    {
        lock (Gate) { Load(); return _entries.Select(entry => (object)entry.Copy()).ToList(); }
    }

    public static PyDict Retry(string id, PyDict target)
    {
        lock (Gate)
        {
            Load(); var entry = _entries.FirstOrDefault(row => Py.S(row.Get("id")) == id);
            if (entry == null || !Py.Truthy(entry.Get("retryable"))) throw new InvalidOperationException("此动作不能直接重试，请先观察世界后重新安排任务。");
            var previous = entry.Get("target") as PyDict;
            if (previous == null || new[] { "host", "port", "name", "auth" }.Any(key => Py.S(previous.Get(key)) != Py.S(target.Get(key))))
                throw new InvalidOperationException("当前世界或账号已改变，不能重放旧动作。");
            return entry.Copy();
        }
    }

    private static void Complete(PyDict entry, string state, string detail)
    {
        entry["state"] = state; entry["detail"] = Py.Slice(detail, null, 500); entry["ended_at"] = DateTime.UtcNow.ToString("O");
        entry["duration_ms"] = DateTimeOffset.TryParse(Py.S(entry.Get("at")), out var start) ? Math.Max(0, (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds) : 0L;
        entry["retryable"] = state == "failed" && Py.S(entry.Get("action")) is "move_to" or "look_at" or "follow_player";
    }

    private static void Load()
    {
        string path = Path.Combine(Utils.ROOT, "ai", "minecraft", "actions.json");
        if (_file == path) return;
        var entries = new List<PyDict>();
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 512 * 1024) throw new IOException("动作记录文件过大，请先备份并清理 actions.json。");
            entries = Py.ListOr(PyJson.Loads(File.ReadAllText(path))).OfType<PyDict>().Reverse().Take(Limit).Reverse().ToList();
            foreach (var entry in entries.Where(entry => Py.S(entry.Get("state")) == "running"))
                Complete(entry, "stopped", "上次运行已中断。实际结果需重新观察，不会自动重放动作。");
        }
        _entries = entries; _file = path;
    }
    private static void Save() => Utils.WriteJson(_file, _entries.Cast<object>().ToList());
}
