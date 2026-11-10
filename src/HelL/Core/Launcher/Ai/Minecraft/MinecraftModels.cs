using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using HelL.Core.Launcher.Ai.Local;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftModels
{
    public const string Model = "qwen3:1.7b";
    private static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim PrepareGate = new SemaphoreSlim(1);
    private static LocalRuntime? _runtime;
    private static string _endpoint = "";
    private static bool _modelReady;
    public static string Root => Environment.GetEnvironmentVariable("HELL_MC_MODEL_HOME") ?? Path.GetFullPath(Py.S(Config.CONFIG.Get("minecraft_model_root", Path.Combine(Utils.ROOT, "ai", "minecraft", "ollama"))));
    public static PyDict Status() => PyDict.Of("ready", _modelReady && _runtime?.Managed == true && _endpoint.Length > 0, "endpoint", _endpoint, "model", Model);
    public static void StartIfSelected(BackendApi backend)
    {
        if (MinecraftExtensions.ModelAutoStart && Py.Truthy(Config.CONFIG.Get("minecraft_model_enabled")) && File.Exists(Path.Combine(Root, "model.json"))) backend.minecraft_prepare_model();
    }

    public static PyDict Prepare(TaskContext ctx)
    {
        if (!PrepareGate.Wait(0)) throw new InvalidOperationException("独立本地模型正在准备，请等待完成。");
        try
        {
            if (_runtime == null)
            {
                _runtime = new LocalRuntime(Root, Http);
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Stop(); } catch (Exception ex) { System.Diagnostics.Trace.TraceError("Minecraft model shutdown: " + ex.Message); } };
            }
            void Progress(long current, long total, string message) => ctx.Progress(current, total, message);
            if (!File.Exists(Path.Combine(Root, "runtime", "ollama.exe")))
            {
                var package = _runtime.PackageAsync(ctx.Token).GetAwaiter().GetResult();
                _runtime.InstallAsync(package, Progress, ctx.Token).GetAwaiter().GetResult();
            }
            if (!_runtime.Managed)
            {
                var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
                _endpoint = "http://127.0.0.1:" + port;
                _runtime.StartAsync(_endpoint, ctx.Token).GetAwaiter().GetResult();
            }
            using var client = new OllamaClient(_endpoint);
            if (!client.ModelsAsync(ctx.Token).GetAwaiter().GetResult().Any(model => model.Name == Model))
                client.StreamAsync("/api/pull", new { model = Model, stream = true }, Progress, ctx.Token).GetAwaiter().GetResult();
            LocalTools.Verify(PyDict.Of("ai_mode", "local", "ai_local_url", _endpoint, "ai_local_model", Model));
            _modelReady = true;
            Utils.WriteJson(Path.Combine(Root, "model.json"), PyDict.Of("model", Model, "runtime", _runtime.FindExecutable(), "prepared_at", DateTime.UtcNow.ToString("O")));
            ctx.Progress(1, 1, "独立游戏模型已就绪"); return Status();
        }
        finally { PrepareGate.Release(); }
    }
    public static void Stop() { _runtime?.Stop(); _endpoint = ""; _modelReady = false; }
}
