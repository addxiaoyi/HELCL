using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class CatalogTranslationTests
{
    [Fact]
    public void LocalTranslationSendsInstructionsAndLearnsSearchableNames()
    {
        using var home = new TestHome();
        using var server = new TranslationServer();
        var project = PyDict.Of("source", "modrinth", "kind", "resourcepack", "slug", "cobalt-fixture",
            "name", "Cobalt Fixture", "original_description", "A blue resource pack.");
        var translated = CatalogPresentation.Translate(Py.L(project), PyDict.Of("ai_local_url", server.Endpoint));
        var row = Assert.IsType<PyDict>(Assert.Single(translated));
        Assert.Equal("钴蓝测试材质", row["chinese_name"]);
        Assert.Equal("Cobalt Fixture", row["name"]);
        Assert.Contains("Cobalt Fixture", ResourceNames.Queries("resourcepack", "钴蓝测试材质"));
        Assert.Contains("中文", server.SystemPrompt);
        Assert.Contains("不编造", server.SystemPrompt);
        Assert.Equal(1, server.ChatCalls);
        CatalogPresentation.Translate(Py.L(project), PyDict.Of("ai_local_url", server.Endpoint));
        Assert.Equal(1, server.ChatCalls);
    }

    [Fact]
    public void DocumentTranslationNeverSendsLinksAndCodeAsModelWritableText()
    {
        using var home = new TestHome();
        using var server = new TranslationServer();
        const string original = "Read [instructions](https://example.com/docs) and edit `config.json`.\n";
        string key = Py.S(CatalogDocument.Create("modrinth", "fixture", "description", "", original)["key"]);
        string translated = CatalogDocument.TranslatePart(key, 0, original, PyDict.Of("ai_local_url", server.Endpoint));
        Assert.Contains("https://example.com/docs", translated);
        Assert.Contains("[instructions](https://example.com/docs)", translated);
        Assert.Contains("`config.json`", translated);
        Assert.DoesNotContain("https://example.com/docs", server.UserPrompt);
        Assert.DoesNotContain("config.json", server.UserPrompt);
        Assert.EndsWith("\n", translated);
    }

    private sealed class TranslationServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _server;
        public string Endpoint { get; }
        public string SystemPrompt { get; private set; } = "";
        public string UserPrompt { get; private set; } = "";
        public int ChatCalls { get; private set; }

        public TranslationServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Endpoint = "http://127.0.0.1:" + ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            _listener.Prefixes.Add(Endpoint + "/");
            _listener.Start();
            _server = Task.Run(Serve);
        }

        private async Task Serve()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext request;
                try { request = await _listener.GetContextAsync(); }
                catch (HttpListenerException) when (!_listener.IsListening) { return; }
                catch (ObjectDisposedException) { return; }
                using var reader = new StreamReader(request.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();
                object reply = new { };
                switch (request.Request.Url.AbsolutePath)
                {
                    case "/api/tags": reply = new { models = new[] { new { name = "fixture:1b", size = 1000000 } } }; break;
                    case "/api/show": break;
                    case "/api/chat":
                        ChatCalls++;
                        using (var chat = JsonDocument.Parse(body))
                        {
                            var messages = chat.RootElement.GetProperty("messages");
                            SystemPrompt = messages[0].TryGetProperty("content", out var prompt) ? prompt.GetString() : "";
                            UserPrompt = messages[1].GetProperty("content").GetString();
                            if (SystemPrompt.StartsWith("Translate all English"))
                            {
                                using var fragment = JsonDocument.Parse(UserPrompt);
                                reply = new { message = new { content = JsonSerializer.Serialize(new { text = "中文说明 " + fragment.RootElement.GetProperty("text").GetString() }) } };
                            }
                            else
                            {
                                using var projects = JsonDocument.Parse(UserPrompt);
                                var items = projects.RootElement.EnumerateArray().Select(row => new {
                                    id = row.GetProperty("id").GetString(), name = "钴蓝测试材质", summary = "一款蓝色的资源包。" }).ToArray();
                                reply = new { message = new { content = JsonSerializer.Serialize(new { items }) } };
                            }
                        }
                        break;
                    default: request.Response.StatusCode = 404; break;
                }
                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(reply));
                request.Response.ContentType = "application/json";
                request.Response.ContentLength64 = bytes.Length;
                await request.Response.OutputStream.WriteAsync(bytes);
                request.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Close();
            _server.GetAwaiter().GetResult();
        }
    }
}
