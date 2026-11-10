using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Plugins;

public sealed class PluginListing
{
    public PluginManifest Manifest { get; set; } = new();
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public static class PluginCatalog
{
    public const string DefaultUrl = "https://raw.githubusercontent.com/addxiaoyi/HELL/refs/heads/main/market/catalog.json";
    public static List<PluginListing> Builtins() => new()
    {
        Builtin("desk-clock", "桌面时钟", "简洁的时间与日期，可切换 12 / 24 小时制。", "clock", new()),
        Builtin("world-journal", "冒险手记", "为不同世界分别记录坐标、计划和探索笔记。", "notes", new() { ["text"] = "世界：\n基地坐标：\n\n下一次冒险\n" }),
        Builtin("minecraft-wiki", "Minecraft 百科", "从启动页直接打开中文 Minecraft Wiki。", "links", new() { ["label"] = "中文 Minecraft Wiki", ["url"] = "https://zh.minecraft.wiki/" })
    };
    private static PluginListing Builtin(string id, string name, string description, string kind, Dictionary<string, string> settings) => new()
    { Manifest = new PluginManifest { Id = id, Name = name, Author = "HelL", Description = description, Modules = new() { new PluginModule { Id = "main", Title = name, Kind = kind, Settings = settings } } } };

    public static async Task<List<PluginListing>> Fetch(string url, CancellationToken token = default)
    {
        if (string.Equals(url, "https://raw.githubusercontent.com/addxiaoyi/HELL/main/market/catalog.json", StringComparison.OrdinalIgnoreCase)) url = DefaultUrl;
        if (!PluginManifest.ValidUrl(url)) throw new InvalidDataException("市场源需要填写 HTTPS 目录地址。");
        byte[] bytes = await Download(url, 2 * 1024 * 1024, token, refresh: true);
        using var doc = JsonDocument.Parse(bytes);
        if (!doc.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out int api) || api != 1 || !doc.RootElement.TryGetProperty("plugins", out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 500) throw new InvalidDataException("市场目录格式不受支持。");
        var entries = new List<PluginListing>(); var ids = new HashSet<string>();
        foreach (var row in rows.EnumerateArray())
        {
            var listing = JsonSerializer.Deserialize<PluginListing>(row.GetRawText(), PluginManifest.Json) ?? throw new InvalidDataException("市场条目为空。");
            if (listing.Manifest == null) throw new InvalidDataException("市场条目缺少插件清单。");
            listing.Manifest.Validate();
            if (!ids.Add(listing.Manifest.Id) || !PluginManifest.ValidUrl(listing.PackageUrl) || listing.Sha256 == null || listing.Sha256.Length != 64 || listing.Sha256.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("市场条目缺少有效的下载地址、校验值或包含重复插件。");
            entries.Add(listing);
        }
        return entries;
    }
    public static async Task Install(PluginListing listing, PluginStore store, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(listing.PackageUrl)) { store.InstallBuiltin(listing.Manifest); return; }
        byte[] bytes = await Download(listing.PackageUrl, PluginStore.MaxPackageBytes, token);
        store.InstallDownload(bytes, listing.Sha256, listing.Manifest.Id, listing.Manifest.Version);
    }
    public static async Task<InstalledPlugin> Preview(PluginListing listing, string cacheRoot, CancellationToken token = default, bool refresh = false)
    {
        if (string.IsNullOrEmpty(listing.PackageUrl)) return new InstalledPlugin { Manifest = listing.Manifest, Enabled = false };
        if (listing.Sha256.Length != 64 || listing.Sha256.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("插件预览缺少有效校验值。");
        var cache = new PluginStore(Path.Combine(cacheRoot, listing.Sha256));
        var ready = cache.List().FirstOrDefault(plugin => plugin.Manifest.Id == listing.Manifest.Id && plugin.Error.Length == 0);
        if (ready == null || refresh) { await Install(listing, cache, token); ready = cache.List().Single(plugin => plugin.Manifest.Id == listing.Manifest.Id); }
        return ready;
    }
    private static async Task<byte[]> Download(string url, long limit, CancellationToken token, bool refresh = false)
    {
        if (!PluginManifest.ValidUrl(url)) throw new InvalidDataException("插件下载地址必须为 HTTPS。");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        if (refresh) client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, MaxAge = TimeSpan.Zero };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HelL/3.2.0");
        // Validate every redirect instead of letting HTTPS silently downgrade to HTTP.
        for (int redirect = 0; redirect < 5; redirect++)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 && response.Headers.Location != null)
            {
                url = new Uri(new Uri(url), response.Headers.Location).AbsoluteUri;
                if (!PluginManifest.ValidUrl(url)) throw new InvalidDataException("插件源重定向到不支持的地址。");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("插件源响应超出大小限制。");
            using var stream = await response.Content.ReadAsStreamAsync(); using var buffer = new MemoryStream();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
            byte[] chunk = new byte[16384]; int read;
            while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, deadline.Token)) > 0)
            {
                if (buffer.Length + read > limit) throw new InvalidDataException("插件源响应超出大小限制。");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        throw new IOException("插件源重定向次数过多。");
    }
}
