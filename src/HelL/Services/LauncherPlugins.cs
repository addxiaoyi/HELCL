using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Plugins;

namespace HelL.Services;

public static class LauncherPlugins
{
    private static PluginStore? _store;
    public static PluginStore Store => _store ??= new PluginStore(Path.Combine(Utils.ROOT, "launcher-plugins"));
    public static int Revision { get; private set; }
    public static event Action? Changed;
    public static void NotifyChanged() { Revision++; Changed?.Invoke(); }
    public static List<InstalledPlugin> Installed() => Store.List();
    public static (InstalledPlugin Plugin, PluginModule Module)? Find(string type)
    {
        if (!PluginManifest.IsModuleType(type)) return null;
        string[] parts = type.Split(':');
        var plugin = Installed().FirstOrDefault(plugin => plugin.Manifest.Id == parts[1] && plugin.Enabled && plugin.Error.Length == 0);
        var module = plugin?.Manifest.Modules.FirstOrDefault(module => module.Id == parts[2]);
        return plugin != null && module != null ? (plugin, module) : null;
    }
}
