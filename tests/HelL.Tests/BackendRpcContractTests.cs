using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class BackendRpcContractTests
{
    [Theory]
    [InlineData("instance_visual", "instance:String|required;version:String|\"\"")]
    [InlineData("instance_screenshots", "instance:String|required;offset:Int32|0;limit:Int32|24")]
    [InlineData("set_instance_cover", "instance:String|required;path:String|required")]
    [InlineData("reset_instance_cover", "instance:String|required")]
    [InlineData("recent_play", "")]
    [InlineData("get_settings", "")]
    [InlineData("get_accounts", "")]
    [InlineData("get_instances", "")]
    [InlineData("wait_task", "task_id:Object|required;timeout:Object|1800;cancelled:Object|null")]
    [InlineData("shutdown", "timeout_ms:Object|800")]
    public void ClientFacingSignaturesKeepTheirNamesTypesAndDefaults(string name, string expected)
    {
        var method = Assert.Single(typeof(BackendApi).GetMethods(), candidate => candidate.Name == name);
        Assert.True(RpcDispatcher.IsExposedName(method.Name));
        Assert.Null(method.GetCustomAttribute<RpcHiddenAttribute>());
        Assert.Equal(expected, string.Join(";", method.GetParameters().Select(DescribeParameter)));
        Assert.False(typeof(Task).IsAssignableFrom(method.ReturnType), "The bridge serializes synchronous RPC results.");
    }

    [Fact]
    public void HighRiskRpcDefaultsAndPayloadKeysRemainStable()
    {
        var launch = typeof(BackendApi).GetMethod("launch_game")!;
        foreach (string name in new[] { "launch_game", "save_settings", "start_task" })
            Assert.False(typeof(Task).IsAssignableFrom(typeof(BackendApi).GetMethod(name)!.ReturnType), name + " must keep a synchronous RPC result.");
        Assert.Equal(new[] { "instance", "version", "account", "username", "memory_mb", "width", "height", "java", "extra_game_args", "force" }, launch.GetParameters().Select(parameter => parameter.Name));
        var java = launch.GetParameters().Single(parameter => parameter.Name == "java");
        var force = launch.GetParameters().Single(parameter => parameter.Name == "force");
        Assert.Equal("自动选择", java.GetCustomAttribute<DefAttribute>()?.Value);
        Assert.Equal(false, force.GetCustomAttribute<DefAttribute>()?.Value);

        var settings = typeof(BackendApi).GetMethod("save_settings")!.GetParameters();
        Assert.Equal(new[] { "data", "extra" }, settings.Select(parameter => parameter.Name));
        Assert.NotNull(settings[1].GetCustomAttribute<VarKwAttribute>());
        var start = typeof(BackendApi).GetMethod("start_task")!.GetParameters();
        Assert.Equal(new[] { "title", "fn", "args", "kwargs" }, start.Select(parameter => parameter.Name));
        Assert.NotNull(start[2].GetCustomAttribute<VarArgsAttribute>());
        Assert.NotNull(start[3].GetCustomAttribute<VarKwAttribute>());

        var wait = typeof(BackendApi).GetMethod("wait_task")!;
        Assert.Equal(new[] { "task_id", "timeout", "cancelled" }, wait.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(1800L, wait.GetParameters().Single(parameter => parameter.Name == "timeout").GetCustomAttribute<DefAttribute>()?.Value);

        using var fixture = new BackendFixture();
        var instances = Assert.IsType<List<object>>(fixture.Api.get_instances());
        var row = Assert.IsType<PyDict>(Assert.Single(instances));
        Assert.Equal(new[] { "group", "java", "java_label", "mc", "mc_version", "name", "pack", "pack_version", "versions" }, row.Keys.OrderBy(key => key));
        var waitResult = fixture.Api.wait_task("missing", 0L);
        Assert.Equal(new[] { "message", "ok", "task_id", "timeout" }, waitResult.Keys.OrderBy(key => key));
    }

    [Fact]
    public void LibraryRpcDefaultsAndErrorsMatchTheUiPayloads()
    {
        using var fixture = new BackendFixture();
        string instance = Py.S(Assert.Single(Instances.ListInstances()));
        var visual = Assert.IsType<PyDict>(fixture.Rpc.Call("instance_visual", PyDict.Of("instance", instance)));
        Assert.Equal(new[] { "kind", "modified", "path" }, visual.Keys.OrderBy(key => key));
        Assert.Equal("", visual["path"]);
        Assert.Equal("none", visual["kind"]);
        Assert.Equal(0L, visual["modified"]);
        var shots = Assert.IsType<PyDict>(fixture.Rpc.Call("instance_screenshots", new List<object> { instance }));
        Assert.Equal(new[] { "items", "offset", "total" }, shots.Keys.OrderBy(key => key));
        Assert.Empty(Assert.IsType<List<object>>(shots["items"]));
        Assert.Equal(0L, shots["offset"]);
        Assert.Equal(0L, shots["total"]);
        var recent = Assert.IsType<PyDict>(fixture.Rpc.Call("recent_play", new PyDict()));
        Assert.Equal(new[] { "available", "duration", "ended", "instance", "version" }, recent.Keys.OrderBy(key => key));
        Assert.False(Py.Truthy(recent["available"]));
        Assert.Throws<ArgumentException>(() => fixture.Rpc.Call("instance_visual", PyDict.Of("instance", "missing")));
        Assert.Throws<ArgumentException>(() => fixture.Rpc.Call("instance_screenshots", PyDict.Of("instance", instance, "limit", 0L)));
    }

    [Fact]
    public async Task AuthenticatedBridgeKeepsHealthAndJsonRpcEnvelopeAndMasksSecrets()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        using var http = Client(bridge);
        using var denied = await http.GetAsync("health");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        http.DefaultRequestHeaders.Add(BridgeServer.TOKEN_HEADER, bridge.Token);
        using var health = await http.GetAsync("health");
        health.EnsureSuccessStatusCode();
        using var status = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        Assert.True(status.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("tipcl-bridge", status.RootElement.GetProperty("name").GetString());

        Config.CONFIG.Set("ai_api_key", "contract-private-token");
        using var body = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":\"contract-1\",\"method\":\"get_settings\",\"params\":{}}", Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("rpc", body);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var envelope = json.RootElement;
        Assert.Equal(new[] { "id", "jsonrpc", "result" }, envelope.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("contract-1", envelope.GetProperty("id").GetString());
        Assert.Equal("2.0", envelope.GetProperty("jsonrpc").GetString());
        var settings = envelope.GetProperty("result");
        Assert.Equal("", settings.GetProperty("ai_api_key").GetString());
        Assert.True(settings.GetProperty("ai_api_key_set").GetBoolean());
        Assert.DoesNotContain("contract-private-token", settings.GetRawText());
    }

    [Theory]
    [InlineData("not-json", 400, -32700)]
    [InlineData("[]", 400, -32600)]
    [InlineData("{\"id\":7,\"method\":\"unknown_rpc\",\"params\":{}}", 200, -32601)]
    [InlineData("{\"id\":7,\"method\":\"instance_visual\",\"params\":{}}", 200, -32000)]
    public async Task BridgeFailuresKeepTheirHttpStatusAndRpcErrorCode(string request, int status, int error)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        using var http = Client(bridge);
        http.DefaultRequestHeaders.Add(BridgeServer.TOKEN_HEADER, bridge.Token);
        using var body = new StringContent(request, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("rpc", body);
        Assert.Equal(status, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "error", "id", "jsonrpc" }, json.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal(error, json.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("error").GetProperty("message").GetString()));
        if (status == 200) Assert.Equal(7, json.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task SseSendsHelloThenNamedUtf8PayloadsInOrder()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        using var http = Client(bridge);
        http.DefaultRequestHeaders.Add(BridgeServer.TOKEN_HEADER, bridge.Token);
        try
        {
            using var response = await http.GetAsync("events", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Assert.Equal("event: hello", await ReadLine(reader));
            using var hello = JsonDocument.Parse((await ReadLine(reader)).Substring(6));
            Assert.True(hello.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("", await ReadLine(reader));
            fixture.Bus.Emit("progress", PyDict.Of("task_id", "task-42", "current", 1L, "total", 2L, "message", "下载中"));
            fixture.Bus.Emit("finished", PyDict.Of("task_id", "task-42", "success", true, "message", "完成"));
            foreach (string name in new[] { "progress", "finished" })
            {
                Assert.Equal("event: " + name, await ReadLine(reader));
                using var payload = JsonDocument.Parse((await ReadLine(reader)).Substring(6));
                Assert.Equal("task-42", payload.RootElement.GetProperty("task_id").GetString());
                Assert.Equal(name == "progress" ? "下载中" : "完成", payload.RootElement.GetProperty("message").GetString());
                Assert.False(payload.RootElement.TryGetProperty("data", out _));
                Assert.Equal("", await ReadLine(reader));
            }
        }
        finally
        {
            bridge.Stop();
            // Wake the SSE queue so its blocking read cannot outlive the test fixture.
            fixture.Bus.Emit("test_shutdown", new PyDict());
            Assert.True(SpinWait.SpinUntil(() => fixture.Bus.SubscriberCount == 0, TimeSpan.FromSeconds(5)), "SSE subscription did not close.");
        }
    }

    private static HttpClient Client(BridgeServer bridge) => new HttpClient(new HttpClientHandler { UseProxy = false })
    {
        BaseAddress = new Uri($"http://127.0.0.1:{bridge.Port}/"),
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static async Task<string> ReadLine(StreamReader reader)
        => await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) ?? throw new EndOfStreamException("Bridge event stream ended early.");

    private static string DescribeParameter(ParameterInfo parameter)
    {
        var value = parameter.GetCustomAttribute<DefAttribute>();
        string fallback = value != null ? PyJson.Dumps(value.Value)
            : !parameter.HasDefaultValue ? "required"
            : parameter.DefaultValue == null ? "null" : PyJson.Dumps(parameter.DefaultValue);
        return parameter.Name + ":" + parameter.ParameterType.Name + "|" + fallback;
    }
}
