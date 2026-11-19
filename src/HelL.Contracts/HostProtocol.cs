using System;
using System.Linq;

namespace HelL.Contracts;

public static class HostProtocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 1024 * 1024;
    public const int MaxTaskLogLines = 500;
}

public enum HostCommand { Snapshot = 1, CancelTask = 2, TaskDetails = 3, GameStatus = 4, StopGame = 5 }
public enum HostPermission { Read = 1, CancelTasks = 2, ControlGame = 3 }

public sealed class HostRequest
{
    public int Protocol { get; set; } = HostProtocol.Version;
    public string RequestId { get; set; } = "";
    public HostCommand Command { get; set; }
    public string TaskId { get; set; } = "";
    public string SessionId { get; set; } = "";

    public void Validate()
    {
        if (Protocol != HostProtocol.Version) throw new ArgumentException("The host protocol versions differ.");
        if (!Guid.TryParseExact(RequestId, "N", out _)) throw new ArgumentException("Request ID must be a UUID in N format.");
        if (!Enum.IsDefined(typeof(HostCommand), Command)) throw new ArgumentException("This host command is not allowed.");
        if (TaskId == null) throw new ArgumentException("Task ID cannot be null.");
        if (SessionId == null) throw new ArgumentException("Game session ID cannot be null.");
        bool requiresTaskId = Command is HostCommand.CancelTask or HostCommand.TaskDetails;
        if (requiresTaskId && (string.IsNullOrWhiteSpace(TaskId) || TaskId.Length > 128 || TaskId.Any(char.IsControl)))
            throw new ArgumentException("A valid task ID is required.");
        if (!requiresTaskId && TaskId.Length != 0)
            throw new ArgumentException("This host command does not accept a task ID.");
        if (Command == HostCommand.StopGame && !Guid.TryParseExact(SessionId, "N", out _))
            throw new ArgumentException("A valid game session ID is required.");
        if (Command != HostCommand.StopGame && SessionId.Length != 0)
            throw new ArgumentException("This host command does not accept a game session ID.");
    }
}

public sealed class HostTask
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public double? ProgressPercent { get; set; }
    public string Stage { get; set; } = "";
    public string Speed { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Crash { get; set; }
    public long StartedAtUnix { get; set; }
    public long FinishedAtUnix { get; set; }
}

public sealed class HostTaskDetails
{
    public string TaskId { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Crash { get; set; }
    public string[] RecentLogs { get; set; } = Array.Empty<string>();
}

public sealed class HostGameStatus
{
    public bool Running { get; set; }
    public string SessionId { get; set; } = "";
    public string State { get; set; } = "stopped";
    public int? ExitCode { get; set; }
    public string Message { get; set; } = "";
}

public sealed class HostReply
{
    public int Protocol { get; set; } = HostProtocol.Version;
    public string RequestId { get; set; } = "";
    public bool Ok { get; set; }
    public string ErrorCode { get; set; } = "";
    public string Error { get; set; } = "";
    public string Device { get; set; } = "";
    public HostTask[] Tasks { get; set; } = Array.Empty<HostTask>();
    public bool TasksTruncated { get; set; }
    public HostTaskDetails? Detail { get; set; }
    public HostGameStatus? Game { get; set; }
}
