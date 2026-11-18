using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public sealed class DashboardInstance
{
    public string Instance { get; set; } = "";
    public string Version { get; set; } = "";
    public long LastPlayed { get; set; }
    public string Cover { get; set; } = "";
    public string CoverKind { get; set; } = "";
    public bool Pinned { get; set; }
}

public static class DashboardLibrary
{
    public static List<DashboardInstance> Select(PyDict history, IReadOnlyDictionary<string, List<string>> installed, long now, int limit = 6, IEnumerable<string>? pinned = null)
    {
        var rows = new List<DashboardInstance>(); var pins = new HashSet<string>(pinned ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        foreach (var entry in installed)
        {
            var latest = RecentPlay.Select(history, new Dictionary<string, List<string>> { [entry.Key] = entry.Value }, now);
            rows.Add(new DashboardInstance {
                Instance = entry.Key, Version = Py.Truthy(latest.Get("available")) ? Py.S(latest.Get("version")) : entry.Value.LastOrDefault() ?? "", Pinned = pins.Contains(entry.Key),
                LastPlayed = Py.Truthy(latest.Get("available")) ? Py.Int(latest.Get("ended")) : 0
            });
        }
        return rows.OrderByDescending(row => row.Pinned).ThenByDescending(row => row.LastPlayed).ThenByDescending(row => row.Version.Length > 0).ThenBy(row => row.Instance, StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, Math.Min(24, limit))).ToList();
    }
}
