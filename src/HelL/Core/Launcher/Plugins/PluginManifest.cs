using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Plugins;

public sealed class PluginModule
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "notes";
    public Dictionary<string, string> Settings { get; set; } = new();
}

public sealed class PluginManifest
{
    public int ApiVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Homepage { get; set; } = "";
    public List<string> Screenshots { get; set; } = new();
    public List<PluginRelease> Releases { get; set; } = new();
    public List<PluginModule> Modules { get; set; } = new();
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static bool ValidId(string id) => id != null && Regex.IsMatch(id, @"\A[a-z][a-z0-9-]{1,62}\z");
    public static bool IsModuleType(string type)
    {
        string[] parts = (type ?? "").Split(':');
        return parts.Length == 3 && parts[0] == "plugin" && ValidId(parts[1]) && ValidId(parts[2]);
    }
    public static bool ValidUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo);
    public void Validate()
    {
        if (ApiVersion != 1) throw new InvalidDataException("此插件需要其他版本的启动器插件接口。");
        if (!ValidId(Id) || !System.Version.TryParse(Version, out _)) throw new InvalidDataException("插件标识或版本号无效。");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || (Description?.Length ?? 0) > 2000 || (Author?.Length ?? 0) > 100) throw new InvalidDataException("插件名称或介绍不符合限制。");
        if (!string.IsNullOrEmpty(Homepage) && !ValidUrl(Homepage)) throw new InvalidDataException("插件主页必须为 HTTPS。");
        if (Screenshots == null || Screenshots.Count > 6 || Screenshots.Any(path => string.IsNullOrEmpty(path) || !SafeAsset(path))) throw new InvalidDataException("插件截图必须来自 assets 目录，最多 6 张。");
        if (Releases == null || Releases.Count > 20 || Releases.Any(release => release == null || !System.Version.TryParse(release.Version, out _) || release.Notes == null || release.Notes.Length > 4000 || release.Date == null || (release.Date.Length > 0 && !DateTime.TryParseExact(release.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)))) throw new InvalidDataException("插件更新说明格式无效。");
        if (Modules == null || Modules.Count == 0 || Modules.Count > 20) throw new InvalidDataException("插件需要包含 1–20 个模块。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in Modules)
        {
            if (module == null || !ValidId(module.Id) || !ids.Add(module.Id) || string.IsNullOrWhiteSpace(module.Title) || module.Title.Length > 80) throw new InvalidDataException("插件模块名称或标识无效、重复。");
            if (!new[] { "notes", "clock", "links", "image", "system" }.Contains(module.Kind)) throw new InvalidDataException("不支持此插件模块类型：" + module.Kind);
            if (module.Settings == null || module.Settings.Count > 20 || module.Settings.Any(pair => pair.Key.Length > 80 || pair.Value == null || pair.Value.Length > 20000)) throw new InvalidDataException("插件模块设置超出限制。");
            if (module.Settings.TryGetValue("url", out string url) && url.Length > 0 && !ValidUrl(url)) throw new InvalidDataException("插件链接需要使用 HTTPS。");
            if (module.Settings.TryGetValue("image", out string image) && image.Length > 0 && !SafeAsset(image)) throw new InvalidDataException("插件图片必须来自包内 assets 目录。");
        }
    }
    public static bool SafeAsset(string path) => path.StartsWith("assets/", StringComparison.Ordinal) && !path.Contains("\\") && !path.Contains(":") && !path.Split('/').Any(part => part == "." || part == ".." || part.Length == 0) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static PluginManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Json) ?? throw new InvalidDataException("插件清单为空。");
        manifest.Validate(); return manifest;
    }
    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

public sealed class PluginRelease
{
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class InstalledPlugin
{
    public PluginManifest Manifest { get; set; } = new();
    public string Directory { get; set; } = "";
    public bool Enabled { get; set; }
    public string Error { get; set; } = "";
}
