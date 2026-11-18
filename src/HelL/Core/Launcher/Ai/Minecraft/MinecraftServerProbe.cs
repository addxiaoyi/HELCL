using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftServerProbe
{
    private const int MaxPacketBytes = 65536;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static async Task<PyDict> CheckAsync(string host, int port, string selectedVersion = "", CancellationToken token = default, int timeoutMs = 3500)
    {
        var target = MinecraftOptions.Validate(host, port, "HelLAI", "offline", selectedVersion);
        host = Py.S(target.Get("host")); selectedVersion = Py.S(target.Get("version"));
        if (timeoutMs < 1 || timeoutMs > 30000) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        using var client = new TcpClient(Uri.CheckHostName(host) == UriHostNameType.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
        using var abort = deadline.Token.Register(client.Close);
        try
        {
            var connecting = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connecting, Task.Delay(timeoutMs, deadline.Token)).ConfigureAwait(false) != connecting)
            {
                _ = connecting.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                deadline.Token.ThrowIfCancellationRequested(); throw new TimeoutException();
            }
            await connecting.ConfigureAwait(false);
            using var stream = client.GetStream();
            using var handshake = new MemoryStream();
            WriteVarInt(handshake, 0); WriteVarInt(handshake, -1);
            byte[] address = Utf8.GetBytes(host); WriteVarInt(handshake, address.Length); handshake.Write(address, 0, address.Length);
            handshake.WriteByte((byte)(port >> 8)); handshake.WriteByte((byte)port); WriteVarInt(handshake, 1);
            using var packet = new MemoryStream(); WriteVarInt(packet, (int)handshake.Length); handshake.WriteTo(packet);
            packet.WriteByte(1); packet.WriteByte(0);
            byte[] request = packet.ToArray(); await stream.WriteAsync(request, 0, request.Length, deadline.Token).ConfigureAwait(false);
            int length = await ReadVarIntAsync(stream, deadline.Token).ConfigureAwait(false);
            if (length < 2 || length > MaxPacketBytes) throw new InvalidDataException("服务器状态包大小无效。");
            byte[] body = new byte[length]; await ReadExactAsync(stream, body, deadline.Token).ConfigureAwait(false);
            using var message = new MemoryStream(body);
            if (await ReadVarIntAsync(message, deadline.Token).ConfigureAwait(false) != 0) throw new InvalidDataException("服务器没有返回 Java 版状态。");
            int textLength = await ReadVarIntAsync(message, deadline.Token).ConfigureAwait(false);
            if (textLength < 1 || textLength != message.Length - message.Position) throw new InvalidDataException("服务器状态内容不完整。");
            byte[] json = new byte[textLength]; await ReadExactAsync(message, json, deadline.Token).ConfigureAwait(false);
            string text = Utf8.GetString(json);
            // Bound nesting before passing untrusted server JSON to the shared decoder.
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            var status = PyJson.Loads(text) as PyDict ?? throw new InvalidDataException("服务器状态不是对象。");
            var version = status.Get("version") as PyDict ?? throw new InvalidDataException("服务器未返回版本信息。");
            if (!int.TryParse(Py.S(version.Get("protocol")), out int protocol)) throw new InvalidDataException("服务器协议版本无效。");
            string compatible = MinecraftVersions.ForProtocol(protocol);
            string code = compatible.Length == 0 ? "unsupported_protocol" : selectedVersion.Length > 0 && MinecraftVersions.ProtocolFor(selectedVersion) != protocol ? "version_mismatch" : "ready";
            var players = status.Get("players") as PyDict;
            return PyDict.Of("online", true, "can_connect", code == "ready", "code", code, "host", host, "port", port,
                "version", News.PlainText(Py.S(version.Get("name")), 80), "protocol", protocol, "compatible_version", compatible, "supported_range", MinecraftVersions.Range,
                "motd", Description(status.Get("description")), "players", players?.Get("online", 0L) ?? 0L, "max_players", players?.Get("max", 0L) ?? 0L,
                "latency_ms", watch.ElapsedMilliseconds, "checked_at", DateTime.UtcNow.ToString("O"),
                "message", code == "ready" ? "服务器在线，版本兼容。" : code == "version_mismatch" ? "手动指定的游戏版本与服务器不匹配，可恢复自动识别。" : "服务器在线，但当前组件不支持该协议；修复同一版本组件不会改变支持范围。",
                "recovery", code == "version_mismatch" ? "auto_version" : code == "unsupported_protocol" ? "choose_server" : "none");
        }
        catch (Exception ex) when (ex is SocketException || ex is IOException || ex is InvalidDataException || ex is JsonException || ex is OperationCanceledException || ex is ObjectDisposedException || ex is TimeoutException || ex is PyException || ex is DecoderFallbackException)
        {
            token.ThrowIfCancellationRequested();
            bool timeout = deadline.IsCancellationRequested || ex is TimeoutException;
            string code = timeout ? "timeout" : ex is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionRefused ? "refused" : ex is InvalidDataException || ex is JsonException || ex is PyException || ex is DecoderFallbackException ? "invalid_status" : "unreachable";
            return PyDict.Of("online", false, "can_connect", false, "host", host, "port", port, "code", code, "checked_at", DateTime.UtcNow.ToString("O"),
                "message", code == "refused" ? "服务器拒绝连接。请先启动服务器，或在游戏中“对局域网开放”，再填写实际显示的端口。" : code == "timeout" ? "服务器检测超时，请检查地址、端口和防火墙。" : code == "invalid_status" ? "这个端口未返回有效的 Minecraft Java 版状态。" : "无法连接此服务器，请检查网络、地址和端口。",
                "recovery", "discover");
        }
    }

    private static string Description(object value, int depth = 0)
    {
        if (depth > 4) return "";
        if (value is string text) return News.PlainText(text, 160);
        if (value is PyDict component)
        {
            string title = Py.S(component.Get("text"));
            foreach (var child in Py.ListOr(component.Get("extra"))) { title += Description(child, depth + 1); if (title.Length > 160) break; }
            return News.PlainText(title, 160);
        }
        return "";
    }
    private static void WriteVarInt(Stream stream, int value)
    {
        uint remaining = unchecked((uint)value);
        do { byte chunk = (byte)(remaining & 0x7f); remaining >>= 7; stream.WriteByte((byte)(chunk | (remaining != 0 ? 0x80 : 0))); } while (remaining != 0);
    }
    private static async Task<int> ReadVarIntAsync(Stream stream, CancellationToken token)
    {
        uint value = 0; var one = new byte[1];
        for (int index = 0; index < 5; index++)
        {
            await ReadExactAsync(stream, one, token).ConfigureAwait(false);
            if (index == 4 && (one[0] & 0xf0) != 0) throw new InvalidDataException("服务器返回了过长整数。");
            value |= (uint)(one[0] & 0x7f) << (7 * index);
            if ((one[0] & 0x80) == 0) return unchecked((int)value);
        }
        throw new InvalidDataException("服务器返回了过长整数。");
    }
    private static async Task ReadExactAsync(Stream stream, byte[] bytes, CancellationToken token)
    {
        int offset = 0;
        while (offset < bytes.Length) { int count = await stream.ReadAsync(bytes, offset, bytes.Length - offset, token).ConfigureAwait(false); if (count == 0) throw new EndOfStreamException(); offset += count; }
    }
}
