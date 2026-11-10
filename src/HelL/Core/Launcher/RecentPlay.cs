using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class RecentPlay
{
    public static PyDict Select(PyDict playtime, IReadOnlyDictionary<string, List<string>> installed, long now)
    {
        string instance = "", version = "";
        long latest = 0, duration = 0;
        foreach (var entry in playtime)
        {
            if (!installed.TryGetValue(entry.Key, out var versions) || !(entry.Value is PyDict record)) continue;
            foreach (var session in Py.ListOr(record.Get("sessions")).OfType<PyDict>())
            {
                string candidate = Py.S(session.Get("version"));
                if (!versions.Contains(candidate)) continue;
                if (!long.TryParse(Py.S(session.Get("start")), out long start) || !long.TryParse(Py.S(session.Get("duration")), out long length)) continue;
                if (start <= 0 || length <= 0 || start > now || start > long.MaxValue - length) continue;
                long ended = start + length;
                if (ended > now + 300 || ended <= latest) continue;
                instance = entry.Key; version = candidate; latest = ended; duration = length;
            }
        }
        return PyDict.Of("instance", instance, "version", version, "ended", latest, "duration", duration, "available", latest > 0);
    }
}
