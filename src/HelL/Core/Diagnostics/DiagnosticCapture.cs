using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core.Diagnostics;

public static class DiagnosticCapture
{
    public static DiagnosticReport Collect(DiagnosticSections sections, IReadOnlyList<DiagnosticIssue>? recentIssues = null)
    {
        string[] names = Instances.ListLegacyInstances().Select(Py.S).ToArray();
        DiagnosticSnapshot snapshot = DiagnosticSnapshot.Collect(Utils.ROOT, AppInfo.APP_VERSION, names.Length);
        var findings = new List<DiagnosticFinding>();
        if (sections.HasFlag(DiagnosticSections.LaunchChecks))
        {
            string selected = Py.S(Config.CONFIG.Get("default_instance"));
            if (names.Contains(selected))
            {
                var instance = new Instance(selected);
                string version = Py.S(instance.Meta().Get("mc_version"));
                PyDict preflight = Preflight.CheckLaunch(instance, version);
                findings = (preflight.Get("items") as IEnumerable<object> ?? Array.Empty<object>()).OfType<PyDict>()
                    .Select(item => new DiagnosticFinding(Py.S(item.Get("code")), Py.S(item.Get("level")))).ToList();
            }
        }
        var mirrors = new List<DiagnosticMirror>();
        if (sections.HasFlag(DiagnosticSections.Network))
        {
            PyDict health = MirrorHealth.Refresh(false);
            mirrors = (health.Get("samples") as IEnumerable<object> ?? Array.Empty<object>()).OfType<PyDict>()
                .Select(item => new DiagnosticMirror(Py.S(item.Get("provider")), Py.Truthy(item.Get("available")), Py.Int(item.Get("latency_ms")))).ToList();
        }
        return DiagnosticReportBuilder.Build(snapshot with { LaunchFindings = findings, Mirrors = mirrors, RecentIssues = recentIssues }, sections, DateTimeOffset.UtcNow);
    }
}
