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

    [Fact]
    public void SnapshotAndTaskDetailsReturnBoundedRedactedProgress()
    {
        using var fixture = new BackendFixture();
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        string taskId = fixture.Api.StartTask("download fixture", context =>
        {
            context.Progress(1L, 4L, "Downloading fixture  |  2 MB/s");
            context.Log("Authorization: Bearer do-not-export-this");
            context.Log(@"Opened C:\Users\Private\launcher\secret.txt");
            ready.Set();
            release.Wait(context.Token);
            return "complete";
        });
        Assert.True(ready.Wait(TimeSpan.FromSeconds(3)));

        var service = new HostCommandService(fixture.Api, "test-host");
        var permissions = new[] { HostPermission.Read };
        HostReply snapshot = service.Execute(new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot }, permissions);
        HostTask task = Assert.Single(snapshot.Tasks, item => item.Id == taskId);
        Assert.Equal("running", task.State);
        Assert.Equal(25d, task.ProgressPercent);
        Assert.Equal("Downloading fixture", task.Stage);
        Assert.Equal("2 MB/s", task.Speed);

        HostReply details = service.Execute(new HostRequest
        {
            RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.TaskDetails, TaskId = taskId
        }, permissions);
        Assert.True(details.Ok);
        Assert.Equal(taskId, details.Detail!.TaskId);
        Assert.Contains(details.Detail.RecentLogs, line => line.Contains("[REDACTED]", StringComparison.Ordinal));
        Assert.DoesNotContain("do-not-export-this", string.Join("\n", details.Detail.RecentLogs), StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\Private", string.Join("\n", details.Detail.RecentLogs), StringComparison.OrdinalIgnoreCase);

        release.Set();
        Assert.True(HelL.Core.Py.Truthy(fixture.Api.wait_task(taskId, 3L)["ok"]));
        HostReply completed = service.Execute(new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot }, permissions);
        Assert.Equal("completed", Assert.Single(completed.Tasks, item => item.Id == taskId).State);
    }

    [Fact]
    public void GameStatusIsReadableButGracefulStopNeedsItsOwnPermissionAndCurrentSession()
    {
        using var fixture = new BackendFixture();
        var service = new HostCommandService(fixture.Api, "test-host");
        var status = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.GameStatus };
        HostReply snapshot = service.Execute(status, new[] { HostPermission.Read });
        Assert.True(snapshot.Ok);
        Assert.False(snapshot.Game!.Running);

        var stop = new HostRequest
        {
            RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.StopGame,
            SessionId = Guid.NewGuid().ToString("N")
        };
        Assert.Equal("permission_denied", service.Execute(stop, new[] { HostPermission.Read }).ErrorCode);
        Assert.Equal("game_not_running", service.Execute(stop, new[] { HostPermission.Read, HostPermission.ControlGame }).ErrorCode);
    }

    [Fact]
    public void StopGameRequiresAValidSessionAndOtherCommandsRejectSessionFields()
    {
        var missingSession = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.StopGame };
        Assert.Throws<ArgumentException>(missingSession.Validate);
        var unexpectedSession = new HostRequest
        {
            RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot,
            SessionId = Guid.NewGuid().ToString("N")
        };
        Assert.Throws<ArgumentException>(unexpectedSession.Validate);
        var nullSession = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot, SessionId = null! };
        Assert.Throws<ArgumentException>(nullSession.Validate);
    }

    [Theory]
    [InlineData("{\"access_token\":\"sensitive-value\"}")]
    [InlineData("--accessToken sensitive-value")]
    [InlineData("Authorization: Basic sensitive-value")]
    [InlineData("https://service.test/?token=sensitive-value")]
    [InlineData("登录码：sensitive-value")]
    [InlineData(@"Open \\server\private\sensitive-value")]
    [InlineData("Open /var/private/sensitive-value")]
    public void RemoteLogsOmitSensitiveValuesInDifferentFormats(string input)
    {
        string sanitized = HostRemoteDiagnostics.Clean(input);
        Assert.DoesNotContain("sensitive-value", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void CancellingALaunchTaskCannotBypassGameControlPermission()
    {
        using var fixture = new BackendFixture();
        using var release = new ManualResetEventSlim();
        string task = fixture.Api.StartTask("game launch", context =>
        {
            release.Wait(context.Token);
            return "done";
        });
        fixture.Api._launch_task_id = task;
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = task };
        var service = new HostCommandService(fixture.Api, "test-host");
        HostReply reply = service.Execute(request, new[] { HostPermission.Read, HostPermission.CancelTasks });
        Assert.Equal("game_control_requires_confirmation", reply.ErrorCode);
        lock (fixture.Api._lock) Assert.False(fixture.Api._workers[task].IsCancelled);
        release.Set();
    }

    [Fact]
    public void TaskDetailsHaveABoundedLogAndSnapshotWindow()
    {
        using var fixture = new BackendFixture();
        string last = "";
        for (int i = 0; i < 100; i++)
        {
            string taskId = "fixture-" + i;
            fixture.Api.Emit("task_added", PyDict.Of("task_id", taskId, "title", "bounded fixture"));
            for (int line = 0; line < 30; line++) fixture.Api.Emit("log", PyDict.Of("task_id", taskId, "text", new string('x', 2048)));
            fixture.Api.Emit("finished", PyDict.Of("task_id", taskId, "success", true, "message", "complete"));
            last = taskId;
        }
        Assert.Equal(40, fixture.Api.remote_task_snapshot().Length);
        Assert.Null(fixture.Api.remote_task_details("fixture-0"));
        HostTaskDetails detail = fixture.Api.remote_task_details(last)!;
        Assert.Equal(20, detail.RecentLogs.Length);
        Assert.All(detail.RecentLogs, line => Assert.True(line.Length <= 512));
    }

    [Fact]
    public void RunningTaskSurvivesHistoryChurnAndRemoteInternalsAreNotRpcMethods()
    {
        using var fixture = new BackendFixture();
        fixture.Api.Emit("task_added", PyDict.Of("task_id", "long-running", "title", "long-running"));
        for (int i = 0; i < 100; i++)
        {
            string task = "finished-" + i;
            fixture.Api.Emit("task_added", PyDict.Of("task_id", task, "title", "short fixture"));
            fixture.Api.Emit("finished", PyDict.Of("task_id", task, "success", true, "message", "complete"));
        }
        fixture.Api.Emit("progress", PyDict.Of("task_id", "long-running", "current", 3L, "total", 4L, "message", "still working"));
        HostTask active = Assert.Single(fixture.Api.remote_task_snapshot(), task => task.Id == "long-running");
        Assert.Equal(75d, active.ProgressPercent);
        Assert.NotNull(fixture.Api.remote_task_details("long-running"));
        Assert.True(fixture.Api.remote_tasks_truncated());
        Assert.False(fixture.Rpc.Has("remote_task_snapshot"));
        Assert.False(fixture.Rpc.Has("remote_request_game_stop"));
    }

    [Fact]
    public void LateProgressCannotOverwriteATasksTerminalOutcome()
    {
        using var fixture = new BackendFixture();
        fixture.Api.Emit("task_added", PyDict.Of("task_id", "terminal", "title", "fixture"));
        fixture.Api.Emit("finished", PyDict.Of("task_id", "terminal", "success", true, "message", "verified complete"));
        fixture.Api.Emit("progress", PyDict.Of("task_id", "terminal", "current", 1L, "total", 10L, "message", "late callback  |  2 MB/s"));

        HostTask terminal = Assert.Single(fixture.Api.remote_task_snapshot());
        Assert.Equal("completed", terminal.State);
        Assert.Equal(100d, terminal.ProgressPercent);
        Assert.Equal("verified complete", terminal.Stage);
        Assert.Empty(terminal.Speed);
    }

    [Fact]
    public void SnapshotOrderUsesEventOrderAndLongOutcomesRemainWithinWireLimits()
    {
        using var fixture = new BackendFixture();
        for (int index = 0; index < 50; index++)
        {
            string id = "reverse-" + (50 - index).ToString("D3");
            fixture.Api.Emit("task_added", PyDict.Of("task_id", id, "title", "fixture"));
            fixture.Api.Emit("finished", PyDict.Of("task_id", id, "success", false, "message", new string('x', 400)));
        }
        var service = new HostCommandService(fixture.Api, "test-host");
        var request = new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot };
        HostReply reply = service.Execute(request, [HostPermission.Read]);
        reply.Validate(request);
        Assert.Equal("reverse-001", reply.Tasks[0].Id);
        Assert.Equal("reverse-040", reply.Tasks[^1].Id);
        Assert.All(reply.Tasks, task => { Assert.Equal(400, task.Message.Length); Assert.Equal(160, task.Stage.Length); });
    }
}
