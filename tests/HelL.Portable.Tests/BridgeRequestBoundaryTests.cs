using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

public sealed class BridgeRequestBoundaryTests
{
    [Theory]
    [InlineData("GET /health", true, false, 400)]
    [InlineData("POST /missing", true, false, 404)]
    [InlineData("POST /rpc", false, false, 401)]
    [InlineData("POST /rpc", true, true, 403)]
    public async Task AnUnconsumedBodyCannotBecomeAnotherRequest(string target, bool authorized, bool blockedOrigin, int status)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        var retained = new List<object> { "retained" };
        fixture.Api._inst_cache = retained;
        string injected = Post(bridge, """{"jsonrpc":"2.0","id":99,"method":"invalidate_instances","params":{}}""");
        string first = target + " HTTP/1.1\r\nHost: localhost\r\nContent-Length: " + Encoding.UTF8.GetByteCount(injected) + "\r\n"
            + (authorized ? BridgeServer.TOKEN_HEADER + ": " + bridge.Token + "\r\n" : "")
            + (blockedOrigin ? "Origin: https://untrusted.invalid\r\n" : "") + "\r\n";
        string response = await Exchange(bridge, first + injected);
        Assert.Same(retained, fixture.Api._inst_cache);
        Assert.StartsWith("HTTP/1.1 " + status + " ", response);
        Assert.Equal(1, response.Split("HTTP/1.1 ").Length - 1);
        Assert.Contains("Connection: close\r\n", response);
    }

    [Theory]
    [InlineData("Transfer-Encoding: chunked\r\nContent-Length: 2")]
    [InlineData("Transfer-Encoding: identity\r\nContent-Length: 2")]
    [InlineData("Content-Length: 2\r\nContent-Length: 3")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2")]
    [InlineData("Content-Length: +2")]
    [InlineData("Content-Length: -1")]
    [InlineData("Content-Length : 2")]
    [InlineData("Content-Length\0: 2")]
    [InlineData("Missing-Colon")]
    [InlineData("Content-Length: 2\r\nX-Test: value\r\n continuation")]
    public async Task InvalidFramingIsRejectedBeforeContinueOrDispatch(string framing)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string request = $"POST /rpc HTTP/1.1\r\nHost: localhost\r\n{BridgeServer.TOKEN_HEADER}: {bridge.Token}\r\n"
            + framing + "\r\nExpect: 100-continue\r\n\r\n{}";
        string response = await Exchange(bridge, request);
        Assert.StartsWith("HTTP/1.1 400 ", response);
        Assert.DoesNotContain("100 Continue", response);
        Assert.Equal(1, response.Split("HTTP/1.1 ").Length - 1);
        Assert.Contains("Connection: close\r\n", response);
    }

    [Fact]
    public async Task EndOfStreamDoesNotSubstituteForTheHeaderTerminator()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string response = await Exchange(bridge, $"GET /health HTTP/1.1\r\nHost: localhost\r\n{BridgeServer.TOKEN_HEADER}: {bridge.Token}\r\n");
        Assert.StartsWith("HTTP/1.1 400 ", response);
    }

    [Theory]
    [InlineData("\"jsonrpc\":\"1.0\",\"id\":1,\"params\":{}", -32600)]
    [InlineData("\"jsonrpc\":2,\"id\":1,\"params\":{}", -32600)]
    [InlineData("\"jsonrpc\":null,\"id\":1,\"params\":{}", -32600)]
    [InlineData("\"id\":{},\"params\":{}", -32600)]
    [InlineData("\"id\":[],\"params\":{}", -32600)]
    [InlineData("\"id\":true,\"params\":{}", -32600)]
    [InlineData("\"id\":1,\"params\":false", -32602)]
    [InlineData("\"id\":1,\"params\":0", -32602)]
    [InlineData("\"id\":1,\"params\":\"\"", -32602)]
    [InlineData("\"id\":1,\"params\":null", -32602)]
    [InlineData("\"id\":1,\"params\":{},\"params\":{}", -32600)]
    [InlineData("\"id\":1,\"params\":{},\"method\":\"invalidate_instances\"", -32600)]
    public async Task InvalidEnvelopesCannotInvokeTheOwner(string fields, int code)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        var retained = new List<object> { "retained" };
        fixture.Api._inst_cache = retained;
        string body = "{\"method\":\"invalidate_instances\"," + fields + "}";
        string response = await Exchange(bridge, Post(bridge, body));
        Assert.Same(retained, fixture.Api._inst_cache);
        Assert.StartsWith("HTTP/1.1 400 ", response);
        using JsonDocument reply = JsonDocument.Parse(Body(response));
        Assert.Equal(code, reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task JsonMustBeFiniteAndHaveABoundedNestingDepth()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        foreach (string body in new[]
        {
            """{"id":NaN,"method":"get_settings","params":{}}""",
            "{\"id\":1,\"method\":\"get_settings\",\"params\":{\"nested\":" + new string('[', 80) + "0" + new string(']', 80) + "}}"
        })
        {
            string response = await Exchange(bridge, Post(bridge, body));
            Assert.StartsWith("HTTP/1.1 400 ", response);
            using JsonDocument reply = JsonDocument.Parse(Body(response));
            Assert.Equal(-32700, reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }
    }

    [Theory]
    [InlineData("1e400", false)]
    [InlineData("-1e400", false)]
    [InlineData("1e400", true)]
    [InlineData("-1e400", true)]
    public async Task FloatingPointOverflowCannotReachTheOwner(string number, bool nested)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        var retained = new List<object> { "retained" };
        fixture.Api._inst_cache = retained;
        string body = nested
            ? "{\"id\":1,\"method\":\"save_settings\",\"params\":{\"data\":{\"instances_dir\":\".minecraft\"},\"ignored\":{\"values\":[" + number + "]}}}"
            : "{\"id\":" + number + ",\"method\":\"invalidate_instances\",\"params\":{}}";
        string response = await Exchange(bridge, Post(bridge, body));
        Assert.Same(retained, fixture.Api._inst_cache);
        Assert.StartsWith("HTTP/1.1 400 ", response);
        using JsonDocument reply = JsonDocument.Parse(Body(response));
        Assert.Equal(-32600, reply.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.DoesNotContain("Infinity", Body(response));
    }

    [Theory]
    [InlineData("1.25")]
    [InlineData("-2.5e2")]
    public async Task FiniteFloatingPointIdsAndNestedArgumentsRemainSupported(string number)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string response = await Exchange(bridge, Post(bridge, "{\"id\":" + number + ",\"method\":\"task_title\",\"params\":[" + number + "]}"));
        Assert.StartsWith("HTTP/1.1 200 ", response);
        using JsonDocument expected = JsonDocument.Parse(number);
        using JsonDocument reply = JsonDocument.Parse(Body(response));
        Assert.Equal(expected.RootElement.GetDouble(), reply.RootElement.GetProperty("id").GetDouble());
        Assert.Equal(expected.RootElement.GetDouble(), reply.RootElement.GetProperty("result").GetDouble());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(401)]
    public async Task ArbitraryPrecisionIntegerIdsAndArgumentsRemainSupported(int digits)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string number = "1" + new string('0', digits - 1);
        string response = await Exchange(bridge, Post(bridge, "{\"id\":" + number + ",\"method\":\"task_title\",\"params\":[" + number + "]}"));
        Assert.StartsWith("HTTP/1.1 200 ", response);
        using JsonDocument reply = JsonDocument.Parse(Body(response));
        Assert.Equal(number, reply.RootElement.GetProperty("id").GetRawText());
        Assert.Equal(number, reply.RootElement.GetProperty("result").GetRawText());
    }

    [Fact]
    public async Task AConsumedMalformedBodyCanBeFollowedByAValidRequest()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string response = await Exchange(bridge, Post(bridge, "not-json", close: false)
            + Post(bridge, """{"id":2,"method":"get_settings","params":{}}"""));
        Assert.StartsWith("HTTP/1.1 400 ", response);
        Assert.Contains("HTTP/1.1 200 ", response);
        Assert.Equal(2, response.Split("HTTP/1.1 ").Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AggregateHeadersAreBoundedBeforeContinueAuthenticationOrDispatch(bool authorized)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        var retained = new List<object> { "retained" };
        fixture.Api._inst_cache = retained;
        const string body = """{"id":1,"method":"invalidate_instances","params":{}}""";
        var headers = new StringBuilder("Host: localhost\r\nExpect: 100-continue\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n");
        if (authorized) headers.Append(BridgeServer.TOKEN_HEADER).Append(": ").Append(bridge.Token).Append("\r\n");
        for (int index = 0; index < 70; index++) headers.Append("X-Padding-").Append(index).Append(": ").Append('x', 1024).Append("\r\n");
        Assert.True(Encoding.ASCII.GetByteCount(headers.ToString()) > 64 * 1024);
        Assert.All(headers.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries), line => Assert.True(line.Length < 64 * 1024));
        string response = await Exchange(bridge, "POST /rpc HTTP/1.1\r\n" + headers + "\r\n" + body);
        Assert.Same(retained, fixture.Api._inst_cache);
        Assert.StartsWith("HTTP/1.1 431 ", response);
        Assert.DoesNotContain("100 Continue", response);
        Assert.Contains("Connection: close\r\n", response);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(1, 431)]
    public async Task AggregateHeaderBudgetCountsWireBytesExactly(int extraBytes, int status)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string prefix = $"Host: localhost\r\n{BridgeServer.TOKEN_HEADER}: {bridge.Token}\r\nConnection: close\r\nX-Padding: ";
        int padding = 64 * 1024 + extraBytes - Encoding.ASCII.GetByteCount(prefix + "\r\n\r\n");
        string headers = prefix + new string('x', padding) + "\r\n\r\n";
        Assert.Equal(64 * 1024 + extraBytes, Encoding.ASCII.GetByteCount(headers));
        string response = await Exchange(bridge, "GET /health HTTP/1.1\r\n" + headers);
        Assert.StartsWith("HTTP/1.1 " + status + " ", response);
    }

    [Fact]
    public async Task ExistingHeaderLineLimitStillRejectsAnOversizedLine()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string response = await Exchange(bridge, "GET /health HTTP/1.1\r\nX-Padding: " + new string('x', 64 * 1024 + 1) + "\r\n\r\n");
        Assert.StartsWith("HTTP/1.1 431 ", response);
        Assert.Contains("Connection: close\r\n", response);
    }

    [Fact]
    public async Task SmallValidRequestsContinueToReuseTheirConnection()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        string response = await Exchange(bridge, Post(bridge, """{"id":1,"method":"get_settings","params":{}}""", close: false)
            + Post(bridge, """{"id":2,"method":"get_settings","params":{}}"""));
        Assert.Equal(2, response.Split("HTTP/1.1 200 ").Length - 1);
        Assert.Equal(2, response.Split("HTTP/1.1 ").Length - 1);
    }

    private static string Post(BridgeServer bridge, string body, bool close = true) =>
        $"POST /rpc HTTP/1.1\r\nHost: localhost\r\n{BridgeServer.TOKEN_HEADER}: {bridge.Token}\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
        + (close ? "Connection: close\r\n" : "") + "\r\n" + body;
    private static string Body(string response) => response[(response.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..];
    private static async Task<string> Exchange(BridgeServer bridge, string request)
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, bridge.Port);
        using NetworkStream stream = socket.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(request));
        socket.Client.Shutdown(SocketShutdown.Send);
        using var received = new MemoryStream();
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0) received.Write(buffer, 0, count);
        }
        catch (IOException) when (received.Length > 0) { }
        return Encoding.UTF8.GetString(received.ToArray());
    }
}
