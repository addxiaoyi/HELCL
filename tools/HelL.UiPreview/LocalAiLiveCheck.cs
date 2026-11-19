using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher.Ai.Local;

internal static class LocalAiLiveCheck
{
    internal static async Task<int> Run(string folder)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var runtime = new LocalRuntime(folder, http);
        DateTime last = DateTime.MinValue;
        void Progress(long current, long total, string message)
        {
            if ((DateTime.UtcNow - last).TotalSeconds < 4 && current != total) return;
            last = DateTime.UtcNow;
            Console.WriteLine("[LIVE] " + message + " " + current + "/" + total);
        }
        try
        {
            if (!File.Exists(Path.Combine(folder, "runtime", "ollama.exe")))
            {
                var package = await runtime.PackageAsync(CancellationToken.None);
                Console.WriteLine("[LIVE] Official package " + package.Version + " bytes=" + package.Size + " sha256=" + package.Sha256);
                await runtime.InstallAsync(package, Progress, CancellationToken.None);
            }
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            string endpoint = "http://127.0.0.1:" + port;
            await runtime.StartAsync(endpoint, CancellationToken.None);
            using var client = new OllamaClient(endpoint);
            const string model = "qwen3:0.6b";
            Console.WriteLine("[LIVE] Local runtime ready at " + endpoint);
            var discovered = await new LocalModelDiscovery().FindAsync(endpoint, false, CancellationToken.None);
            if (!discovered.Online || discovered.Endpoint != endpoint) throw new IOException("Configured local service was not discovered");
            Console.WriteLine("[LIVE] Configured service discovery passed");
            await client.StreamAsync("/api/pull", new { model, stream = true }, Progress, CancellationToken.None);
            var models = await client.ModelsAsync(CancellationToken.None);
            Console.WriteLine("[LIVE] Discovered " + models.Count + " local model(s)");
            var settings = PyDict.Of("ai_mode", "local", "ai_local_url", endpoint, "ai_local_model", model, "ai_local_context", 4096L, "ai_api_key", "unused-test-key");
            var reply = await Task.Run(() => LocalChat.Run(settings, new System.Collections.Generic.List<object>(), "请只回答：本地运行成功。", null, null, null, null, null));
            if (string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException("No local model response");
            File.WriteAllText(Path.Combine(folder, "reply.txt"), reply.Text);
            Console.WriteLine("[LIVE] Inference passed: chars=" + reply.Text.Length + " stop_reason=" + reply.stop_reason);
            var hub = new ModelRepository(http, "https://hf-mirror.com");
            var files = await hub.FilesAsync("Qwen/Qwen3-0.6B-GGUF", new ModelHardware(), CancellationToken.None);
            var file = files.Find(entry => entry.Supported && entry.Name == "Qwen3-0.6B-Q8_0.gguf") ?? throw new IOException("Expected GGUF file missing upstream");
            string download = Path.Combine(folder, "downloads", file.Sha256 + ".gguf");
            Console.WriteLine("[LIVE] Mirror file revision=" + file.Revision + " bytes=" + file.Size + " sha256=" + file.Sha256);
            await new ModelDownload(http).DownloadAsync(hub.FileUri(file), download, file.Size, file.Sha256, Progress, CancellationToken.None);
            const string imported = "local/qwen3-gguf-check";
            if (!models.Exists(entry => entry.Name == imported + ":latest")) await client.ImportAsync(download, imported, Progress, CancellationToken.None);
            settings["ai_local_model"] = imported;
            var ggufReply = await Task.Run(() => LocalChat.Run(settings, new System.Collections.Generic.List<object>(), "请只回答：导入成功。", null, null, null, null, null));
            File.WriteAllText(Path.Combine(folder, "gguf-reply.txt"), ggufReply.Text);
            Console.WriteLine("[LIVE] Mirror GGUF imported and inference passed: chars=" + ggufReply.Text.Length);
            string splitFolder = Path.GetFullPath(Path.Combine(folder, "..", "..", "ai-model-management-20261107", "split"));
            if (Directory.Exists(splitFolder))
            {
                string[] shards = Directory.GetFiles(splitFolder, "*-00001-of-*.gguf");
                if (shards.Length != 1) throw new IOException("Expected one complete split model fixture");
                string splitModel = "local/split-check-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                await client.ImportAsync(shards[0], splitModel, Progress, CancellationToken.None);
                try
                {
                    settings["ai_local_model"] = splitModel;
                    var splitReply = await Task.Run(() => LocalChat.Run(settings, new System.Collections.Generic.List<object>(), "请只回答：分片运行成功。", null, null, null, null, null));
                    File.WriteAllText(Path.Combine(folder, "split-reply.txt"), splitReply.Text);
                    Console.WriteLine("[LIVE] Split GGUF import and inference passed: " + splitReply.Text);
                    await client.StreamAsync("/api/generate", new { model = splitModel, keep_alive = "0", stream = true }, Progress, CancellationToken.None);
                }
                finally { await client.DeleteAsync(splitModel, CancellationToken.None); }
                var remaining = await client.ModelsAsync(CancellationToken.None);
                if (remaining.Exists(entry => entry.Name.StartsWith(splitModel, StringComparison.Ordinal))) throw new IOException("Temporary split model was not deleted");
                Console.WriteLine("[LIVE] Temporary split model deletion verified");
            }
            settings["ai_local_model"] = model;
            settings["ai_local_tools"] = true;
            LocalTools.Verify(settings);
            var toolSettings = LocalTools.Settings(settings);
            var toolSchemas = new System.Collections.Generic.List<object> { PyDict.Of("type", "function", "function", PyDict.Of("name", "test_local_capability", "description", "Call this function when asked to verify local tools.", "parameters", PyDict.Of("type", "object", "properties", new PyDict()))) };
            bool called = false;
            foreach (var frame in LocalTools.Chat(toolSettings, new System.Collections.Generic.List<object> { PyDict.Of("role", "user", "content", "请调用 test_local_capability 函数验证本地工具，不要只用文字回答。") }, toolSchemas, 0, null))
                if (Py.S(frame.Get("type")) == "tool_calls") called = true;
            if (!called) throw new IOException("Local model did not call the requested test tool");
            Console.WriteLine("[LIVE] Real local model emitted a complete tool call; no action executed");
            await client.StreamAsync("/api/generate", new { model = imported, keep_alive = "0", stream = true }, Progress, CancellationToken.None);
            await client.StreamAsync("/api/generate", new { model, keep_alive = "0", stream = true }, Progress, CancellationToken.None);
            Console.WriteLine("[LIVE] Model unloaded. Runtime will now stop.");
            return 0;
        }
        finally { runtime.Stop(); }
    }
}
