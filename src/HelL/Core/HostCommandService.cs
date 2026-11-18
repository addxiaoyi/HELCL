using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Contracts;

namespace HelL.Core;

// Operates on the existing owner; creating a second BackendApi would split task state.
public sealed class HostCommandService
{
    private const int RememberedCommands = 128;
    private readonly BackendApi api;
    private readonly string device;
    private readonly object gate = new object();
    private readonly Dictionary<string, (string task, HostReply reply)> cancelled = new Dictionary<string, (string, HostReply)>(StringComparer.Ordinal);
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
        if (request.Command == HostCommand.Snapshot)
        {
            try { return Snapshot(request.RequestId); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("HostCommandService.Snapshot: " + ex);
                return Failure(request.RequestId, "snapshot_failed", "The host could not read its task state. Refresh and try again.");
            }
        }
        if (!permissions.Contains(HostPermission.CancelTasks)) return Failure(request.RequestId, "permission_denied", "This device cannot cancel host tasks.");
        lock (gate)
        {
            string requestKey = request.RequestId.ToLowerInvariant();
            if (cancelled.TryGetValue(requestKey, out var previous))
            {
                if (previous.task != request.TaskId) return Failure(request.RequestId, "request_conflict", "This request ID was already used for another task.");
                HostReply receipt = Clone(previous.reply);
                receipt.RequestId = request.RequestId;
                return receipt;
            }
            HostReply reply;
            try
            {
                PyDict current = api.list_tasks();
                bool running = Rows(current["running"]).Any(row => Py.S(row["task_id"]) == request.TaskId);
                if (!running) return Failure(request.RequestId, "task_not_running", "The task is no longer running. Refresh host state.");
                api.cancel_task(request.TaskId);
                reply = new HostReply { RequestId = request.RequestId, Ok = true, Device = device };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("HostCommandService.CancelTask: " + ex);
                return Failure(request.RequestId, "cancel_failed", "The host could not cancel this task. Check its task log.");
            }
            cancelled.Add(requestKey, (request.TaskId, Clone(reply)));
            requests.Enqueue(requestKey);
            if (requests.Count > RememberedCommands) cancelled.Remove(requests.Dequeue());
            return reply;
        }
    }

    private HostReply Snapshot(string request)
    {
        PyDict state = api.list_tasks();
        var tasks = Rows(state["running"]).Select(row => new HostTask
        {
            Id = Py.S(row["task_id"]), Title = Py.S(row["title"]), State = "running"
        }).Concat(Rows(state["finished"]).Select(row => new HostTask
        {
            Id = Py.S(row["task_id"]), State = Py.Truthy(row["success"]) ? "completed" : "failed"
        })).ToArray();
        return new HostReply { RequestId = request, Device = device, Ok = true, Tasks = tasks };
    }

    private HostReply Failure(string request, string code, string message) => new HostReply
    { RequestId = request ?? "", Device = device, ErrorCode = code, Error = message };

    private static HostReply Clone(HostReply reply) => new HostReply
    { RequestId = reply.RequestId, Ok = reply.Ok, ErrorCode = reply.ErrorCode, Error = reply.Error, Device = reply.Device };

    private static IEnumerable<PyDict> Rows(object value) => (value as IEnumerable<object> ?? Array.Empty<object>()).OfType<PyDict>();
}
