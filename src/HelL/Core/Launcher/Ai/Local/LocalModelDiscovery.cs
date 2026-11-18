using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class LocalModelDiscovery
{
    private readonly Func<string, OllamaClient> _connect;

    public LocalModelDiscovery(Func<string, OllamaClient>? connect = null) =>
        _connect = connect ?? (address => new OllamaClient(address));

    public async Task<LocalModelStatus> FindAsync(string endpoint, bool detect, CancellationToken ct)
    {
        endpoint = ModelRules.Endpoint(endpoint);
        var configured = await ProbeAsync(endpoint, ct).ConfigureAwait(false);
        if (configured.Online || !detect) return configured;

        foreach (string address in new[] { ModelRules.DefaultEndpoint, ModelRules.ManagedEndpoint }.Distinct())
        {
            if (address == endpoint) continue;
            var found = await ProbeAsync(address, ct).ConfigureAwait(false);
            if (!found.Online) continue;
            // Discovery is a suggestion; model actions must keep using the saved endpoint.
            configured.DiscoveredEndpoint = address;
            break;
        }
        return configured;
    }

    private async Task<LocalModelStatus> ProbeAsync(string endpoint, CancellationToken ct)
    {
        var status = new LocalModelStatus { Endpoint = endpoint };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var client = _connect(endpoint);
        try
        {
            var version = await client.JsonAsync("/api/version", null, timeout.Token).ConfigureAwait(false);
            string number = OllamaClient.Text(version, "version");
            if (number.Length == 0) throw new IOException("此端口没有返回 Ollama 版本信息。");
            status.Models = await client.ModelsAsync(timeout.Token).ConfigureAwait(false);
            status.Version = number;
            status.Online = true;
            var running = await client.JsonAsync("/api/ps", null, timeout.Token).ConfigureAwait(false);
            if (!running.TryGetProperty("models", out var loaded) || loaded.ValueKind != JsonValueKind.Array)
                throw new IOException("服务未返回运行中的模型列表。");
            var names = loaded.EnumerateArray().Select(model => OllamaClient.Text(model, "name")).ToList();
            foreach (var model in status.Models) model.Running = names.Contains(model.Name);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException || ex is JsonException)
        {
            ct.ThrowIfCancellationRequested();
            status.Error = status.Online ? "服务已连接，但读取运行状态失败：" + ex.Message
                : "无法连接 " + endpoint + "。请确认 Ollama 已启动，并检查本地服务地址。";
        }
        return status;
    }
}
