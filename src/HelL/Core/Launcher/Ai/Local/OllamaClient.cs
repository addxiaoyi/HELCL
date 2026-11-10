using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class OllamaClient : IDisposable
{
    private readonly HttpClient _http;
    public string Endpoint { get; }

    public OllamaClient(string endpoint, HttpMessageHandler? handler = null)
    {
        Endpoint = ModelRules.Endpoint(endpoint);
        _http = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = new Uri(Endpoint), Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<JsonElement> JsonAsync(string path, object? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
        if (body != null) request.Content = JsonContent(body);
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        await EnsureSuccess(response).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    public async Task StreamAsync(string path, object body, Action<long, long, string> progress, CancellationToken ct)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(TimeSpan.FromMinutes(5));
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent(body) };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
        await EnsureSuccess(response).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var cancel = idle.Token.Register(stream.Dispose);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        bool complete = false;
        string line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            ct.ThrowIfCancellationRequested();
            idle.CancelAfter(TimeSpan.FromMinutes(5));
            if (line.Length == 0) continue;
            using var document = JsonDocument.Parse(line);
            var item = document.RootElement;
            string error = Text(item, "error");
            if (error.Length > 0) throw new InvalidOperationException("Ollama：" + error);
            string status = Text(item, "status");
            complete |= status == "success" || (item.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True);
            progress(Number(item, "completed"), Number(item, "total"), status.Length > 0 ? ModelRules.ProgressText(status) : "正在加载模型");
        }
        ct.ThrowIfCancellationRequested();
        if (!complete) throw new IOException("本地服务提前断开，操作尚未完成。请重试，已有下载会保留。");
    }

    public async Task<List<LocalModelInfo>> ModelsAsync(CancellationToken ct)
    {
        var tags = await JsonAsync("/api/tags", null, ct).ConfigureAwait(false);
        if (!tags.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new IOException("此端口未返回 Ollama 模型列表，请检查本地服务地址。");
        return models.EnumerateArray().Where(model => Text(model, "remote_model").Length == 0 && Text(model, "remote_host").Length == 0
            && !Text(model, "name").EndsWith("-cloud", StringComparison.OrdinalIgnoreCase)).Select(model => {
            model.TryGetProperty("details", out var details);
            return new LocalModelInfo { Name = Text(model, "name"), Size = Number(model, "size"),
                Parameters = Text(details, "parameter_size"), Quantization = Text(details, "quantization_level") };
        }).ToList();
    }

    public Task ImportAsync(string file, string name, Action<long, long, string> progress, CancellationToken ct)
    {
        var paths = GgufParts.LocalFiles(file);
        return ImportFilesAsync(paths.ToDictionary(Path.GetFileName, path => path), name, progress, ct);
    }

    public async Task ImportFilesAsync(IReadOnlyDictionary<string, string> files, string name, Action<long, long, string> progress, CancellationToken ct)
    {
        ModelRules.ModelName(name);
        var ordered = GgufParts.Order(files.Keys);
        if (ordered.Any(file => Path.GetFileName(file) != file)) throw new IOException("导入文件名不能包含目录。");
        foreach (string file in ordered) GgufParts.ValidateHeader(files[file]);
        var installed = await ModelsAsync(ct).ConfigureAwait(false);
        if (installed.Any(model => model.Name == name || model.Name == name + ":latest"))
            throw new IOException("本地已存在同名模型，请使用其他名称，避免覆盖原模型。");
        var digests = new Dictionary<string, string>();
        foreach (string file in ordered)
        {
            progress(0, 0, "正在校验与导入 " + file);
            string digest = "sha256:" + await ModelDownload.HashAsync(files[file], ct).ConfigureAwait(false);
            using (var content = new StreamContent(File.OpenRead(files[file])))
            using (var response = await _http.PostAsync("/api/blobs/" + digest, content, ct).ConfigureAwait(false))
                await EnsureSuccess(response).ConfigureAwait(false);
            digests.Add(file, digest);
        }
        await StreamAsync("/api/create", new { model = name, files = digests, stream = true }, progress, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        ModelRules.ModelName(name);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/delete") { Content = JsonContent(new { model = name }) };
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        await EnsureSuccess(response).ConfigureAwait(false);
    }

    public static string Text(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public static long Number(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.TryGetInt64(out var number) ? number : 0;
    private static StringContent JsonContent(object body) => new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string message = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (message.Length > 600) message = message.Substring(0, 600);
        throw new IOException("本地服务请求失败（" + (int)response.StatusCode + "）：" + message);
    }
    public void Dispose() => _http.Dispose();
}
