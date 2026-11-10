using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HelL.Core.Launcher;

public sealed class EngineWallpaper
{
    public string Monitor { get; set; } = "";
    public string File { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Preview { get; set; } = "";
    public string Error { get; set; } = "";
    public bool Direct => Kind == "video" || Kind == "image";
    public string DisplayFile => Direct && System.IO.File.Exists(File) ? File : Preview;
    public string Description => Error.Length > 0 ? Error : Direct ? "图片 / 视频 · 跟随当前选择" : "网页 / 场景 · 使用项目预览图，暂不播放引擎动画";
}

public static class WallpaperEngine
{
    public static List<string> DiscoverConfigs()
    {
        var folders = new List<string>();
        foreach (string processName in new[] { "wallpaper64", "wallpaper32" })
            foreach (var process in Process.GetProcessesByName(processName))
                using (process)
                    try { string path = process.MainModule?.FileName ?? ""; if (path.Length > 0) folders.Add(Path.GetDirectoryName(path)!); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) { Trace.TraceWarning("Wallpaper Engine process lookup: {0}", ex.Message); }
        string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "") as string ?? "";
        if (steam.Length > 0)
        {
            var libraries = new List<string> { steam }; string index = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (System.IO.File.Exists(index))
                foreach (Match match in Regex.Matches(System.IO.File.ReadAllText(index), "\"path\"\\s+\"([^\"]+)\"")) libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            folders.AddRange(libraries.Select(library => Path.Combine(library, "steamapps", "common", "wallpaper_engine")));
        }
        return folders.Select(folder => Path.Combine(folder, "config.json")).Where(System.IO.File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static List<EngineWallpaper> ParseSelections(string json, string user = "")
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Wallpaper Engine 配置格式无效。");
        if (user.Length > 0)
            foreach (var property in root.EnumerateObject())
                if (property.Name.Equals(user, StringComparison.OrdinalIgnoreCase)) { var selected = FindCurrent(property.Value); if (selected.Count > 0) return selected; }
        return FindCurrent(root);
    }
    private static List<EngineWallpaper> FindCurrent(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return new();
        if (node.TryGetProperty("wallpaperconfig", out var config) && config.ValueKind == JsonValueKind.Object && config.TryGetProperty("selectedwallpapers", out var selected) && selected.ValueKind == JsonValueKind.Object)
        {
            var papers = new List<EngineWallpaper>();
            foreach (var monitor in selected.EnumerateObject())
            {
                string file = monitor.Value.ValueKind == JsonValueKind.String ? monitor.Value.GetString() ?? "" : Text(monitor.Value, "file");
                if (file.Length > 0) papers.Add(new EngineWallpaper { Monitor = monitor.Name, File = file });
            }
            return papers.OrderBy(paper => paper.Monitor, StringComparer.OrdinalIgnoreCase).ToList();
        }
        foreach (var property in node.EnumerateObject().Where(property => property.Name.IndexOf("recent", StringComparison.OrdinalIgnoreCase) < 0 && property.Name.IndexOf("history", StringComparison.OrdinalIgnoreCase) < 0))
        { var papers = FindCurrent(property.Value); if (papers.Count > 0) return papers; }
        return new();
    }
    public static List<EngineWallpaper> Read(string configFile)
    {
        using var stream = new FileStream(configFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("Wallpaper Engine 配置过大。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var papers = ParseSelections(reader.ReadToEnd(), Environment.UserName);
        foreach (var paper in papers) ReadProject(paper);
        return papers;
    }
    private static void ReadProject(EngineWallpaper paper)
    {
        try
        {
            paper.File = Path.GetFullPath(paper.File); string folder = Path.GetDirectoryName(paper.File)!;
            paper.Title = Path.GetFileNameWithoutExtension(paper.File); string extension = Path.GetExtension(paper.File).ToLowerInvariant();
            paper.Kind = new[] { ".mp4", ".webm", ".wmv", ".avi", ".m4v" }.Contains(extension) ? "video" : new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(extension) ? "image" : "scene";
            string project = Path.Combine(folder, "project.json");
            if (System.IO.File.Exists(project))
            {
                if (new FileInfo(project).Length > 2 * 1024 * 1024) throw new InvalidDataException("壁纸项目说明过大。");
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(project));
                string kind = Text(doc.RootElement, "type").ToLowerInvariant(); if (kind.Length > 0) paper.Kind = kind;
                string title = Text(doc.RootElement, "title"); if (title.Length > 0) paper.Title = title;
                string preview = Text(doc.RootElement, "preview");
                if (preview.Length > 0 && !Path.IsPathRooted(preview))
                {
                    string path = Path.GetFullPath(Path.Combine(folder, preview));
                    if (path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path)) paper.Preview = path;
                }
            }
            if (!System.IO.File.Exists(paper.File)) paper.Error = "原壁纸文件已移动或尚未下载，暂用可用预览图。";
            if (paper.DisplayFile.Length == 0) paper.Error = "没有可读取的壁纸或预览图，请在 Wallpaper Engine 中重新下载。";
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException || ex is ArgumentException || ex is UnauthorizedAccessException)
        { paper.Error = "壁纸项目无法读取：" + ex.Message; }
    }
    private static string Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
