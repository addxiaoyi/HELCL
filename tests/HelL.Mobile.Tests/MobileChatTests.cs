using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;
using HelL.Core.Platform;
using HelL.Mobile.Ai;
using HelL.Portable;
using Xunit;

namespace HelL.Mobile.Tests;

public sealed class MobileChatTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-mobile-chat-" + Guid.NewGuid().ToString("N"));
    private readonly string? previousReplay = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
    private readonly HttpListener listener = new();
    private readonly Task server;
    private readonly ConcurrentQueue<string> requests = new();
    private string response = "data: {\"choices\":[{\"delta\":{\"content\":\"你好，\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Minecraft\"},\"finish_reason\":\"stop\"}]}\n\ndata: {\"choices\":[],\"usage\":{\"total_tokens\":12}}\n\ndata: [DONE]\n\n";
    private int status = 200;
    private bool stall;
    private readonly TaskCompletionSource releaseResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool responseFinished;
    private static MobileAiProfile Profile => new("custom", "https://fixture.invalid/v1", "fixture-model", "fictional-key");

    public MobileChatTests()
    {
        Directory.CreateDirectory(root);
        _ = new PortableLauncher(root);
        Config.CONFIG.Set("use_system_proxy", false);
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        string address = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(address);
        listener.Start();
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", address);
        server = Serve();
    }

    private async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { return; }
            try
            {
                using var reader = new StreamReader(context.Request.InputStream);
                requests.Enqueue(await reader.ReadToEndAsync());
                context.Response.StatusCode = status;
                context.Response.ContentType = status == 200 ? "text/event-stream" : "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(response);
                context.Response.SendChunked = true;
                await context.Response.OutputStream.WriteAsync(bytes);
                await context.Response.OutputStream.FlushAsync();
                if (stall) await releaseResponse.Task;
                responseFinished = true;
            }
            catch (Exception error) when (error is HttpListenerException or IOException or ObjectDisposedException) { }
            finally
            {
                try { context.Response.Close(); }
                catch (ObjectDisposedException) when (!listener.IsListening) { }
            }
        }
    }

    [Fact]
    public void StreamsConfiguredModelWithoutToolExecutionAndKeepsHistory()
    {
        var chat = new MobileChat();
        int paints = 0;
        chat.Send(Profile, "解释这个 Minecraft 问题", () => ++paints, default);
        Assert.True(paints >= 3);
        Assert.Equal("你好，Minecraft", chat.Entries.Last().Text);
        Assert.Equal("complete", chat.Entries.Last().State);
        Assert.True(requests.TryDequeue(out string? body));
        using var request = JsonDocument.Parse(body!);
        Assert.Equal("fixture-model", request.RootElement.GetProperty("model").GetString());
        Assert.False(request.RootElement.TryGetProperty("tools", out _));
        Assert.False(request.RootElement.TryGetProperty("tool_choice", out _));
        Assert.Equal(2, request.RootElement.GetProperty("messages").GetArrayLength());
        chat.Send(Profile, "再说明一次", () => { }, default);
        Assert.True(requests.TryDequeue(out body));
        using var continuation = JsonDocument.Parse(body!);
        Assert.Equal(4, continuation.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public void ProviderFailureIsNotMarkedComplete()
    {
        status = 401;
        response = "{\"error\":{\"message\":\"fixture denied\"}}";
        var chat = new MobileChat();
        Assert.Throws<AIClientError>(() => chat.Send(Profile, "你好", () => { }, default));
        Assert.Equal("failed", chat.Entries.Last().State);
        Assert.DoesNotContain(chat.Entries, entry => entry.Role == "assistant" && entry.State == "complete");
    }

    [Fact]
    public void StopsActiveStreamAndPreservesReceivedText()
    {
        response = "data: {\"choices\":[{\"delta\":{\"content\":\"部分回复\"}}]}\n\n";
        stall = true;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var chat = new MobileChat();
        try
        {
            Assert.Throws<OperationCanceledException>(() => chat.Send(Profile, "你好", () =>
            {
                if (chat.Entries.Last().Text.Length > 0 && !cancel.IsCancellationRequested) cancel.Cancel();
            }, cancel.Token));
            Assert.False(responseFinished);
        }
        finally { releaseResponse.TrySetResult(); }
        Assert.Equal("部分回复", chat.Entries.Last().Text);
        Assert.Equal("cancelled", chat.Entries.Last().State);
    }

    [Fact]
    public void RefusesToolCallsWithoutRunningThem()
    {
        response = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"fixture\",\"function\":{\"name\":\"delete_file\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\ndata: [DONE]\n\n";
        var chat = new MobileChat();
        Assert.Throws<InvalidOperationException>(() => chat.Send(Profile, "删除文件", () => { }, default));
        Assert.Equal("failed", chat.Entries.Last().State);
    }

    [Fact]
    public void ProfileNeverUsesImplicitBuiltInCredentials()
    {
        Config.CONFIG.Set("ai_base_url", "");
        Config.CONFIG.Set("ai_api_key", "");
        Config.CONFIG.Set("ai_mode", "custom");
        Assert.Throws<ArgumentException>(() => MobileAiProfile.Read(Config.CONFIG, new MemoryKeys()));
        Assert.Throws<ArgumentException>(() => MobileAiProfile.Validate("custom", "http://fixture.invalid", "model", "key"));
        Assert.DoesNotContain("fictional-key", Profile.ToString());
    }

    [Fact]
    public void ProfileSavesKeySeparatelyAndFailedReplacementPreservesPrevious()
    {
        var keys = new MemoryKeys();
        MobileAiProfile.Save(Config.CONFIG, keys, Profile);
        string oldId = Py.S(Config.CONFIG.Get("mobile_ai_key_id"));
        string disk = File.ReadAllText(Config.CONFIG_FILE);
        Assert.DoesNotContain("fictional-key", disk);
        Assert.Equal("fictional-key", MobileAiProfile.Read(Config.CONFIG, keys).ApiKey);
        keys.FailWrites = true;
        Assert.Throws<IOException>(() => MobileAiProfile.Save(Config.CONFIG, keys, Profile with { ApiKey = "replacement" }));
        Assert.Equal(oldId, Py.S(Config.CONFIG.Get("mobile_ai_key_id")));
        Assert.Equal("fictional-key", MobileAiProfile.Read(Config.CONFIG, keys).ApiKey);
        Assert.Single(keys.Values);
    }

    [Fact]
    public void FailedConfigurationCommitRollsBackNewKeyAndRetainsOldProfile()
    {
        var keys = new MemoryKeys();
        MobileAiProfile.Save(Config.CONFIG, keys, Profile);
        string oldId = Py.S(Config.CONFIG.Get("mobile_ai_key_id"));
        byte[] saved = File.ReadAllBytes(Config.CONFIG_FILE);
        string previousFile = Config.CONFIG_FILE + ".rollback-fixture";
        File.Move(Config.CONFIG_FILE, previousFile);
        try
        {
            // Linux permits replacing an open file; a directory blocks file replacement on both platforms.
            Directory.CreateDirectory(Config.CONFIG_FILE);
            Assert.ThrowsAny<Exception>(() => MobileAiProfile.Save(Config.CONFIG, keys, Profile with { Model = "different-model", ApiKey = "replacement" }));
        }
        finally
        {
            if (Directory.Exists(Config.CONFIG_FILE)) Directory.Delete(Config.CONFIG_FILE);
            File.Move(previousFile, Config.CONFIG_FILE);
        }
        Assert.Equal(saved, File.ReadAllBytes(Config.CONFIG_FILE));
        Assert.Equal(oldId, Py.S(Config.CONFIG.Get("mobile_ai_key_id")));
        Assert.Equal(Profile.Model, MobileAiProfile.Read(Config.CONFIG, keys).Model);
        Assert.Equal(Profile.ApiKey, MobileAiProfile.Read(Config.CONFIG, keys).ApiKey);
        Assert.Single(keys.Values);
    }

    [Fact]
    public void HttpCancellationDisposeIsIdempotentDuringAbortRace()
    {
        var cancel = new HttpCancel();
        cancel.Bind(new AiSession());
        Parallel.Invoke(cancel.Abort, cancel.Dispose, cancel.Dispose);
        cancel.Bind(new AiSession());
        cancel.Dispose();
        Assert.True(cancel.Cancelled());
    }

    private sealed class MemoryKeys : ISecretStore
    {
        internal readonly Dictionary<string, string> Values = [];
        internal bool FailWrites;
        public string GetPassword(string service, string username) => Values.GetValueOrDefault(username, "");
        public void SetPassword(string service, string username, string password)
        {
            if (FailWrites) throw new IOException("Fixture keyring locked.");
            Values[username] = password;
        }
        public void DeletePassword(string service, string username) => Values.Remove(username);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", previousReplay);
        releaseResponse.TrySetResult();
        listener.Close();
        if (!server.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture server did not stop.");
        Directory.Delete(root, recursive: true);
    }
}
