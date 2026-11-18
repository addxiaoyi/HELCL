using System.Net.WebSockets;
using HelL.Contracts;
using HelL.Relay;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class RelayRoomsTests
{
    [Fact]
    public async Task InvalidRolesAndExpiredTicketsCannotOccupyARoom()
    {
        var rooms = new RelayRooms();
        using var socket = new StubSocket();
        long expires = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        await Assert.ThrowsAsync<IOException>(() => rooms.ForwardAsync(new("room", "observer", expires), socket, CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => rooms.ForwardAsync(new("room", "host", 0), socket, CancellationToken.None));
        Assert.False(socket.Receiving.Task.IsCompleted);
    }

    [Fact]
    public async Task DuplicateRoleCannotReplaceTheConnectedHost()
    {
        var rooms = new RelayRooms();
        using var first = new StubSocket();
        using var second = new StubSocket();
        using var ended = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var ticket = new RelayTicket("room", "host", DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());
        Task waiting = rooms.ForwardAsync(ticket, first, ended.Token);
        await Assert.ThrowsAsync<IOException>(() => rooms.ForwardAsync(ticket, second, ended.Token));
        ended.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task CancellationAfterACompletedForwardMustNotReturnNormally()
    {
        var rooms = new RelayRooms();
        using var ended = new CancellationTokenSource();
        using var host = new StubSocket();
        using var client = new StubSocket { OnSend = ended.Cancel };
        host.Incoming.Enqueue(new(1, WebSocketMessageType.Binary, true));
        long expires = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        Task receivingHost = rooms.ForwardAsync(new("room", "host", expires), host, ended.Token);
        Task receivingClient = rooms.ForwardAsync(new("room", "client", expires), client, ended.Token);

        // Cancellation lands inside the completed send, before the next loop condition.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receivingHost.WaitAsync(TimeSpan.FromSeconds(3)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receivingClient.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, client.SentBytes);
        Assert.Equal(WebSocketState.Aborted, host.State);
        Assert.Equal(WebSocketState.Aborted, client.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidTrafficClosesThePairBeforeForwardingTheOffendingFragment(bool oversized)
    {
        var rooms = new RelayRooms();
        using var host = new StubSocket();
        using var client = new StubSocket();
        if (oversized)
            for (int fragment = 0; fragment <= HostProtocol.MaxMessageBytes / 65536; fragment++)
                host.Incoming.Enqueue(new(65536, WebSocketMessageType.Binary, false));
        else host.Incoming.Enqueue(new(1, WebSocketMessageType.Text, true));
        using var ended = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        long expires = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        Task receivingHost = rooms.ForwardAsync(new("room", "host", expires), host, ended.Token);
        Task receivingClient = rooms.ForwardAsync(new("room", "client", expires), client, ended.Token);
        await Assert.ThrowsAsync<IOException>(() => receivingHost);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receivingClient);
        Assert.Equal(oversized ? HostProtocol.MaxMessageBytes : 0, client.SentBytes);
        Assert.Equal(WebSocketState.Aborted, host.State);
        Assert.Equal(WebSocketState.Aborted, client.State);
    }
}
