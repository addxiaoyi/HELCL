using System;
using System.Collections.Generic;
using System.Text;

namespace HelL.Services;

public static class LaunchUtils
{
    public static (string Host, string Port) ParseServerAddress(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ("127.0.0.1", "25565");
        }

        string text = raw.Trim();

        // Handle bracketed IPv6: [::1]:25565 or [::1]
        if (text.StartsWith("["))
        {
            int closeBracket = text.IndexOf(']');
            if (closeBracket > 0)
            {
                string host = text.Substring(1, closeBracket - 1);
                string port = "25565";
                if (closeBracket + 1 < text.Length && text[closeBracket + 1] == ':')
                {
                    string p = text.Substring(closeBracket + 2).Trim();
                    if (int.TryParse(p, out int portNum) && portNum > 0 && portNum <= 65535)
                    {
                        port = portNum.ToString();
                    }
                }
                return (host, port);
            }
        }

        // Handle host:port
        int colon = text.LastIndexOf(':');
        if (colon > 0 && colon < text.Length - 1)
        {
            string hostPart = text.Substring(0, colon).Trim();
            string portPart = text.Substring(colon + 1).Trim();
            if (int.TryParse(portPart, out int portNum) && portNum > 0 && portNum <= 65535)
            {
                return (hostPart, portNum.ToString());
            }
        }

        return (text, "25565");
    }

    public static string ExpandPlaceholders(string template, string gameDir, string versionId)
    {
        if (string.IsNullOrEmpty(template))
        {
            return "";
        }

        return template
            .Replace("{game_directory}", gameDir ?? "")
            .Replace("{version_id}", versionId ?? "")
            .Replace("{app_dir}", AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/'));
    }

    public static bool IsGcSupportedForJava(int javaMajor, string gcPreset)
    {
        if (string.IsNullOrWhiteSpace(gcPreset))
        {
            return true;
        }

        string upper = gcPreset.Trim().ToUpperInvariant();
        if (upper.Contains("ZGC"))
        {
            // ZGC is only officially supported without crashes on Java 17+ (or experimental 15+)
            return javaMajor >= 15;
        }

        if (upper.Contains("SHENANDOAH"))
        {
            return javaMajor >= 11;
        }

        return true;
    }

    public static string GetAikarOptimizedFlags(int memoryMb)
    {
        return "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 " +
               "-XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch " +
               "-XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1ReservePercent=20 " +
               "-XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 " +
               "-XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 " +
               "-XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 " +
               "-XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1";
    }
}

public class VirtualLogBuffer
{
    private readonly int _maxLines;
    private readonly List<string> _lines;
    private readonly object _lock = new object();

    public VirtualLogBuffer(int maxLines = 1000)
    {
        _maxLines = Math.Max(100, maxLines);
        _lines = new List<string>(_maxLines);
    }

    public int LineCount
    {
        get
        {
            lock (_lock)
            {
                return _lines.Count;
            }
        }
    }

    public void AppendLine(string line)
    {
        if (line == null) return;

        lock (_lock)
        {
            if (_lines.Count >= _maxLines)
            {
                int removeCount = _lines.Count - _maxLines + 1;
                _lines.RemoveRange(0, removeCount);
            }
            _lines.Add(line);
        }
    }

    public string GetLine(int index)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _lines.Count)
            {
                return "";
            }
            return _lines[index];
        }
    }

    public string GetRecentText(int count = 200)
    {
        lock (_lock)
        {
            if (_lines.Count == 0) return "";
            int start = Math.Max(0, _lines.Count - count);
            var sb = new StringBuilder();
            for (int i = start; i < _lines.Count; i++)
            {
                sb.AppendLine(_lines[i]);
            }
            return sb.ToString();
        }
    }
}
