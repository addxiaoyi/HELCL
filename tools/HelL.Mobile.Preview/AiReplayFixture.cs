using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HelL.Core.Platform;

internal sealed class AiReplayFixture : IDisposable
{
    private readonly string? previous = Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY");
    private readonly ISecretStore? previousSecrets = PlatformServices.SecretStore;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopped = new();
    private readonly Task server;

    internal AiReplayFixture()
    {
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string address = $"http://127.0.0.1:{port}/";
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", address);
        PlatformServices.SecretStore = new FixtureSecrets();
        server = Serve();
    }

    private async Task Serve()
    {
        using var client = await listener.AcceptTcpClientAsync(stopped.Token);
        await using var socket = client.GetStream();
        using var header = new MemoryStream();
        byte[] one = new byte[1];
        while (header.Length < 16384)
        {
            if (await socket.ReadAsync(one, stopped.Token) == 0) throw new IOException("Incomplete replay request.");
            header.WriteByte(one[0]);
            if (header.Length >= 4 && Encoding.ASCII.GetString(header.GetBuffer(), (int)header.Length - 4, 4) == "\r\n\r\n") break;
        }
        var headers = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n").Skip(1).Where(line => line.Contains(':'))
            .Select(line => line.Split(':', 2)).ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
        int length = int.Parse(headers["Content-Length"], System.Globalization.CultureInfo.InvariantCulture);
        if (length is < 1 or > 200000) throw new InvalidDataException("Invalid replay request size.");
        byte[] body = new byte[length];
        await socket.ReadExactlyAsync(body, stopped.Token);
        using var request = JsonDocument.Parse(body);
        if (headers["X-HelL-Replay-Url"] != "https://fixture.invalid/v1/chat/completions"
            || request.RootElement.TryGetProperty("tools", out _) || request.RootElement.GetProperty("model").GetString() != "fixture-model")
            throw new InvalidDataException("The preview AI request used an unexpected endpoint, model or tools.");
        const string stream = "data: {\"choices\":[{\"delta\":{\"content\":\"请先核对 Minecraft 版本和加载器。\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"这是本机隔离 HTTP 回放，没有向外部模型发送消息。\"},\"finish_reason\":\"stop\"}]}\n\ndata: {\"choices\":[],\"usage\":{\"total_tokens\":20}}\n\ndata: [DONE]\n\n";
        byte[] bytes = Encoding.UTF8.GetBytes(stream);
        byte[] responseHeader = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await socket.WriteAsync(responseHeader, stopped.Token);
        await socket.WriteAsync(bytes, stopped.Token);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TIPCL_HTTP_REPLAY", previous);
        PlatformServices.SecretStore = previousSecrets!;
        stopped.Cancel();
        listener.Stop();
        if (!server.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The preview AI replay did not finish.");
        stopped.Dispose();
    }

    private sealed class FixtureSecrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];
        public string GetPassword(string service, string username) => values.GetValueOrDefault(username, "");
        public void SetPassword(string service, string username, string password) => values[username] = password;
        public void DeletePassword(string service, string username) => values.Remove(username);
    }
}
