using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

public sealed class BackendBridgeHardeningTests
{
    [Fact]
    public void OverflowSignalsResynchronizationAndLeavesTheTerminalReceiptAvailable()
    {
        using var fixture = new BackendFixture();
        using var events = fixture.Bus.Subscribe();
        for (int index = 0; index < EventBus.QueueMax; index++)
            fixture.Bus.Emit("progress", PyDict.Of("task_id", "overflow-task", "current", (long)index));

        fixture.Api.Emit("finished", PyDict.Of("task_id", "overflow-task", "success", true, "message", "complete"));

        var reset = Assert.Single(events.ToArray());
        Assert.Equal("events_reset", reset["event"]);
        Assert.Equal("queue_overflow", Assert.IsType<PyDict>(reset["data"])["reason"]);
        Assert.True(events.IsAddingCompleted);
        Assert.Equal(0, fixture.Bus.SubscriberCount);
        var receipt = Assert.IsType<PyDict>(Assert.Single(Assert.IsType<List<object>>(fixture.Api.list_tasks()["finished"])));
        Assert.Equal("overflow-task", receipt["task_id"]);
        Assert.True(Py.Truthy(receipt["success"]));

        using var reconnected = fixture.Bus.Subscribe();
        fixture.Bus.Emit("ui_changed", new PyDict());
        Assert.Equal("ui_changed", Assert.Single(reconnected.ToArray())["event"]);
        fixture.Bus.Unsubscribe(reconnected);
    }

    [Fact]
    public void CompletedTaskTitlesAreEvictedAlongWithTheirRetainedReceipts()
    {
        using var fixture = new BackendFixture();
        using var finish = new ManualResetEventSlim();
        string running = fixture.Api.StartTask("still running", context =>
        {
            finish.Wait(context.Token);
            return "complete";
        });
        try
        {
            for (int index = 0; index < 90; index++)
            {
                string id = "history-" + index;
                lock (fixture.Api._lock) fixture.Api._titles[id] = "title " + index;
                fixture.Api.Emit("finished", PyDict.Of("task_id", id, "success", true, "message", "complete"));
            }

            var snapshot = fixture.Api.list_tasks();
            var retained = Assert.IsType<List<object>>(snapshot["finished"])
                .Cast<PyDict>().Select(row => Py.S(row["task_id"])).ToHashSet();
            retained.Add(running);
            lock (fixture.Api._lock) Assert.Equal(retained.Order(), fixture.Api._titles.Keys.Order());
            Assert.Equal("history-0", fixture.Api.task_title("history-0"));
            Assert.Equal("title 89", fixture.Api.task_title("history-89"));
            Assert.Equal("still running", fixture.Api.task_title(running));
        }
        finally { finish.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteHeadersAndBodiesExpire(bool body)
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, bridge.Port);
        using NetworkStream stream = socket.GetStream();
        string request = body
            ? $"POST /rpc HTTP/1.1\r\nHost: localhost\r\n{BridgeServer.TOKEN_HEADER}: {bridge.Token}\r\nContent-Length: 10\r\n\r\n{{"
            : "GET /health HTTP/1.1\r\nHost:";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        using var reader = new StreamReader(stream, Encoding.ASCII);
        string? status = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
        Assert.StartsWith("HTTP/1.1 408 ", status);
    }

    [Fact]
    public async Task TrickleHeadersCannotExtendTheRequestDeadline()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, bridge.Port);
        using NetworkStream stream = socket.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET /health HTTP/1.1\r\nX-Slow: "));
        using var stop = new CancellationTokenSource();
        Task trickle = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await stream.WriteAsync(new byte[] { (byte)'x' }, stop.Token);
                    await Task.Delay(100, stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (IOException) { }
            catch (SocketException) { }
        });
        try
        {
            using var reader = new StreamReader(stream, Encoding.ASCII);
            string? status = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Assert.StartsWith("HTTP/1.1 408 ", status);
        }
        finally
        {
            stop.Cancel();
            await trickle;
        }
    }

    [Fact]
    public async Task ExcessConnectionsAreClosedBeforeAnotherWorkerIsStarted()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        var sockets = new List<TcpClient>();
        try
        {
            for (int index = 0; index < 64; index++)
            {
                var socket = new TcpClient();
                sockets.Add(socket);
                await socket.ConnectAsync(IPAddress.Loopback, bridge.Port);
            }
            await Task.Delay(150);
            using var excess = new TcpClient();
            await excess.ConnectAsync(IPAddress.Loopback, bridge.Port);
            using NetworkStream stream = excess.GetStream();
            try
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET /health HTTP/1.1\r\nHost: localhost\r\n{BridgeServer.TOKEN_HEADER}: {bridge.Token}\r\n\r\n"));
                var response = new byte[1];
                Assert.Equal(0, await stream.ReadAsync(response).AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            }
            catch (IOException) { }
            catch (SocketException) { }
        }
        finally
        {
            foreach (TcpClient socket in sockets) socket.Dispose();
        }
    }

    [Fact]
    public async Task SseStaysConnectedBeyondTheRequestReadDeadline()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        http.DefaultRequestHeaders.Add(BridgeServer.TOKEN_HEADER, bridge.Token);
        using var response = await http.GetAsync($"http://127.0.0.1:{bridge.Port}/events", HttpCompletionOption.ResponseHeadersRead);
        using Stream stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        Assert.Equal("event: hello", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        await reader.ReadLineAsync();
        await reader.ReadLineAsync();
        await Task.Delay(TimeSpan.FromSeconds(6));
        fixture.Bus.Emit("finished", PyDict.Of("task_id", "late", "success", true));
        Assert.Equal("event: finished", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        bridge.Stop();
        fixture.Bus.Emit("test_shutdown", new PyDict());
        Assert.True(SpinWait.SpinUntil(() => fixture.Bus.SubscriberCount == 0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task SseResetIsDeliveredBeforeClosingTheStreamForRecovery()
    {
        using var fixture = new BackendFixture();
        using var bridge = new BridgeServer(fixture.Api, fixture.Bus);
        bridge.Start();
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        http.DefaultRequestHeaders.Add(BridgeServer.TOKEN_HEADER, bridge.Token);
        using var response = await http.GetAsync($"http://127.0.0.1:{bridge.Port}/events", HttpCompletionOption.ResponseHeadersRead);
        using Stream stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        Assert.Equal("event: hello", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        await reader.ReadLineAsync();
        await reader.ReadLineAsync();
        fixture.Bus.Emit("events_reset", PyDict.Of("reason", "queue_overflow"));
        Assert.Equal("event: events_reset", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("queue_overflow", await reader.ReadLineAsync());
        Assert.Equal("", await reader.ReadLineAsync());
        Assert.Null(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(SpinWait.SpinUntil(() => fixture.Bus.SubscriberCount == 0, TimeSpan.FromSeconds(2)));
    }
}
