using System;
using System.Collections.Generic;

namespace HelL.Core.Launcher;

public static class DashboardPresets
{
    public static LayoutDoc Create(string name)
    {
        if (name == "focus") return new LayoutDoc(new List<LayoutItem> {
            Item("banner", 0, 0, .62, .48, 0), Item("config", .64, 0, .36, .48, 1),
            Item("quick", 0, .50, .62, .22, 2), Item("playtime", .64, .50, .36, .14, 3),
            Item("tasks", .64, .66, .36, .28, 4) });
        if (name == "studio") return new LayoutDoc(new List<LayoutItem> {
            Item("banner", 0, 0, .62, .48, 0), Item("config", .64, 0, .36, .48, 1),
            Item("screenshots", 0, .50, .62, .42, 2), Item("servers", .64, .50, .36, .34, 3),
            Item("notes", .64, .86, .36, .30, 4), Item("tasks", 0, .94, .62, .24, 5) });
        if (name != "standard") throw new ArgumentException("布局预设不存在。", nameof(name));
        return new LayoutDoc(new List<LayoutItem> {
            Item("banner", 0, 0, .62, .48, 0), Item("config", .64, 0, .36, .48, 1),
            Item("log", .64, .88, .36, .30, 2), Item("quick", 0, .50, .62, .20, 3),
            Item("playtime", .64, .50, .36, .14, 4), Item("news", 0, .72, .62, .46, 5) });
    }
    private static LayoutItem Item(string type, double x, double y, double w, double h, long z) => new(type, x, y, w, h, type + "-main", z);
}
