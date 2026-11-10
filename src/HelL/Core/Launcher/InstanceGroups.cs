using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HelL.Core.Launcher;

public static class InstanceGroups
{
    private static readonly object Gate = new();
    public static string Normalize(string group)
    {
        string name = (group ?? "").Trim();
        if (name.Length > 32 || name.Any(char.IsControl)) throw new ArgumentException("分组名称最多 32 个字符，不能包含换行。留空可移出分组。");
        return name;
    }

    public static void Assign(string instance, string group) => AssignMany(new[] { instance }, group);

    public static string[] ValidateSelection(IEnumerable<string> instances)
    {
        var selected = instances?.Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        if (selected.Length == 0 || selected.Length > 200) throw new ArgumentException("请选择 1–200 个实例。");
        var installed = new HashSet<string>(Instances.ListInstances().Select(Py.S), StringComparer.Ordinal);
        if (selected.Any(name => !installed.Contains(name))) throw new ArgumentException("部分实例已不存在，请刷新后再试；尚未修改任何实例。");
        return selected;
    }

    public static void AssignMany(IEnumerable<string> instances, string group)
    {
        string name = Normalize(group);
        lock (Gate) WriteGroups(ValidateSelection(instances), name);
    }

    public static int Rename(string group, string replacement)
    {
        string original = Normalize(group), renamed = Normalize(replacement);
        if (original.Length == 0 || renamed.Length == 0) throw new ArgumentException("分组名称不能为空。");
        lock (Gate)
        {
            var games = Instances.ListInstances().Select(Py.S).Select(name => new Instance(name)).ToArray();
            var metadata = games.Select(game => (Game: game, Meta: ReadMeta(Path.Combine(game.path, ".instance.json")))).ToArray();
            bool sameGroup = string.Equals(original, renamed, StringComparison.OrdinalIgnoreCase);
            if (!sameGroup && metadata.Any(row => string.Equals(Py.S(row.Meta.Get("group")), renamed, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("已存在同名分组。请使用批量分组将实例移入该组。");
            var members = metadata.Where(row => string.Equals(Py.S(row.Meta.Get("group")), original, StringComparison.OrdinalIgnoreCase)).Select(row => row.Game.name).ToArray();
            if (members.Length == 0) throw new ArgumentException("此分组已不存在，请刷新后再试。");
            WriteGroups(members, renamed); return members.Length;
        }
    }

    private static PyDict ReadMeta(string path)
    {
        if (!File.Exists(path)) return new PyDict();
        return Utils.ReadJson(path) as PyDict ?? throw new InvalidDataException("实例配置已损坏，无法保存分组；原文件已保留。");
    }

    private static void WriteGroups(IEnumerable<string> members, string group)
    {
        var edits = members.Select(name =>
        {
            string path = Path.Combine(new Instance(name).path, ".instance.json");
            return (Path: path, Meta: ReadMeta(path), Before: File.Exists(path) ? File.ReadAllBytes(path) : null);
        }).ToArray();
        var applied = new List<(string Path, byte[]? Before)>();
        try
        {
            foreach (var edit in edits)
            {
                edit.Meta["group"] = group;
                Utils.WriteJson(edit.Path, edit.Meta);
                applied.Add((edit.Path, edit.Before));
            }
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            foreach (var edit in applied.AsEnumerable().Reverse())
            {
                try { if (edit.Before == null) File.Delete(edit.Path); else File.WriteAllBytes(edit.Path, edit.Before); }
                catch (Exception rollback) { failures.Add(rollback); }
            }
            Guard.WriteLog("instance-group-batch", new AggregateException(failures).ToString());
            if (failures.Count > 1) throw new IOException("分组保存失败，部分实例未能恢复。请刷新后检查分组；详细信息已写入日志。", new AggregateException(failures));
            throw new IOException("分组保存失败，已恢复此前分组，请稍后重试。", error);
        }
    }
}
