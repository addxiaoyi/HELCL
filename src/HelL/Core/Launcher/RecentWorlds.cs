using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core.Launcher.Sync;

namespace HelL.Core.Launcher;

public sealed class RecentWorld
{
    public string Instance { get; set; } = "";
    public string Version { get; set; } = "";
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Path { get; set; } = "";
    public string Icon { get; set; } = "";
    public long LastPlayed { get; set; }
    public bool Estimated { get; set; }
}

public static class RecentWorlds
{
    public static List<RecentWorld> Read(IEnumerable<(string Instance, string Version, string Root)> roots, long now, int limit)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var worlds = new List<RecentWorld>();
        foreach (var root in roots.Take(200))
        {
            string saves = System.IO.Path.Combine(root.Root, "saves");
            try
            {
                if (!Directory.Exists(saves) || (File.GetAttributes(saves) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string folder in Directory.EnumerateDirectories(saves).Take(400))
                {
                    string full = System.IO.Path.GetFullPath(folder);
                    if (!seen.Add(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) continue;
                    string level = System.IO.Path.Combine(full, "level.dat");
                    if (!File.Exists(level) || (File.GetAttributes(level) & FileAttributes.ReparsePoint) != 0) continue;
                    var metadata = LevelDat.ReadFile(level);
                    long modified = new DateTimeOffset(File.GetLastWriteTimeUtc(level)).ToUnixTimeSeconds();
                    long played = metadata?.LastPlayed / 1000 ?? 0;
                    bool estimated = played <= 0 || played > now + 300;
                    if (estimated) played = Math.Min(now, modified);
                    string icon = System.IO.Path.Combine(full, "icon.png");
                    worlds.Add(new RecentWorld { Instance = root.Instance, Version = root.Version,
                        Name = string.IsNullOrWhiteSpace(metadata?.LevelName) ? System.IO.Path.GetFileName(full) : metadata!.LevelName,
                        Folder = System.IO.Path.GetFileName(full), Path = full, Icon = InstanceMedia.IsLocalImage(icon, full) ? icon : "", LastPlayed = played, Estimated = estimated });
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("RecentWorlds: {0}: {1}", saves, ex.Message); }
        }
        return worlds.OrderByDescending(world => world.LastPlayed).ThenBy(world => world.Name, StringComparer.Ordinal).Take(Math.Max(1, Math.Min(12, limit))).ToList();
    }
}
