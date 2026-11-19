using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using HelL.Contracts;

namespace HelL.Core;

public partial class BackendApi
{
    private const int RemoteTaskCapacity = 80;
    private const int RemoteSnapshotLimit = 40;
    private const int RemoteTaskLogLimit = 20;
    private readonly object remoteTaskGate = new object();
    private readonly Dictionary<string, RemoteTaskState> remoteTasks = new Dictionary<string, RemoteTaskState>(StringComparer.Ordinal);
    private readonly List<string> remoteTaskOrder = new List<string>();

    private sealed class RemoteTaskState
    {
        public string Id = "";
        public string Title = "";
        public string State = "running";
        public double? ProgressPercent;
        public string Stage = "";
        public string Speed = "";
        public string Message = "";
        public bool Crash;
        public long StartedAtUnix;
        public long FinishedAtUnix;
        public readonly Queue<string> RecentLogs = new Queue<string>();
    }

    private void TrackRemoteTask(string eventName, PyDict data)
    {
        if (eventName is not ("task_added" or "progress" or "log" or "crash" or "finished")) return;
        string id = Py.S(data?.Get("task_id"));
        if (string.IsNullOrWhiteSpace(id)) return;

        lock (remoteTaskGate)
        {
            if (!remoteTasks.TryGetValue(id, out RemoteTaskState? task))
            {
                if (eventName != "task_added") return;
                task = new RemoteTaskState
                {
                    Id = id,
                    Title = HostRemoteDiagnostics.Clean(Py.S(data.Get("title")), 128),
                    StartedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                remoteTasks.Add(id, task);
                remoteTaskOrder.Add(id);
                TrimRemoteTasks();
            }

            switch (eventName)
            {
                case "progress":
                    task.ProgressPercent = Percent(data.Get("current"), data.Get("total"));
                    task.Stage = HostRemoteDiagnostics.Clean(Py.S(data.Get("message")), 160);
                    int speedSeparator = task.Stage.IndexOf("  |  ", StringComparison.Ordinal);
                    if (speedSeparator >= 0)
                    {
                        task.Speed = task.Stage.Substring(speedSeparator + 5).Trim();
                        task.Stage = task.Stage.Substring(0, speedSeparator).Trim();
                    }
                    else task.Speed = "";
                    break;
                case "log":
                    string line = HostRemoteDiagnostics.Clean(Py.S(data.Get("text")));
                    if (line.Length != 0)
                    {
                        task.RecentLogs.Enqueue(line);
                        while (task.RecentLogs.Count > RemoteTaskLogLimit) task.RecentLogs.Dequeue();
                    }
                    break;
                case "crash":
                    task.Crash = true;
                    break;
                case "finished":
                    bool success = Py.Truthy(data.Get("success"));
                    task.State = success ? "completed" : "failed";
                    task.Message = HostRemoteDiagnostics.Clean(Py.S(data.Get("message")), 512);
                    task.Crash |= Py.Truthy(data.Get("crash"));
                    if (success) task.ProgressPercent = 100;
                    task.Stage = task.Message;
                    task.Speed = "";
                    task.FinishedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    break;
            }
        }
    }

    internal HostTask[] remote_task_snapshot()
    {
        lock (remoteTaskGate)
            return remoteTasks.Values.OrderBy(task => task.State != "running")
                .ThenByDescending(task => task.StartedAtUnix).ThenBy(task => task.Id, StringComparer.Ordinal)
                .Take(RemoteSnapshotLimit).Select(ToHostTask).ToArray();
    }

    internal bool remote_tasks_truncated()
    {
        lock (remoteTaskGate) return remoteTasks.Count > RemoteSnapshotLimit;
    }

    internal bool remote_cancel_task(string taskId)
    {
        BackendWorker worker;
        lock (_lock)
        {
            if (taskId == _launch_task_id || !_workers.TryGetValue(taskId, out worker)) return false;
        }
        // Remote cancellation never enters cancel_task's process-termination branch.
        worker.cancel();
        return true;
    }

    internal HostTaskDetails? remote_task_details(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("A task ID is required.", nameof(taskId));
        lock (remoteTaskGate)
        {
            if (!remoteTasks.TryGetValue(taskId, out RemoteTaskState? task)) return null;
            return new HostTaskDetails
            {
                TaskId = task.Id,
                Message = task.Message,
                Crash = task.Crash,
                RecentLogs = task.RecentLogs.ToArray()
            };
        }
    }

    private void TrimRemoteTasks()
    {
        while (remoteTasks.Count > RemoteTaskCapacity && remoteTaskOrder.Count != 0)
        {
            string id = remoteTaskOrder.FirstOrDefault(key => remoteTasks[key].State != "running") ?? remoteTaskOrder[0];
            remoteTasks.Remove(id);
            remoteTaskOrder.Remove(id);
        }
    }

    private static HostTask ToHostTask(RemoteTaskState task) => new HostTask
    {
        Id = task.Id,
        Title = task.Title,
        State = task.State,
        ProgressPercent = task.ProgressPercent,
        Stage = task.Stage,
        Speed = task.Speed,
        Message = task.Message,
        Crash = task.Crash,
        StartedAtUnix = task.StartedAtUnix,
        FinishedAtUnix = task.FinishedAtUnix
    };

    private static double? Percent(object? current, object? total)
    {
        if (!Number(current, out double done) || !Number(total, out double full) || done < 0 || full <= 0) return null;
        return Math.Max(0, Math.Min(100, done * 100 / full));
    }

    private static bool Number(object? value, out double number)
    {
        try
        {
            number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            number = 0;
            return false;
        }
    }

    internal HostGameStatus remote_game_status()
    {
        lock (_game_lock)
        {
            Process? process = _game_proc;
            bool running;
            try { running = process is not null && !process.HasExited; }
            catch (InvalidOperationException) { running = false; }
            catch (System.ComponentModel.Win32Exception) { running = process is not null; }
            return new HostGameStatus
            {
                Running = running,
                SessionId = running ? _game_session_id : "",
                State = !running ? "stopped" : _game_stop_requested ? "stopping" : "running",
                Message = !running ? "No game is currently running." : _game_stop_requested ? "A graceful close was requested." : "The game is running."
            };
        }
    }

    internal bool remote_request_game_stop(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A game session ID is required.", nameof(sessionId));
        lock (_game_lock)
        {
            if (_game_proc is null || _game_session_id.Length == 0 || _game_session_id != sessionId) return false;
            try
            {
                if (_game_proc.HasExited) return false;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (_game_stop_requested && now - _game_stop_requested_at < 10) return true;
                if (!_game_proc.CloseMainWindow()) return false;
                _game_stop_requested = true;
                _game_stop_requested_at = now;
                return true;
            }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
    }
}
