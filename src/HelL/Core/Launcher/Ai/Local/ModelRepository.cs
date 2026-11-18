using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class ModelRepository
{
    private readonly HttpClient _http;
    public string Source { get; }
    public ModelRepository(HttpClient http, string source)
    {
        _http = http;
        Source = ValidateSource(source);
    }

    public static string ValidateSource(string source)
    {
        if (!Uri.TryCreate(source?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("模型镜像应为 HTTPS 站点根地址，例如 https://hf-mirror.com。");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public async Task<List<string>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("请输入模型名称，例如 Qwen3 GGUF。");
        var json = await GetAsync("/api/models?search=" + Uri.EscapeDataString(query.Trim()) + "&filter=gguf&limit=30&sort=downloads&direction=-1", ct).ConfigureAwait(false);
        if (json.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("该来源没有返回兼容 Hugging Face 的模型列表。");
        return json.EnumerateArray().Select(model => OllamaClient.Text(model, "id")).Where(id => id.Length > 0).ToList();
    }

    public async Task<List<ModelFile>> FilesAsync(string repository, ModelHardware hardware, CancellationToken ct)
    {
        ValidateRepository(repository);
        var json = await GetAsync("/api/models/" + repository + "?blobs=true", ct).ConfigureAwait(false);
        string revision = OllamaClient.Text(json, "sha");
        if (!Regex.IsMatch(revision, @"\A[a-fA-F0-9]{40,64}\z")) throw new InvalidOperationException("仓库缺少固定版本号，无法安全续传。请切换到官方源重试。");
        if (!json.TryGetProperty("siblings", out var siblings) || siblings.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("仓库没有返回文件列表。");
        var files = new List<ModelFile>();
        foreach (var entry in siblings.EnumerateArray())
        {
            string name = OllamaClient.Text(entry, "rfilename");
            if (!name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;
            entry.TryGetProperty("lfs", out var lfs);
            string sha = OllamaClient.Text(lfs, "sha256");
            long size = OllamaClient.Number(lfs, "size");
            bool split = Regex.IsMatch(name, @"-\d{5}-of-\d{5}\.gguf$", RegexOptions.IgnoreCase);
            bool projector = name.IndexOf("mmproj", StringComparison.OrdinalIgnoreCase) >= 0;
            bool supported = !projector && size > 0 && Regex.IsMatch(sha, @"\A[a-fA-F0-9]{64}\z");
            files.Add(new ModelFile { Repository = repository, Revision = revision, Name = name, Size = size, Sha256 = sha,
                Supported = supported, Fit = ModelRules.Assess(size, hardware, true),
                Note = projector ? "这是视觉投影文件，不是独立聊天模型" : !supported ? "缺少文件大小或 SHA-256" : "校验后导入 Ollama · 导入时需额外磁盘空间" });
        }
        return GgufParts.Group(files, hardware);
    }

    public Uri FileUri(ModelFile file)
    {
        ValidateRepository(file.Repository);
        if (!Regex.IsMatch(file.Revision, @"\A[a-fA-F0-9]{40,64}\z") || file.Name.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
            throw new ArgumentException("模型文件路径无效。");
        string path = string.Join("/", file.Name.Split('/').Select(Uri.EscapeDataString));
        return new Uri(Source + "/" + file.Repository + "/resolve/" + file.Revision + "/" + path);
    }

    private static void ValidateRepository(string repository)
    {
        if (!Regex.IsMatch(repository ?? "", @"\A[a-zA-Z0-9][\w.-]*/[a-zA-Z0-9][\w.-]*\z") || repository.Contains(".."))
            throw new ArgumentException("仓库名称应为 作者/模型，例如 Qwen/Qwen3-0.6B-GGUF。");
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await ModelHttp.GetAsync(_http, new Uri(Source + path), 0, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("模型仓库请求失败（" + (int)response.StatusCode + "）。请检查镜像地址或切换官方源；私有与受限仓库暂不支持。");
        using var cancellation = timeout.Token.Register(response.Dispose);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.Clone();
    }
}
