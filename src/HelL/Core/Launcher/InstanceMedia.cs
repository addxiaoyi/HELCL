using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HelL.Core.Launcher;

public static class InstanceMedia
{
    public const long MaxImageBytes = 32L * 1024 * 1024;

    public static bool IsLocalImage(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) return false;
            string extension = Path.GetExtension(full).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg")) return false;
            var file = new FileInfo(full);
            if (!file.Exists || file.Length < 8 || file.Length > MaxImageBytes) return false;
            string? current = full;
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                current = Path.GetDirectoryName(current);
            }
            using var stream = File.OpenRead(full);
            var signature = new byte[8];
            if (stream.Read(signature, 0, signature.Length) != signature.Length) return false;
            return extension == ".png" ? signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                : signature[0] == 255 && signature[1] == 216 && signature[2] == 255;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
        { System.Diagnostics.Trace.TraceWarning("InstanceMedia: " + ex.Message); return false; }
    }

    public static PyDict Find(IEnumerable<string> gameDirectories)
    {
        var screenshots = new List<(string Path, DateTime Modified)>();
        var icons = new List<(string Path, DateTime Modified)>();
        foreach (string root in gameDirectories.Distinct(StringComparer.OrdinalIgnoreCase).Take(24))
        {
            try
            {
                string shots = Path.Combine(root, "screenshots");
                if (RegularDirectory(shots))
                    foreach (string file in Directory.EnumerateFiles(shots).Take(2000))
                        if (IsLocalImage(file, root)) screenshots.Add((file, File.GetLastWriteTimeUtc(file)));
                string saves = Path.Combine(root, "saves");
                if (RegularDirectory(saves))
                    foreach (string world in Directory.EnumerateDirectories(saves).Take(400))
                    {
                        if (!RegularDirectory(world)) continue;
                        string icon = Path.Combine(world, "icon.png");
                        if (IsLocalImage(icon, root)) icons.Add((icon, File.GetLastWriteTimeUtc(icon)));
                    }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { System.Diagnostics.Trace.TraceWarning("InstanceMedia.Find: " + ex.Message); }
        }
        var candidates = screenshots.Count > 0 ? screenshots : icons;
        var selected = candidates.OrderByDescending(file => file.Modified).ThenBy(file => file.Path, StringComparer.Ordinal).FirstOrDefault();
        return PyDict.Of("path", selected.Path ?? "", "kind", selected.Path == null ? "none" : screenshots.Count > 0 ? "screenshot" : "world", "modified", selected.Modified == default ? 0L : new DateTimeOffset(selected.Modified).ToUnixTimeSeconds());
    }

    private static bool RegularDirectory(string path) => Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
}
