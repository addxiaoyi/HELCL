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

    [Theory]
    [InlineData("missing-protocol")]
    [InlineData("duplicate-outcome")]
    [InlineData("duplicate-task-field")]
    [InlineData("duplicate-game-field")]
    public void RemoteAndCompanionReplyReadersRejectDefaultedOrAmbiguousWireFields(string fault)
    {
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        string prefix = "{\"RequestId\":\"" + request.RequestId + "\",\"Device\":\"owner\",";
        string json = prefix + (fault switch
        {
            "missing-protocol" => "\"Ok\":true,\"Tasks\":[]}",
            "duplicate-outcome" => "\"Protocol\":1,\"Ok\":false,\"Ok\":true,\"Tasks\":[]}",
            "duplicate-task-field" => "\"Protocol\":1,\"Ok\":true,\"Tasks\":[{\"Id\":\"one\",\"Id\":\"two\",\"State\":\"running\"}]}",
            _ => "\"Protocol\":1,\"Ok\":true,\"Tasks\":[],\"Game\":{\"State\":\"running\",\"State\":\"stopped\"}}"
        });
        Assert.Throws<InvalidDataException>(() => HostWireMessages.ReadReply(Encoding.UTF8.GetBytes(json), request));
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Theory]
    [InlineData("device")]
    [InlineData("task-id")]
    [InlineData("detail-id")]
    [InlineData("error-code")]
    public void ControlCharactersCannotMakeReplyIdentifiersAmbiguous(string field)
    {
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        var reply = new HostReply { RequestId = request.RequestId, Device = "host", Ok = true };
        switch (field)
        {
            case "device": reply.Device = "host\nother"; break;
            case "task-id": reply.Tasks = [new HostTask { Id = "task\nother", State = "running" }]; break;
            case "detail-id": reply.Detail = new HostTaskDetails { TaskId = "task\nother" }; break;
            case "error-code": reply.Ok = false; reply.ErrorCode = "denied\nother"; reply.Error = "denied"; break;
        }
        Assert.Throws<ArgumentException>(() => reply.Validate(request));
    }
}
