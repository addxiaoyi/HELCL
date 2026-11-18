using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class FeiyuLink
{
    private static readonly object Gate = new object();
    private static TcpListener? _listener;
    private static string _token = "";
    public static string DiscoveryPath => Environment.GetEnvironmentVariable("HELL_FEIYU_LINK") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HelL", "minecraft-link.json");

    public static void Open()
    {
        lock (Gate)
        {
            if (_listener != null) return;
            byte[] secret = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
            string token = BitConverter.ToString(secret).Replace("-", "").ToLowerInvariant();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start(8); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Utils.WriteJson(DiscoveryPath, PyDict.Of("version", 1L, "endpoint", "http://127.0.0.1:" + port, "token", token));
                _listener = listener; _token = token;
                new Thread(() => Listen(listener, token, port)) { IsBackground = true, Name = "feiyu-game-link" }.Start();
            }
            catch { listener.Stop(); throw; }
        }
    }
    private static void Listen(TcpListener listener, string token, int port)
    {
        while (true)
        {
            try { var client = listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(_ => Serve(client, token, port)); }
            catch (Exception ex) when (ex is SocketException || ex is ObjectDisposedException) { return; }
        }
    }
    private static void Serve(TcpClient client, string token, int port)
    {
        using (client)
        {
            client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
            using var stream = client.GetStream();
            try
            {
                string[] start = ReadLine(stream).Split(' ');
                if (start.Length != 3) throw new ArgumentException("请求格式无效。");
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int headerBytes = 0;
                while (true)
                {
                    string line = ReadLine(stream); if (line.Length == 0) break;
                    headerBytes += line.Length; int colon = line.IndexOf(':');
                    if (colon <= 0 || headerBytes > 8192) throw new ArgumentException("请求头无效。");
                    string key = line.Substring(0, colon).Trim();
                    if (headers.ContainsKey(key)) throw new ArgumentException("重复的请求头。");
                    headers.Add(key, line.Substring(colon + 1).Trim());
                }
                bool authorized;
                lock (Gate) authorized = _token == token && headers.TryGetValue("Authorization", out var auth) && auth == "Bearer " + token;
                if (!authorized || headers.ContainsKey("Origin") || !headers.TryGetValue("Host", out var host) || host != "127.0.0.1:" + port)
                { Reply(stream, 403, PyDict.Of("ok", false, "error", "联动授权无效，请在启动器重新允许大肥鱼接管。")); return; }
                if (start[0] == "GET" && start[1] == "/status")
                { Reply(stream, 200, PyDict.Of("ok", true, "data", MinecraftSession.SharedStatus())); return; }
                if (start[0] != "POST" || headers.ContainsKey("Transfer-Encoding") || !headers.TryGetValue("Content-Length", out var rawLength) ||
                    !int.TryParse(rawLength, out int length) || length < 0 || length > 4096) throw new ArgumentException("请求格式无效。");
                if (start[1] == "/stop")
                {
                    Reply(stream, 200, PyDict.Of("ok", true, "data", PyDict.Of("stopped", true)));
                    MinecraftSession.Stop(); return;
                }
                if (start[1] != "/action") { Reply(stream, 404, PyDict.Of("ok", false, "error", "没有此联动接口。")); return; }
                byte[] body = new byte[length];
                for (int offset = 0; offset < length;)
                { int read = stream.Read(body, offset, length - offset); if (read == 0) throw new IOException("请求被中断。"); offset += read; }
                var payload = PyJson.Loads(new UTF8Encoding(false, true).GetString(body)) as PyDict ?? throw new ArgumentException("动作必须是 JSON 对象。");
                string name = Py.S(payload.Get("name"));
                if (name is "stop" or "disconnect") throw new ArgumentException("停止请使用专用停止接口。");
                string response = MinecraftSession.FeiyuCall(MinecraftSession.Prefix + name, payload.Get("arguments") as PyDict ?? new PyDict());
                Reply(stream, 200, PyJson.Loads(response));
            }
            catch (Exception ex)
            {
                try { Reply(stream, 400, PyDict.Of("ok", false, "error", ex.Message)); }
                catch (Exception sendError) when (sendError is IOException || sendError is SocketException || sendError is ObjectDisposedException)
                { System.Diagnostics.Trace.TraceWarning("Feiyu link response: " + sendError.Message); }
            }
        }
    }
    private static string ReadLine(Stream stream)
    {
        var line = new StringBuilder();
        while (line.Length <= 4096)
        {
            int value = stream.ReadByte(); if (value < 0) throw new IOException("请求被中断。");
            if (value == '\n') return line.ToString();
            if (value != '\r') line.Append((char)value);
        }
        throw new ArgumentException("请求行过长。");
    }
    private static void Reply(Stream stream, int code, object value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(PyJson.Dumps(value, ensureAscii: false));
        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + code + (code == 200 ? " OK" : " Error") + "\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n\r\n");
        stream.Write(header, 0, header.Length); stream.Write(bytes, 0, bytes.Length); stream.Flush();
    }
    public static void Close()
    {
        lock (Gate)
        {
            string token = _token; _token = ""; _listener?.Stop(); _listener = null;
            try
            {
                if (File.Exists(DiscoveryPath) && Utils.ReadJson(DiscoveryPath) is PyDict stored && Py.S(stored.Get("token")) == token) File.Delete(DiscoveryPath);
            }
            catch (IOException ex) { System.Diagnostics.Trace.TraceWarning("Feiyu link discovery cleanup: " + ex.Message); }
        }
    }
}
