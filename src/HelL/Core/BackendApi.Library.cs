using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object instance_visual(string instance, string version = "")
    {
        if (!Instances.ListInstances().Any(name => Py.S(name) == instance)) throw new ArgumentException("该实例已不存在。");
        var game = new Instance(instance);
        string custom = InstanceArtwork.CustomCover(game);
        if (custom.Length > 0) return PyDict.Of("path", custom, "kind", "custom", "modified", new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(custom)).ToUnixTimeSeconds());
        var paths = new List<string>();
        var installed = game.InstalledIds().Select(Py.S).ToList();
        if (version.Length > 0)
        {
            if (!installed.Contains(version)) throw new ArgumentException("该版本已不在此实例中。");
            paths.Add(VersionSettings.GameDir(game, version));
        }
        else
        {
            paths.Add(game.path);
            paths.AddRange(installed.Take(23).Select(id => VersionSettings.GameDir(game, id)));
        }
        return InstanceMedia.Find(paths);
    }

    public object set_instance_cover(string instance, string path)
    {
        var game = ExistingArtworkInstance(instance);
        InstanceArtwork.SetCover(game, path);
        return instance_visual(instance);
    }

    public object reset_instance_cover(string instance)
    {
        InstanceArtwork.ResetCover(ExistingArtworkInstance(instance));
        return instance_visual(instance);
    }

    public object instance_screenshots(string instance, int offset = 0, int limit = 24)
    {
        var game = ExistingArtworkInstance(instance);
        var roots = new List<string> { game.path };
        roots.AddRange(game.InstalledIds().Select(id => VersionSettings.GameDir(game, id)));
        return InstanceArtwork.Screenshots(roots, offset, limit);
    }

    private static Instance ExistingArtworkInstance(string name)
    {
        if (!Instances.ListInstances().Any(instance => Py.S(instance) == name)) throw new ArgumentException("实例不存在。");
        return new Instance(name);
    }

    public object recent_play()
    {
        var installed = Instances.ListInstances().Select(Py.S).ToDictionary(name => name, name => new Instance(name).InstalledIds().Select(Py.S).ToList(), StringComparer.Ordinal);
        var recent = RecentPlay.Select(Playtime.GetAllPlaytime() as PyDict ?? new PyDict(), installed, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (Py.Truthy(recent.Get("available"))) recent["visual"] = instance_visual(Py.S(recent.Get("instance")), Py.S(recent.Get("version")));
        return recent;
    }
}
