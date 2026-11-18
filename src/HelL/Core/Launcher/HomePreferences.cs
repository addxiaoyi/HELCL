using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class HomePreferences
{
    public static readonly string[] Sections = { "clock", "worlds", "tasks", "playtime" };
    public static List<string> Pins(object stored, IEnumerable<string> installed) =>
        Py.ListOr(stored).Select(Py.S).Where(new HashSet<string>(installed, StringComparer.Ordinal).Contains).Distinct(StringComparer.Ordinal).Take(24).ToList();
    public static List<string> NormalizeSections(object stored) =>
        Py.ListOr(stored).Select(Py.S).Where(section => Sections.Contains(section)).Distinct().Take(4).ToList();
    public static List<string> SetPinned(IEnumerable<string> pins, string instance, bool pinned)
    {
        var next = pins.Where(name => name != instance).ToList();
        if (pinned)
        {
            if (next.Count >= 24) throw new ArgumentException("最多固定 24 个实例，请先取消一个固定。");
            next.Add(instance);
        }
        return next;
    }

    public static void PinMany(IEnumerable<string> instances, bool pinned)
    {
        var selected = InstanceGroups.ValidateSelection(instances);
        var config = Config.CONFIG;
        lock (config._lock)
        {
            var previous = config.Get("ui_pinned_instances");
            var pins = Pins(previous, Instances.ListInstances().Select(Py.S));
            var next = pinned ? pins.Concat(selected).Distinct(StringComparer.Ordinal).ToList() : pins.Except(selected, StringComparer.Ordinal).ToList();
            if (next.Count > 24) throw new ArgumentException("最多固定 24 个实例；本次未修改，请减少选择或先取消固定。");
            try { config.Set("ui_pinned_instances", next.Cast<object>().ToList()); config.Save(); }
            catch { config.Set("ui_pinned_instances", previous); throw; }
        }
    }
}
