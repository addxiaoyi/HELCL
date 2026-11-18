using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using HelL.Relay;
using HelL.Remote;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class TunnelTests
{
    [Fact]
    public async Task RelayRequiresAuthorizationAndTransportsMutuallyAuthenticatedTls()
    {
        string operatorToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using WebApplication relay = RelayApp.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Relay:OperatorToken"] = operatorToken,
                ["Relay:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["urls"] = "http://127.0.0.1:0"
            });
            builder.Logging.ClearProviders();
        });
        await relay.StartAsync();
        string address = relay.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        using HttpResponseMessage denied = await http.PostAsync("/v1/rooms", null);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", operatorToken);
        using HttpResponseMessage issued = await http.PostAsync("/v1/rooms", null);
        issued.EnsureSuccessStatusCode();
        RelayRoom room = (await issued.Content.ReadFromJsonAsync<RelayRoom>())!;
        Uri tunnel = new(address.Replace("http:", "ws:") + "/v1/tunnel");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var host = new ClientWebSocket();
        using var client = new ClientWebSocket();
        host.Options.SetRequestHeader("Authorization", "Bearer " + room.HostTicket);
        client.Options.SetRequestHeader("Authorization", "Bearer " + room.ClientTicket);
        await host.ConnectAsync(tunnel, timeout.Token);
        await client.ConnectAsync(tunnel, timeout.Token);
        using var hostIdentity = DeviceTunnel.CreateIdentity("host");
        using var clientIdentity = DeviceTunnel.CreateIdentity("client");
        Task<SslStream> accepting = DeviceTunnel.AcceptAsync(new WebSocketStream(host, true), hostIdentity,
            DeviceTunnel.Fingerprint(clientIdentity), timeout.Token);
        Task<SslStream> connecting = DeviceTunnel.ConnectAsync(new WebSocketStream(client, true), clientIdentity,
            DeviceTunnel.Fingerprint(hostIdentity), timeout.Token);
        await Task.WhenAll(accepting, connecting);
        await using SslStream clientTls = await connecting;
        await using SslStream hostTls = await accepting;
        byte[] command = Encoding.UTF8.GetBytes("private launcher state");
        await clientTls.WriteAsync(command, timeout.Token);
        byte[] received = new byte[command.Length];
        await hostTls.ReadExactlyAsync(received, timeout.Token);
        Assert.Equal(command, received);
        await hostTls.WriteAsync("ack"u8.ToArray(), timeout.Token);
        byte[] ack = new byte[3];
        await clientTls.ReadExactlyAsync(ack, timeout.Token);
        Assert.Equal("ack", Encoding.UTF8.GetString(ack));
        client.Abort();
        host.Abort();
        await relay.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task DeviceTunnelRejectsADifferentPairedDevice()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using TcpClient host = await accepting;
        using var hostIdentity = DeviceTunnel.CreateIdentity("host");
        using var clientIdentity = DeviceTunnel.CreateIdentity("client");
        using var stranger = DeviceTunnel.CreateIdentity("stranger");
        Task<SslStream> hostTls = DeviceTunnel.AcceptAsync(host.GetStream(), hostIdentity,
            DeviceTunnel.Fingerprint(clientIdentity), timeout.Token);
        Exception? rejection = await Record.ExceptionAsync(async () =>
            await DeviceTunnel.ConnectAsync(client.GetStream(), clientIdentity, DeviceTunnel.Fingerprint(stranger), timeout.Token));
        Assert.True(rejection is AuthenticationException or IOException, rejection?.ToString());
        try
        {
            await using SslStream accepted = await hostTls;
            byte[] received = new byte[1];
            // TLS 1.3 can finish the server flight before the client's pin rejection arrives.
            int count = await accepted.ReadAsync(received, timeout.Token);
            Assert.Equal(0, count);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException) { }
    }

    [Theory]
    [InlineData(-2, -1)]
    [InlineData(1, 2)]
    public async Task DeviceTunnelRejectsCertificatesOutsideTheirValidityEvenWhenTheFingerprintMatches(int startDays, int endDays)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        Task<TcpClient> accepting = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using TcpClient host = await accepting;
        using X509Certificate2 expired = DatedIdentity(startDays, endDays);
        using X509Certificate2 identity = DeviceTunnel.CreateIdentity("client");
        Task<SslStream> hostTls = DeviceTunnel.AcceptAsync(host.GetStream(), expired,
            DeviceTunnel.Fingerprint(identity), timeout.Token);
        Exception? rejection = await Record.ExceptionAsync(async () =>
            await DeviceTunnel.ConnectAsync(client.GetStream(), identity, DeviceTunnel.Fingerprint(expired), timeout.Token));
        Assert.True(rejection is AuthenticationException or IOException, rejection?.ToString());
        try
        {
            await using SslStream accepted = await hostTls;
            Assert.Equal(0, await accepted.ReadAsync(new byte[1], timeout.Token));
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException) { }
    }

    private static X509Certificate2 DatedIdentity(int startDays, int endDays)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=expired paired device", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(startDays), DateTimeOffset.UtcNow.AddDays(endDays));
        byte[] pkcs12 = generated.Export(X509ContentType.Pkcs12);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, null,
                X509KeyStorageFlags.Exportable | (OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet
                    : OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet));
        }
        finally { CryptographicOperations.ZeroMemory(pkcs12); }
    }
}
