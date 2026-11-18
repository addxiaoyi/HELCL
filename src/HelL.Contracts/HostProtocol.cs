using System;

namespace HelL.Contracts;

public static class HostProtocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 1024 * 1024;
    public const int MaxTaskLogLines = 500;
}

public enum HostCommand { Snapshot = 1, CancelTask = 2 }
public enum HostPermission { Read = 1, CancelTasks = 2 }

public sealed class HostRequest
{
    public int Protocol { get; set; } = HostProtocol.Version;
    public string RequestId { get; set; } = "";
    public HostCommand Command { get; set; }
    public string TaskId { get; set; } = "";

    public void Validate()
    {
        if (Protocol != HostProtocol.Version) throw new ArgumentException("The host protocol versions differ.");
        if (!Guid.TryParseExact(RequestId, "N", out _)) throw new ArgumentException("Request ID must be a UUID in N format.");
        if (!Enum.IsDefined(typeof(HostCommand), Command)) throw new ArgumentException("This host command is not allowed.");
        if (Command == HostCommand.CancelTask && (string.IsNullOrWhiteSpace(TaskId) || TaskId.Length > 128))
            throw new ArgumentException("A valid task ID is required.");
        if (TaskId == null) throw new ArgumentException("Task ID cannot be null.");
        if (Command == HostCommand.Snapshot && TaskId.Length != 0)
            throw new ArgumentException("Snapshot does not accept a task ID.");
    }
}

public sealed class HostTask
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
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
}
