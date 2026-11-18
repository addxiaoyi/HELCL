using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HelL.Contracts;
using HelL.Remote;
using Xunit;

namespace HelL.Remote.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void RoomTicketsAreRoleBoundTamperResistantAndExpire()
    {
        var signer = new RelayTickets(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RelayRoom room = signer.Issue(now);
        Assert.Equal("host", signer.Validate(room.HostTicket, now)?.Role);
        Assert.Equal("client", signer.Validate(room.ClientTicket, now)?.Role);
        Assert.Equal(room.Room, signer.Validate(room.HostTicket, now)?.Room);
        Assert.Null(signer.Validate(room.HostTicket[..^4] + "xxxx", now));
        Assert.Null(signer.Validate(room.HostTicket, now.AddDays(1)));
        var other = new RelayTickets(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Assert.Null(other.Validate(room.HostTicket, now));
    }

    [Fact]
    public void HostProtocolRejectsUnknownCommandsAndMalformedRequestIds()
    {
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = (HostCommand)900 };
        Assert.Throws<ArgumentException>(request.Validate);
        request.Command = HostCommand.CancelTask;
        Assert.Throws<ArgumentException>(request.Validate);
        request.TaskId = "download-1";
        request.Validate();
        request.Protocol++;
        Assert.Throws<ArgumentException>(request.Validate);
    }

    [Fact]
    public void AvailableFeatureIdentifiesTheExecutingDevice()
    {
        Assert.Throws<ArgumentException>(() => new FeatureAccess("ai.local", ExecutionSite.PairedHost, ""));
        Assert.Throws<ArgumentException>(() => new FeatureAccess("game.launch", ExecutionSite.Unavailable, ""));
        Assert.Equal("desktop", new FeatureAccess("ai.local", ExecutionSite.PairedHost, "desktop").Device);
    }

    [Theory]
    [InlineData("{\"Room\":[],\"Role\":\"host\",\"Expires\":1}")]
    [InlineData("null")]
    [InlineData("{not json")]
    [InlineData("{\"Room\":null,\"Role\":\"host\",\"Expires\":1}")]
    public void AuthenticatedMalformedClaimsAreRejected(string claims)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var signer = new RelayTickets(Convert.ToBase64String(key));
        byte[] payload = Encoding.UTF8.GetBytes(claims);
        string ticket = Encode(payload) + "." + Encode(HMACSHA256.HashData(key, payload));
        Assert.Null(signer.Validate(ticket, DateTimeOffset.UtcNow));
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
