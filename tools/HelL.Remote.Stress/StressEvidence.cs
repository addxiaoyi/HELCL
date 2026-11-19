using System.Diagnostics;
using System.Text.Json;
using HelL.Remote;

namespace HelL.Remote.Stress;

public sealed record StressError(int Cycle, string Stage, string Type, string Message)
{
    public static StressError From(Exception error, int cycle, string stage) => new(cycle, stage,
        error.GetType().Name, error is StressCheckFailure ? error.Message : "The operation failed. Exception payloads are omitted because they may contain credentials.");
}

internal sealed class StressCheckFailure(string message) : Exception(message);

public sealed class StressReport
{
    public string Scope { get; } = "One process hosting a real loopback TCP/WebSocket relay with pinned inner TLS 1.3. In-memory secret-store and task-owner fixtures; not native OS credentials, public Internet, two physical devices, or a full launcher acceptance.";
    public string Status { get; set; } = "running";
    public required StressOptions Options { get; init; }
    public int CompletedCycles { get; set; }
    public int RelayRestarts { get; set; }
    public int Snapshots { get; set; }
    public int Cancellations { get; set; }
    public int ReadOnlyDenials { get; set; }
    public int RevocationDenials { get; set; }
    public int FeedRecoveries { get; set; }
    public int PeakTrackedRooms { get; set; }
    public int PeakTrackedFeeds { get; set; }
    public long PeakWorkingSetBytes { get; set; }
    public long PeakManagedBytes { get; set; }
    public int PeakThreads { get; set; }
    public int PeakHandles { get; set; }
    public int PeakSecretEntries { get; set; }
    public double ElapsedSeconds { get; set; }
    public bool RelayStopped { get; set; }
    public bool HostSessionsStopped { get; set; }
    public bool OwnedWorkspaceRemoved { get; set; }
    public int FinalSecretEntries { get; set; }
    public int FinalFixtureTasks { get; set; }
    public List<StressError> Errors { get; } = [];
    public string FixtureLimits { get; } = "1,024 feed records and 4,096 room records; unexpired revocation tombstones intentionally accumulate. Management/anonymous request budgets are raised to 100,000/minute only inside this isolated lifecycle fixture; ordinary per-role connection and per-reader budgets remain enabled.";
}

internal sealed class ResourceMeter(StressReport report)
{
    private readonly object gate = new();
    private int secrets;
    internal int SecretEntries => Volatile.Read(ref secrets);
    internal void KeyAdded() { int count = Interlocked.Increment(ref secrets); lock (gate) report.PeakSecretEntries = Math.Max(report.PeakSecretEntries, count); }
    internal void KeyRemoved() => Interlocked.Decrement(ref secrets);
    internal void Sample()
    {
        using Process process = Process.GetCurrentProcess();
        lock (gate)
        {
            report.PeakWorkingSetBytes = Math.Max(report.PeakWorkingSetBytes, process.WorkingSet64);
            report.PeakManagedBytes = Math.Max(report.PeakManagedBytes, GC.GetTotalMemory(false));
            report.PeakThreads = Math.Max(report.PeakThreads, process.Threads.Count);
            report.PeakHandles = Math.Max(report.PeakHandles, process.HandleCount);
        }
    }
}

internal sealed class StressSecrets(ResourceMeter meter) : IRemoteSecretStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> values = new();
    public string Read(string key) => values.GetValueOrDefault(key, "");
    public void Write(string key, string value)
    {
        if (values.TryAdd(key, value)) meter.KeyAdded(); else values[key] = value;
    }
    public void Delete(string key) { if (values.TryRemove(key, out _)) meter.KeyRemoved(); }
    internal void Clear() { foreach (string key in values.Keys) Delete(key); }
}

public sealed class OwnedWorkspace
{
    private const string Prefix = "hell-remote-stress-";
    private readonly string id = Guid.NewGuid().ToString("N");
    private readonly string parent;
    public string Root { get; }
    public OwnedWorkspace()
    {
        parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        Root = Path.Combine(parent, Prefix + id);
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, ".owner"), id);
    }
    public void Remove()
    {
        if (!Directory.Exists(Root)) return;
        var directory = new DirectoryInfo(Root);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(directory.Parent?.FullName.TrimEnd(Path.DirectorySeparatorChar), parent, comparison)
            || directory.Name != Prefix + id || directory.LinkTarget is not null
            || File.ReadAllText(Path.Combine(Root, ".owner")) != id)
            throw new InvalidOperationException("Refusing to remove a workspace without this run's ownership marker.");
        Directory.Delete(Root, recursive: true);
    }
}
