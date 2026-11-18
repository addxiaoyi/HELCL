using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object dashboard_instances(int limit = 6)
    {
        var installed = Instances.ListInstances().Select(Py.S).ToDictionary(name => name, name => new Instance(name).InstalledIds().Select(Py.S).ToList(), StringComparer.Ordinal);
        var pins = HomePreferences.Pins(Config.CONFIG.Get("ui_pinned_instances"), installed.Keys);
        var rows = DashboardLibrary.Select(Playtime.GetAllPlaytime() as PyDict ?? new PyDict(), installed, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), limit, pins);
        foreach (var row in rows)
        {
            var visual = instance_visual(row.Instance, row.Version) as PyDict;
            row.Cover = Py.S(visual?.Get("path")); row.CoverKind = Py.S(visual?.Get("kind"));
        }
        return rows.Select(row => (object)PyDict.Of("instance", row.Instance, "version", row.Version, "last_played", row.LastPlayed, "cover", row.Cover, "cover_kind", row.CoverKind, "pinned", row.Pinned)).ToList();
    }
    public object home_options()
    {
        lock (Config.CONFIG._lock)
        {
            var pins = HomePreferences.Pins(Config.CONFIG.Get("ui_pinned_instances"), Instances.ListInstances().Select(Py.S));
            var modules = HomePreferences.NormalizeSections(Config.CONFIG.Get("ui_home_sidebar_modules", new List<object> { "clock", "worlds", "tasks" }));
            return PyDict.Of("pinned_instances", pins.Cast<object>().ToList(), "sidebar_enabled", Py.Truthy(Config.CONFIG.Get("ui_home_sidebar", true)), "sidebar_modules", modules.Cast<object>().ToList());
        }
    }
    public object pin_instance(string instance, bool pinned)
    {
        var installed = Instances.ListInstances().Select(Py.S).ToList();
        if (!installed.Contains(instance)) throw new ArgumentException("此实例已不存在，请刷新后再试。");
        lock (Config.CONFIG._lock)
        {
            var pins = HomePreferences.SetPinned(HomePreferences.Pins(Config.CONFIG.Get("ui_pinned_instances"), installed), instance, pinned);
            Config.CONFIG.Set("ui_pinned_instances", pins.Cast<object>().ToList()); Config.CONFIG.Save(); return home_options();
        }
    }
    public object save_home_sidebar(bool enabled, object modules)
    {
        if (!(modules is List<object> entries) || entries.Count > 4 || entries.Any(item => !(item is string name) || !HomePreferences.Sections.Contains(name))) throw new ArgumentException("信息栏模块设置无效。");
        lock (Config.CONFIG._lock)
        {
            Config.CONFIG.Set("ui_home_sidebar", enabled); Config.CONFIG.Set("ui_home_sidebar_modules", HomePreferences.NormalizeSections(modules).Cast<object>().ToList()); Config.CONFIG.Save();
            return home_options();
        }
    }
    public object recent_worlds(int limit = 6)
    {
        var roots = new List<(string Instance, string Version, string Root)>();
        foreach (string name in Instances.ListInstances().Select(Py.S).Take(40))
        {
            var game = new Instance(name); var versions = game.InstalledIds().Select(Py.S).Take(20).ToList();
            foreach (string version in versions) roots.Add((name, version, VersionSettings.GameDir(game, version)));
            roots.Add((name, "", game.path));
        }
        return RecentWorlds.Read(roots, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), limit).Select(world => (object)PyDict.Of("instance", world.Instance, "version", world.Version, "name", world.Name, "folder", world.Folder, "path", world.Path, "icon", world.Icon, "last_played", world.LastPlayed, "estimated", world.Estimated)).ToList();
    }
}
