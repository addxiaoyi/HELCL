using System.Net.WebSockets;
using System.Security.Cryptography;
using HelL.Relay;
using HelL.Remote;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class RelayBudgetTests
{
    [Fact]
    public void CredentialsHaveSeparateConnectionBudgetsAndOneConcurrentOccupant()
    {
        var limits = new RelayLimits { ConnectionsPerWindow = 2 };
        var rooms = new RelayRooms(limits);
        RelayRoom issued = Issue(rooms);
        using (RelayRooms.Reservation first = rooms.Reserve(Host(issued)))
        {
            RelayAdmissionException duplicate = Assert.Throws<RelayAdmissionException>(() => rooms.Reserve(Host(issued)));
            Assert.Equal(429, duplicate.StatusCode);
        }
        RelayAdmissionException throttled = Assert.Throws<RelayAdmissionException>(() => rooms.Reserve(Host(issued)));
        Assert.Equal(429, throttled.StatusCode);
        using RelayRooms.Reservation client = rooms.Reserve(new(issued.Room, "client", issued.Expires));
    }

    [Fact]
    public void ActiveCapacityAndRevokedTombstoneCapacityAreBothBounded()
    {
        var rooms = new RelayRooms(new RelayLimits { MaxRooms = 1, MaxTrackedRooms = 2 });
        RelayRoom first = Issue(rooms);
        RelayRoom second = Issue(rooms);
        using (RelayRooms.Reservation occupied = rooms.Reserve(Host(first)))
        {
            Assert.Equal(503, Assert.Throws<RelayAdmissionException>(() => rooms.Reserve(Host(second))).StatusCode);
        }
        Assert.True(rooms.Revoke(first.Room));
        Assert.Equal(503, Assert.Throws<RelayAdmissionException>(() => Issue(rooms)).StatusCode);
        Assert.Equal(401, Assert.Throws<RelayAdmissionException>(() => rooms.Reserve(Host(first))).StatusCode);
        using RelayRooms.Reservation available = rooms.Reserve(Host(second));
    }

    [Fact]
    public void RevocationAndIssuedRoomStateSurviveARelayRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "hell-relay-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            var limits = new RelayLimits { StatePath = Path.Combine(directory, "rooms.json") };
            var rooms = new RelayRooms(limits);
            RelayRoom revoked = Issue(rooms);
            RelayRoom active = Issue(rooms);
            Assert.True(rooms.Revoke(revoked.Room));
            var restarted = new RelayRooms(limits);
            Assert.False(restarted.IsAllowed(Host(revoked)));
            Assert.True(restarted.IsAllowed(Host(active)));
            Assert.Equal(401, Assert.Throws<RelayAdmissionException>(() => restarted.Reserve(Host(revoked))).StatusCode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AnIdlePairIsClosedWithinTheConfiguredDeadline()
    {
        var rooms = new RelayRooms(new RelayLimits { IdleTimeout = TimeSpan.FromMilliseconds(100) });
        using var host = new StubSocket();
        using var client = new StubSocket();
        RelayRoom issued = Issue(rooms);
        Task hostIo = rooms.ForwardAsync(Host(issued), host, CancellationToken.None);
        Task clientIo = rooms.ForwardAsync(new(issued.Room, "client", issued.Expires), client, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hostIo.WaitAsync(TimeSpan.FromSeconds(3)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clientIo.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(WebSocketState.Aborted, host.State);
        Assert.Equal(WebSocketState.Aborted, client.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ByteAndZeroLengthFragmentBudgetsCloseThePairBeforeForwardingExcess(bool emptyFragments)
    {
        var limits = new RelayLimits { BytesPerWindow = 8, FragmentsPerWindow = 2 };
        var rooms = new RelayRooms(limits);
        using var host = new StubSocket();
        using var client = new StubSocket();
        if (emptyFragments)
            for (int frame = 0; frame < 3; frame++) host.Incoming.Enqueue(new(0, WebSocketMessageType.Binary, true));
        else host.Incoming.Enqueue(new(9, WebSocketMessageType.Binary, true));
        RelayRoom issued = Issue(rooms);
        Task hostIo = rooms.ForwardAsync(Host(issued), host, CancellationToken.None);
        Task clientIo = rooms.ForwardAsync(new(issued.Room, "client", issued.Expires), client, CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => hostIo);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => clientIo);
        Assert.Equal(emptyFragments ? 2 : 0, client.SendCalls);
        Assert.Equal(0, client.SentBytes);
    }

    private static RelayRoom Issue(RelayRooms rooms)
    {
        var signer = new RelayTickets(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        RelayRoom issued = signer.Issue(DateTimeOffset.UtcNow);
        rooms.Register(issued);
        return issued;
    }

    private static RelayTicket Host(RelayRoom issued) => new(issued.Room, "host", issued.Expires);
}
