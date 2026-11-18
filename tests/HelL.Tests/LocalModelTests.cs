using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Local;
using Xunit;

namespace HelL.Tests;

public sealed class LocalModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hell-model-tests-" + Guid.NewGuid().ToString("N"));
    public LocalModelTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);

    [Theory]
    [InlineData("http://localhost:11434", "http://localhost:11434")]
    [InlineData("http://127.0.0.1:11435/", "http://127.0.0.1:11435")]
    [InlineData("http://[::1]:11434", "http://[::1]:11434")]
    public void AcceptsOnlyLoopbackService(string url, string expected) => Assert.Equal(expected, ModelRules.Endpoint(url));

    [Theory]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://user:password@localhost:11434")]
    [InlineData("file:///C:/models")]
    [InlineData("http://192.168.1.3:11434")]
    [InlineData("http://localhost:11434/?key=secret")]
    public void RejectsAmbiguousLocalEndpoint(string url) => Assert.Throws<ArgumentException>(() => ModelRules.Endpoint(url));

    [Theory]
    [InlineData("../../bad")]
    [InlineData("qwen3;whoami")]
    [InlineData("https://host/model")]
    [InlineData("qwen3:235b-cloud")]
    public void RejectsNonLocalOrInvalidModelNames(string name) => Assert.Throws<ArgumentException>(() => ModelRules.ModelName(name));

    [Fact]
    public void DownloadAvailabilityRequiresOnlineRuntimeAndSupportedHardware()
    {
        var fit = ModelRules.Assess(512 * 1024 * 1024, new ModelHardware { AvailableMemory = 8 * ModelRules.GiB, FreeDisk = 8 * ModelRules.GiB });
        Assert.False(ModelRules.CanDownload(false, true, fit));
        Assert.False(ModelRules.CanDownload(true, false, fit));
        Assert.False(ModelRules.CanDownload(true, true, new ModelFit { CanDownload = false }));
        Assert.True(ModelRules.CanDownload(true, true, fit));
    }

    [Fact]
    public void RecommendationHandlesUnrepresentableModelSizeWithoutThrowing()
    {
        var hardware = new ModelHardware { AvailableMemory = long.MaxValue, FreeDisk = long.MaxValue };
        var fit = ModelRules.Assess(long.MaxValue, hardware, import: true);
        Assert.False(fit.CanDownload);
        Assert.Equal("unknown", fit.Level);
        Assert.True(fit.EstimatedMemory >= 0);
    }

    [Fact]
    public void RecommendationUsesAvailableMemoryAndImportDiskOverhead()
    {
        var hardware = new ModelHardware { TotalMemory = 64 * ModelRules.GiB, AvailableMemory = 4 * ModelRules.GiB, FreeDisk = 8 * ModelRules.GiB };
        Assert.Equal("large", ModelRules.Assess(4 * ModelRules.GiB, hardware).Level);
        Assert.False(ModelRules.Assess(4 * ModelRules.GiB, hardware, true).CanDownload);
        Assert.Equal("recommended", ModelRules.Assess(512 * 1024 * 1024, hardware).Level);
        Assert.Equal("unknown", ModelRules.Assess(0, hardware).Level);
        Assert.Equal("unknown", ModelRules.Assess(ModelRules.GiB, new ModelHardware()).Level);
    }

    [Fact]
    public async Task DownloadResumesAndVerifiesBeforePublishing()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("a complete model fixture");
        string target = Path.Combine(_folder, "model.gguf");
        await File.WriteAllBytesAsync(target + ".partial", bytes.Take(5).ToArray());
        using var http = Http(request =>
        {
            Assert.Equal(5, request.Headers.Range.Ranges.Single().From);
            var response = Bytes(bytes.Skip(5).ToArray(), HttpStatusCode.PartialContent);
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(5, bytes.Length - 1, bytes.Length);
            return response;
        });
        await new ModelDownload(http).DownloadAsync(new Uri("https://fixture/model"), target, bytes.Length, Hash(bytes), (_, _, _) => { }, CancellationToken.None);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public async Task IgnoredRangeRestartsWithoutAppending()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("whole file");
        string target = Path.Combine(_folder, "range.gguf");
        await File.WriteAllTextAsync(target + ".partial", "bad");
        using var http = Http(_ => Bytes(bytes));
        await new ModelDownload(http).DownloadAsync(new Uri("https://fixture/model"), target, bytes.Length, Hash(bytes), (_, _, _) => { }, CancellationToken.None);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task WrongResumeRangeDoesNotAlterPartialFile()
    {
        string target = Path.Combine(_folder, "range.gguf");
        await File.WriteAllTextAsync(target + ".partial", "abc");
        using var http = Http(_ => { var r = Bytes(new byte[7], HttpStatusCode.PartialContent); r.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 6, 10); return r; });
        await Assert.ThrowsAsync<IOException>(() => new ModelDownload(http).DownloadAsync(new Uri("https://fixture/model"), target, 10, new string('a', 64), (_, _, _) => { }, CancellationToken.None));
        Assert.Equal("abc", await File.ReadAllTextAsync(target + ".partial"));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task ShortRangeEndCannotBePublishedAsAFullModel()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("whole model");
        string target = Path.Combine(_folder, "end.gguf");
        await File.WriteAllBytesAsync(target + ".partial", bytes.Take(3).ToArray());
        using var http = Http(_ =>
        {
            var reply = Bytes(bytes.Skip(3).ToArray(), HttpStatusCode.PartialContent);
            reply.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, bytes.Length); return reply;
        });
        await Assert.ThrowsAsync<IOException>(() => new ModelDownload(http).DownloadAsync(new Uri("https://fixture/model"), target, bytes.Length, Hash(bytes), (_, _, _) => { }, CancellationToken.None));
        Assert.Equal(bytes.Take(3), await File.ReadAllBytesAsync(target + ".partial")); Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task CorruptDownloadIsNeverInstalled()
    {
        string target = Path.Combine(_folder, "bad.gguf");
        using var http = Http(_ => Bytes(new byte[10]));
        await Assert.ThrowsAsync<IOException>(() => new ModelDownload(http).DownloadAsync(new Uri("https://fixture/model"), target, 10, new string('a', 64), (_, _, _) => { }, CancellationToken.None));
        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public async Task CancellationKeepsPartialAndNeverPublishesTarget()
    {
        string target = Path.Combine(_folder, "cancel.gguf");
        byte[] bytes = new byte[500000];
        using var http = Http(_ => Bytes(bytes));
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModelDownload(http).DownloadAsync(new Uri("https://fixture/model"), target, bytes.Length, Hash(bytes), (_, _, _) => cancel.Cancel(), cancel.Token));
        Assert.False(File.Exists(target));
        Assert.True(new FileInfo(target + ".partial").Length > 0);
    }

    [Fact]
    public async Task PullRequiresExplicitTerminalSuccess()
    {
        using var client = new OllamaClient("http://localhost:11434", new Handler(_ => Json("{\"status\":\"pulling\",\"total\":50,\"completed\":20}\n")));
        await Assert.ThrowsAsync<IOException>(() => client.StreamAsync("/api/pull", new { model = "qwen3:4b" }, (_, _, _) => { }, CancellationToken.None));
    }

    [Fact]
    public async Task PullReportsProgressAndUpstreamError()
    {
        int seen = 0;
        using var client = new OllamaClient("http://localhost:11434", new Handler(_ => Json("{\"status\":\"pulling\",\"total\":50,\"completed\":20}\n{\"error\":\"disk full\"}\n")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.StreamAsync("/api/pull", new { model = "qwen3:4b" }, (current, total, _) => { Assert.Equal(20, current); Assert.Equal(50, total); seen++; }, CancellationToken.None));
        Assert.Contains("disk full", error.Message);
        Assert.Equal(1, seen);
    }

    [Fact]
    public async Task DiscoversModelsWithSizeAndQuantization()
    {
        using var client = new OllamaClient("http://localhost:11434", new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return Json("{\"models\":[{\"name\":\"qwen3:0.6b\",\"size\":500,\"details\":{\"parameter_size\":\"0.6B\",\"quantization_level\":\"Q4_K_M\"}}]}");
        }));
        var model = Assert.Single(await client.ModelsAsync(CancellationToken.None));
        Assert.Equal("qwen3:0.6b", model.Name);
        Assert.Equal("Q4_K_M", model.Quantization);
        Assert.Equal(500, model.Size);
    }

    [Fact]
    public async Task RepositoryPinsCommitAndRejectsSplitOrUnverifiedFiles()
    {
        using var http = Http(request =>
        {
            Assert.Equal("mirror.example", request.RequestUri.Host);
            Assert.Null(request.Headers.Authorization);
            return Json("{\"sha\":\"" + new string('b', 40) + "\",\"siblings\":[{\"rfilename\":\"Q4_K_M.gguf\",\"lfs\":{\"size\":100,\"sha256\":\"" + new string('a', 64) + "\"}},{\"rfilename\":\"model-00001-of-00002.gguf\"},{\"rfilename\":\"unverified.gguf\"}]}");
        });
        var repository = new ModelRepository(http, "https://mirror.example");
        var files = await repository.FilesAsync("author/model", new ModelHardware(), CancellationToken.None);
        Assert.Single(files, file => file.Supported);
        var file = files.Single(file => file.Supported);
        Assert.Contains("/resolve/" + new string('b', 40) + "/", repository.FileUri(file).AbsoluteUri);
        Assert.Equal(2, files.Count(file => !file.Supported));
    }

    [Theory]
    [InlineData("../outside.exe")]
    [InlineData("C:\\outside.exe")]
    public void RuntimeExtractionCannotEscapeStaging(string entry) => Assert.Throws<IOException>(() => LocalRuntime.SafeArchivePath(_folder, entry));

    [Fact]
    public void PlainChatFiltersToolHistoryAndBoundsContext()
    {
        var history = new List<object> { PyDict.Of("role", "system", "content", "private tools"), PyDict.Of("role", "tool", "content", "file result"), PyDict.Of("role", "user", "content", "previous"), PyDict.Of("role", "assistant", "content", "answer") };
        var messages = LocalChat.BuildMessages(history, "hello", 25).Cast<PyDict>().ToList();
        Assert.DoesNotContain(messages, message => Py.S(message.Get("role")) == "tool");
        Assert.Equal("hello", messages.Last()["content"]);
        Assert.Throws<AIClientError>(() => LocalChat.BuildMessages(history, new string('x', 30), 25));
    }

    [Fact]
    public void FolderScanFindsExistingModelsAndSkipsOtherFiles()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "models"));
        File.WriteAllText(Path.Combine(_folder, "models", "qwen.gguf"), "GGUF");
        File.WriteAllText(Path.Combine(_folder, "ignore.bin"), "not a model");
        Assert.Equal("qwen.gguf", Assert.Single(LocalModelService.ScanFolder(_folder)).Name);
    }

    [Fact]
    public void OfficialChecksumMatchesExactAssetName()
    {
        string hash = new string('b', 64);
        Assert.Equal(hash, LocalRuntime.ChecksumFor(hash + "  ./ollama-windows-amd64.zip\n", "ollama-windows-amd64.zip"));
        Assert.Throws<IOException>(() => LocalRuntime.ChecksumFor(hash + "  ./ollama-windows-arm64.zip", "ollama-windows-amd64.zip"));
    }

    [Fact]
    public async Task CloudAliasesAreExcludedFromDiscovery()
    {
        using var client = new OllamaClient("http://localhost:11434", new Handler(_ => Json("{\"models\":[{\"name\":\"local:latest\"},{\"name\":\"alias\",\"remote_model\":\"cloud\"},{\"name\":\"gpt:cloud-cloud\"}]}")));
        Assert.Equal("local:latest", Assert.Single(await client.ModelsAsync(CancellationToken.None)).Name);
    }

    [Fact]
    public async Task InvalidGgufNeverReachesImportApi()
    {
        string file = Path.Combine(_folder, "invalid.gguf");
        await File.WriteAllTextAsync(file, "this is not a model file");
        int requests = 0;
        using var client = new OllamaClient("http://localhost:11434", new Handler(_ => { requests++; return Json("{}"); }));
        await Assert.ThrowsAsync<IOException>(() => client.ImportAsync(file, "local/test", (_, _, _) => { }, CancellationToken.None));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Mirror308PreservesRangeAndRejectsDowngrade()
    {
        int count = 0;
        using var http = Http(request =>
        {
            Assert.Equal(25, request.Headers.Range.Ranges.Single().From);
            if (count++ == 0)
            {
                var redirect = new HttpResponseMessage((HttpStatusCode)308);
                redirect.Headers.Location = new Uri("https://cdn.example/model");
                return redirect;
            }
            Assert.Equal("cdn.example", request.RequestUri.Host);
            return Bytes(new byte[10]);
        });
        using var response = await ModelHttp.GetAsync(http, new Uri("https://mirror.example/model"), 25, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var insecure = Http(_ => { var redirect = new HttpResponseMessage((HttpStatusCode)308); redirect.Headers.Location = new Uri("http://cdn.example/model"); return redirect; });
        await Assert.ThrowsAsync<IOException>(() => ModelHttp.GetAsync(insecure, new Uri("https://mirror.example/model"), 0, CancellationToken.None));
    }

    private static HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> send) => new HttpClient(new Handler(send));

    [Fact]
    public async Task DiscoverySuggestsOtherServiceWithoutChangingTheActiveEndpoint()
    {
        var addresses = new List<string>();
        var discovery = new LocalModelDiscovery(address => new OllamaClient(address, new Handler(request =>
        {
            addresses.Add(address);
            if (address.EndsWith(":12345")) throw new HttpRequestException("offline");
            return Json(request.RequestUri.AbsolutePath switch
            {
                "/api/version" => "{\"version\":\"test\"}",
                "/api/tags" => "{\"models\":[{\"name\":\"qwen3:4b\"}]}",
                _ => "{\"models\":[]}"
            });
        })));
        var status = await discovery.FindAsync("http://127.0.0.1:12345", true, CancellationToken.None);
        Assert.Equal("http://127.0.0.1:12345", status.Endpoint);
        Assert.False(status.Online);
        Assert.Empty(status.Models);
        Assert.Equal(ModelRules.DefaultEndpoint, status.DiscoveredEndpoint);
        Assert.DoesNotContain(ModelRules.ManagedEndpoint, addresses);
    }

    [Fact]
    public async Task ExplicitConnectionDoesNotFallBackToAnotherService()
    {
        int requests = 0;
        var discovery = new LocalModelDiscovery(address => new OllamaClient(address, new Handler(_ =>
        { requests++; throw new HttpRequestException("offline"); })));
        var status = await discovery.FindAsync("http://127.0.0.1:12345", false, CancellationToken.None);
        Assert.False(status.Online);
        Assert.Empty(status.DiscoveredEndpoint);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task MissingRunningStateDoesNotReplaceAWorkingConfiguredService()
    {
        var addresses = new List<string>();
        var discovery = new LocalModelDiscovery(address => new OllamaClient(address, new Handler(request =>
        {
            addresses.Add(address);
            return Json(request.RequestUri.AbsolutePath switch
            {
                "/api/version" => "{\"version\":\"test\"}",
                "/api/tags" => "{\"models\":[{\"name\":\"qwen3:4b\"}]}",
                _ => "{}"
            });
        })));
        var status = await discovery.FindAsync("http://127.0.0.1:12345", true, CancellationToken.None);
        Assert.True(status.Online);
        Assert.Single(status.Models);
        Assert.NotEmpty(status.Error);
        Assert.All(addresses, address => Assert.Equal(status.Endpoint, address));
        Assert.Empty(status.DiscoveredEndpoint);
    }
    private static HttpResponseMessage Bytes(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) => new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Json(string text) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _send;
        public Handler(Func<HttpRequestMessage, HttpResponseMessage> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(_send(request));
    }
}
