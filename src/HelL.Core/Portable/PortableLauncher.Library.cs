#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Portable;

public sealed record LibraryInstance(string Name, string Description, string Group, bool Pinned, string Cover, string Folder)
{
    public string Detail => string.Join(" · ", new[] { Pinned ? "已固定" : "", Group, Description }.Where(value => value.Length > 0));
}

public sealed partial class PortableLauncher
{
    public IReadOnlyList<LibraryInstance> LibraryList()
    {
        var entries = InstancesList();
        var pins = HomePreferences.Pins(Config.CONFIG.Get("ui_pinned_instances"), entries.Select(row => row.Name));
        return entries.Select(entry =>
        {
            var game = RequireInstance(entry.Name);
            var meta = Utils.ReadJson(Path.Combine(game.path, ".instance.json")) as PyDict
                ?? throw new InvalidDataException($"实例 {entry.Name} 的元数据损坏，请从备份恢复后刷新。");
            string cover = InstanceArtwork.CustomCover(game);
            if (cover.Length == 0)
                cover = Py.S(InstanceMedia.Find(new[] { game.path }.Concat(entry.Versions.Select(id => VersionSettings.GameDir(game, id)))).Get("path"));
            return new LibraryInstance(entry.Name, entry.Description, Py.S(meta.Get("group")), pins.Contains(entry.Name), cover, game.path);
        }).OrderByDescending(entry => entry.Pinned).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public void RenameInstance(string name, string replacement)
    {
        var game = RequireInstance(name);
        replacement = replacement.Trim();
        if (replacement.Length == 0 || replacement.Length > 48 || Instances.SanitizeInstanceName(replacement) != replacement || replacement == Instances.RootName())
            throw new ArgumentException("请输入有效的实例名称（1–48 个字符，不含路径符号）。");
        if (replacement != name) game.Rename(replacement);
    }

    public void GroupInstance(string name, string group) => InstanceGroups.Assign(RequireInstance(name).name, group);
    public void PinInstance(string name, bool pinned) => HomePreferences.PinMany(new[] { RequireInstance(name).name }, pinned);
    public void SetInstanceCover(string name, string file) => InstanceArtwork.SetCover(RequireInstance(name), file);
    public void ResetInstanceCover(string name) => InstanceArtwork.ResetCover(RequireInstance(name));
    public string InstanceFolder(string name) => RequireInstance(name).path;
    public void SetReferenceVersion(string name, string version)
    {
        RequireVersionId(version);
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"\A1\.\d+(?:\.\d+)?\z"))
            throw new ArgumentException("请输入正式 Minecraft 版本，例如 1.21.1。此设置仅用于资源兼容性筛选。");
        RequireInstance(name).SetMeta("portable_reference_version", version);
    }
    public string ReferenceVersion(string name) => Py.S(RequireInstance(name).Meta().Get("portable_reference_version"));
}
