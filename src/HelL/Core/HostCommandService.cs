using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Contracts;

namespace HelL.Core;

// Dispatches an explicit, small remote surface into the existing application owner.
public sealed class HostCommandService
{
    private const int RememberedCommands = 128;
    private readonly BackendApi api;
    private readonly string device;
    private readonly object gate = new object();
    private readonly Dictionary<string, (HostCommand command, string target, HostReply reply)> receipts =
        new Dictionary<string, (HostCommand, string, HostReply)>(StringComparer.Ordinal);
    private readonly Queue<string> requests = new Queue<string>();

    public HostCommandService(BackendApi api, string device)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("A host device name is required.", nameof(device));
        this.device = device;
    }

    public HostReply Execute(HostRequest request, IReadOnlyCollection<HostPermission> permissions)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (permissions == null) throw new ArgumentNullException(nameof(permissions));
        try { request.Validate(); }
        catch (ArgumentException) { return Failure(request.RequestId, "invalid_request", "The host request is invalid or uses a different protocol."); }
        if (!permissions.Contains(HostPermission.Read)) return Failure(request.RequestId, "permission_denied", "This device cannot read host state.");

        switch (request.Command)
        {
            case HostCommand.Snapshot:
                return Snapshot(request.RequestId);
            case HostCommand.TaskDetails:
                return TaskDetails(request.RequestId, request.TaskId);
            case HostCommand.GameStatus:
                return GameStatus(request.RequestId);
            case HostCommand.CancelTask:
                if (!permissions.Contains(HostPermission.CancelTasks))
                    return Failure(request.RequestId, "permission_denied", "This device cannot cancel host tasks.");
                return CancelTask(request);
            case HostCommand.StopGame:
                if (!permissions.Contains(HostPermission.ControlGame))
                    return Failure(request.RequestId, "permission_denied", "This device cannot request a game to close.");
                return StopGame(request);
            default:
                return Failure(request.RequestId, "invalid_request", "The host request is invalid or uses a different protocol.");
        }
    }

    private HostReply Snapshot(string request)
    {
        try
        {
            var snapshot = api.remote_task_state();
            return new HostReply { RequestId = request, Device = device, Ok = true, Tasks = snapshot.Tasks, TasksTruncated = snapshot.Truncated };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError("HostCommandService.Snapshot: " + error.GetType().Name);
            return Failure(request, "snapshot_failed", "The host could not read its task state. Refresh and try again.");
        }
    }

    private HostReply TaskDetails(string request, string taskId)
    {
        try
        {
            HostTaskDetails? detail = api.remote_task_details(taskId);
            return detail is null
                ? Failure(request, "task_not_found", "The task details are no longer available. Refresh host state.")
                : new HostReply { RequestId = request, Device = device, Ok = true, Detail = detail };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError("HostCommandService.TaskDetails: " + error.GetType().Name);
            return Failure(request, "task_details_failed", "The host could not read task details. Refresh and try again.");
        }
    }

    private HostReply GameStatus(string request)
    {
        try { return new HostReply { RequestId = request, Device = device, Ok = true, Game = api.remote_game_status() }; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError("HostCommandService.GameStatus: " + error.GetType().Name);
            return Failure(request, "game_status_failed", "The host could not read the current game state. Refresh and try again.");
        }
    }

    private HostReply CancelTask(HostRequest request)
    {
        lock (gate)
        {
            string key = request.RequestId.ToLowerInvariant();
            HostReply? replay = Replay(key, request, request.TaskId);
            if (replay is not null) return replay;

            try
            {
                PyDict current = api.list_tasks();
                bool running = Rows(current["running"]).Any(row => Py.S(row["task_id"]) == request.TaskId);
                if (!running) return Failure(request.RequestId, "task_not_running", "The task is no longer running. Refresh host state.");
                if (request.TaskId == api._launch_task_id)
                    return Failure(request.RequestId, "game_control_requires_confirmation", "Use the separate graceful game-close action for a game session.");
                if (!api.remote_cancel_task(request.TaskId))
                    return Failure(request.RequestId, "task_not_running", "The task is no longer running. Refresh host state.");
                HostReply reply = new HostReply { RequestId = request.RequestId, Ok = true, Device = device };
                Remember(key, request.Command, request.TaskId, reply);
                return reply;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                System.Diagnostics.Trace.TraceError("HostCommandService.CancelTask: " + error.GetType().Name);
                return Failure(request.RequestId, "cancel_failed", "The host could not cancel this task. Check its task log.");
            }
        }
    }

    private HostReply StopGame(HostRequest request)
    {
        lock (gate)
        {
            string key = request.RequestId.ToLowerInvariant();
            HostReply? replay = Replay(key, request, request.SessionId);
            if (replay is not null) return replay;

            try
            {
                HostGameStatus current = api.remote_game_status();
                if (!current.Running) return Failure(request.RequestId, "game_not_running", "No game is currently running.");
                if (!string.Equals(current.SessionId, request.SessionId, StringComparison.Ordinal))
                    return Failure(request.RequestId, "game_session_changed", "The game session changed. Refresh before requesting a close.");
                if (!api.remote_request_game_stop(request.SessionId))
                    return Failure(request.RequestId, "graceful_stop_unavailable", "The game did not accept a graceful window-close request. It was left running.");

                HostReply reply = new HostReply { RequestId = request.RequestId, Ok = true, Device = device, Game = api.remote_game_status() };
                Remember(key, request.Command, request.SessionId, reply);
                return reply;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                System.Diagnostics.Trace.TraceError("HostCommandService.StopGame: " + error.GetType().Name);
                return Failure(request.RequestId, "game_stop_failed", "The host could not request a graceful game close. The game was not force-stopped.");
            }
        }
    }

    private HostReply? Replay(string key, HostRequest request, string target)
    {
        if (!receipts.TryGetValue(key, out var previous)) return null;
        if (previous.command != request.Command || previous.target != target)
            return Failure(request.RequestId, "request_conflict", "This request ID was already used for another host action.");
        HostReply receipt = Clone(previous.reply);
        receipt.RequestId = request.RequestId;
        return receipt;
    }

    private void Remember(string key, HostCommand command, string target, HostReply reply)
    {
        receipts.Add(key, (command, target, Clone(reply)));
        requests.Enqueue(key);
        while (requests.Count > RememberedCommands) receipts.Remove(requests.Dequeue());
    }

    private HostReply Failure(string request, string code, string message) => new HostReply
    { RequestId = request ?? "", Device = device, ErrorCode = code, Error = message };

    private static HostReply Clone(HostReply reply) => new HostReply
    {
        RequestId = reply.RequestId,
        Ok = reply.Ok,
        ErrorCode = reply.ErrorCode,
        Error = reply.Error,
        Device = reply.Device,
        Game = reply.Game is null ? null : new HostGameStatus
        {
            Running = reply.Game.Running,
            SessionId = reply.Game.SessionId,
            State = reply.Game.State,
            ExitCode = reply.Game.ExitCode,
            Message = reply.Game.Message
        }
    };

    private static IEnumerable<PyDict> Rows(object value) => (value as IEnumerable<object> ?? Array.Empty<object>()).OfType<PyDict>();
}
