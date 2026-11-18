using HelL.Contracts;
using HelL.Core;
using HelL.Tests;
using System.Runtime.CompilerServices;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class HostCommandTests
{
    [Fact]
    public void CommandsUseTheExistingOwnerAndRequireExplicitPermissions()
    {
        using var fixture = new BackendFixture();
        var service = new HostCommandService(fixture.Api, "test-host");
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        HostReply denied = service.Execute(request, Array.Empty<HostPermission>());
        Assert.False(denied.Ok);
        Assert.Equal("permission_denied", denied.ErrorCode);
        HostReply snapshot = service.Execute(request, new[] { HostPermission.Read });
        Assert.True(snapshot.Ok);
        Assert.Equal("test-host", snapshot.Device);
        request.Command = HostCommand.CancelTask;
        request.TaskId = "nonexistent";
        Assert.Equal("permission_denied", service.Execute(request, new[] { HostPermission.Read }).ErrorCode);
        Assert.Equal("task_not_running", service.Execute(request, new[] { HostPermission.Read, HostPermission.CancelTasks }).ErrorCode);
    }

    [Fact]
    public void RemoteCommandsCannotInvokeArbitraryBackendMethods()
    {
        using var fixture = new BackendFixture();
        var service = new HostCommandService(fixture.Api, "test-host");
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = (HostCommand)999 };
        Assert.Equal("invalid_request", service.Execute(request, new[] { HostPermission.Read, HostPermission.CancelTasks }).ErrorCode);
    }

    [Fact]
    public void CancellationReceiptsAreIdempotentAndCannotBypassRevokedPermissions()
    {
        using var fixture = new BackendFixture();
        var service = new HostCommandService(fixture.Api, "test-host");
        string task = fixture.Api.StartTask("cancel from paired device", context =>
        {
            if (!context.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Host cancellation was not delivered.");
            context.Token.ThrowIfCancellationRequested();
            return "unexpected completion";
        });
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = task };
        var permissions = new[] { HostPermission.Read, HostPermission.CancelTasks };
        HostReply first = service.Execute(request, permissions);
        Assert.True(first.Ok);
        Assert.False(HelL.Core.Py.Truthy(fixture.Api.wait_task(task, 5L)["ok"]));
        first.Ok = false;
        first.Device = "modified by caller";
        request.RequestId = request.RequestId.ToUpperInvariant();
        HostReply repeated = service.Execute(request, permissions);
        Assert.True(repeated.Ok);
        Assert.Equal(request.RequestId, repeated.RequestId);
        Assert.Equal("test-host", repeated.Device);
        Assert.Equal("permission_denied", service.Execute(request, new[] { HostPermission.Read }).ErrorCode);
        request.TaskId = "different-task";
        Assert.Equal("request_conflict", service.Execute(request, permissions).ErrorCode);
    }

    [Fact]
    public void OwnerFailuresReturnAPlainStructuredError()
    {
        var owner = (BackendApi)RuntimeHelpers.GetUninitializedObject(typeof(BackendApi));
        var service = new HostCommandService(owner, "test-host");
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        HostReply reply = service.Execute(request, new[] { HostPermission.Read });
        Assert.False(reply.Ok);
        Assert.Equal("snapshot_failed", reply.ErrorCode);
        Assert.Equal(request.RequestId, reply.RequestId);
        Assert.DoesNotContain(nameof(NullReferenceException), reply.Error);
        Assert.Empty(reply.Tasks);
    }
}
