using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Minecraft;
using Xunit;

namespace HelL.Tests;

public sealed class MinecraftConnectionTests
{
    [Theory]
    [InlineData("[MOTD]我的世界[/MOTD][AD]51234[/AD]", 51234)]
    [InlineData("[MOTD]world[/MOTD][AD]0[/AD]", 0)]
    [InlineData("[MOTD]world[/MOTD][AD]65536[/AD]", 0)]
    [InlineData("[MOTD]world[/MOTD][AD]25565[/AD]trailing", 0)]
    [InlineData("[MOTD]world[/MOTD][AD]host:25565[/AD]", 0)]
    public void LanAnnouncementUsesSenderAddressAndValidPort(string packet, int port)
    {
        var entry = MinecraftLanDiscovery.Parse(Encoding.UTF8.GetBytes(packet), new IPEndPoint(IPAddress.Parse("192.168.1.25"), 4445));
        if (port == 0) { Assert.Null(entry); return; }
        Assert.Equal("192.168.1.25", entry.Get("host"));
        Assert.Equal(port, Py.Int(entry.Get("port")));
    }

    [Fact]
    public void LanAnnouncementRejectsInvalidUtf8AndExcessiveLength()
    {
        var sender = new IPEndPoint(IPAddress.Loopback, 4445);
        Assert.Null(MinecraftLanDiscovery.Parse(new byte[] { 0xff, 0xfe }, sender));
        Assert.Null(MinecraftLanDiscovery.Parse(new byte[2049], sender));
        Assert.Null(MinecraftLanDiscovery.Parse(Encoding.UTF8.GetBytes("[MOTD]" + new string('x', 513) + "[/MOTD][AD]25565[/AD]"), sender));
    }

    [Theory]
    [InlineData(767, "", "ready", "none")]
    [InlineData(767, "1.20.4", "version_mismatch", "auto_version")]
    [InlineData(99999, "", "unsupported_protocol", "choose_server")]
    public async Task ServerStatusSeparatesReachabilityFromCompatibility(int protocol, string selected, string code, string recovery)
    {
        using var server = new StatusServer(Packet(StatusJson(protocol)));
        var status = await MinecraftServerProbe.CheckAsync("127.0.0.1", server.Port, selected);
        Assert.True(Py.Truthy(status.Get("online")));
        Assert.Equal(code, status.Get("code"));
        Assert.Equal(recovery, status.Get("recovery"));
        Assert.Equal(code == "ready", Py.Truthy(status.Get("can_connect")));
        Assert.Equal("测试世界", status.Get("motd"));
    }

    [Theory]
    [InlineData("broken-json")]
    [InlineData("[]")]
    [InlineData("{\"version\":{\"name\":\"server\",\"protocol\":\"bad\"}}")]
    public async Task MalformedStatusReturnsAnActionableError(string json)
    {
        using var server = new StatusServer(Packet(json));
        var status = await MinecraftServerProbe.CheckAsync("127.0.0.1", server.Port);
        Assert.Equal("invalid_status", status.Get("code"));
        Assert.False(Py.Truthy(status.Get("can_connect")));
    }

    [Fact]
    public async Task RejectsOversizedPacketsAndTrailingPayload()
    {
        using var oversized = new StatusServer(new byte[] { 0x81, 0x80, 0x04 });
        Assert.Equal("invalid_status", (await MinecraftServerProbe.CheckAsync("127.0.0.1", oversized.Port)).Get("code"));
        using var trailing = new StatusServer(Packet(StatusJson(767), trailing: true));
        Assert.Equal("invalid_status", (await MinecraftServerProbe.CheckAsync("127.0.0.1", trailing.Port)).Get("code"));
    }

    [Fact]
    public async Task ServerJsonNestingIsBounded()
    {
        using var server = new StatusServer(Packet(new string('[', 80) + "0" + new string(']', 80)));
        Assert.Equal("invalid_status", (await MinecraftServerProbe.CheckAsync("127.0.0.1", server.Port)).Get("code"));
    }

    [Fact]
    public async Task TimeoutIsDistinctFromExplicitCancellation()
    {
        using var silent = new StatusServer(null);
        var status = await MinecraftServerProbe.CheckAsync("127.0.0.1", silent.Port, timeoutMs: 120);
        Assert.Equal("timeout", status.Get("code"));
        using var other = new StatusServer(null);
        using var cancel = new CancellationTokenSource();
        var probe = MinecraftServerProbe.CheckAsync("127.0.0.1", other.Port, token: cancel.Token);
        await other.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
    }

    [Fact]
    public async Task StoppingPreflightPreventsLateConnectionAndReleasesSettings()
    {
        using var home = new TestHome();
        using var server = new StatusServer(null);
        MinecraftSession.Stop();
        var options = MinecraftOptions.Validate("127.0.0.1", server.Port, "HelLAI", "offline", "");
        var connecting = Task.Run(() => MinecraftSession.Start(options));
        try
        {
            await server.Accepted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("checking", MinecraftSession.Status(false).Get("phase"));
            Assert.Throws<InvalidOperationException>(() => MinecraftSession.SaveOptions(options));
            MinecraftSession.Stop();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
            Assert.False(MinecraftSession.Running);
            Assert.Equal("disconnected", MinecraftSession.Status(false).Get("phase"));
            Assert.Empty(MinecraftSession.Schemas());
            MinecraftSession.SaveOptions(options);
            Assert.Throws<McpError>(() => MinecraftSession.FeiyuCall("mcp_minecraft_observe", new PyDict()));
        }
        finally { MinecraftSession.Stop(); }
    }

    [Fact]
    public void ExplicitInvalidNodeDoesNotSilentlyChooseAnotherInstallation()
    {
        Assert.Equal("", MinecraftRuntime.FindNode("Z:/nonexistent-minecraft-node/node.exe"));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.2.60")]
    [InlineData("255.255.255.255")]
    public void AnnouncementSourceMustBeAUnicastAddress(string sender)
    {
        Assert.Null(MinecraftLanDiscovery.Parse(Encoding.UTF8.GetBytes("[MOTD]world[/MOTD][AD]25565[/AD]"), new IPEndPoint(IPAddress.Parse(sender), 4445)));
    }

    [Fact]
    public async Task DiscoveryRetainsBlockedAnnouncementsAndDeduplicatesHealthyServers()
    {
        using var healthy = new StatusServer(Packet(StatusJson(767)));
        using var blocked = new StatusServer(null);
        var one = PyDict.Of("host", "127.0.0.1", "port", healthy.Port, "source", "lan", "name", "one");
        var two = PyDict.Of("host", "127.0.0.1", "port", blocked.Port, "source", "lan", "name", "two");
        var found = await MinecraftLanDiscovery.VerifyCandidatesAsync(new[] { one, two, one.Copy() });
        Assert.Single(Py.ListOr(found.Get("servers")));
        var unavailable = Assert.Single(Py.ListOr(found.Get("unreachable"))) as PyDict;
        Assert.Equal("timeout", unavailable.Get("code")); Assert.Equal(blocked.Port, Py.Int(unavailable.Get("port")));
    }

    internal static string StatusJson(int protocol) => "{\"version\":{\"name\":\"1.21.1\",\"protocol\":" + protocol + "},\"players\":{\"online\":1,\"max\":8},\"description\":{\"text\":\"测试世界\"}}";
    internal static byte[] Packet(string json, bool trailing = false)
    {
        using var body = new MemoryStream();
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        body.WriteByte(0); VarInt(body, bytes.Length); body.Write(bytes);
        if (trailing) body.WriteByte(42);
        using var packet = new MemoryStream(); VarInt(packet, (int)body.Length); body.Position = 0; body.CopyTo(packet);
        return packet.ToArray();
    }
    private static void VarInt(Stream stream, int value)
    {
        do { byte part = (byte)(value & 127); value >>= 7; stream.WriteByte((byte)(part | (value > 0 ? 128 : 0))); } while (value > 0);
    }

    private sealed class StatusServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly Task _serving;
        public TaskCompletionSource<bool> Accepted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Port { get; }
        public StatusServer(byte[] packet)
        {
            _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serving = Serve(packet);
        }
        private async Task Serve(byte[] packet)
        {
            try
            {
                using var peer = await _listener.AcceptTcpClientAsync(_cancel.Token);
                Accepted.TrySetResult(true);
                if (packet != null) await peer.GetStream().WriteAsync(packet, _cancel.Token);
                await Task.Delay(Timeout.Infinite, _cancel.Token);
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        }
        public void Dispose() { _cancel.Cancel(); _listener.Stop(); _serving.GetAwaiter().GetResult(); _cancel.Dispose(); }
    }
}
