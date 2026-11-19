using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Diagnostics;

[Flags]
public enum DiagnosticSections
{
    None = 0,
    Application = 1,
    Environment = 2,
    Storage = 4,
    Library = 8,
    LaunchChecks = 16,
    Network = 32,
    RecentIssues = 64,
    All = Application | Environment | Storage | Library | LaunchChecks | Network | RecentIssues
}

public sealed record DiagnosticSnapshot(string AppVersion, string Platform, string Architecture,
    int RuntimeMajor, int RuntimeMinor, long? FreeBytes, int InstanceCount,
    IReadOnlyList<DiagnosticFinding>? LaunchFindings = null,
    IReadOnlyList<DiagnosticMirror>? Mirrors = null,
    IReadOnlyList<DiagnosticIssue>? RecentIssues = null)
{
    public static DiagnosticSnapshot Collect(string dataDirectory, string appVersion, int instanceCount)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("A data directory is required.", nameof(dataDirectory));
        string fullPath = Path.GetFullPath(dataDirectory);
        string? driveRoot = Path.GetPathRoot(fullPath);
        long? freeBytes = null;
        if (!string.IsNullOrWhiteSpace(driveRoot))
        {
            try { freeBytes = new DriveInfo(driveRoot).AvailableFreeSpace; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }

        string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
#if HELL_PORTABLE
            : OperatingSystem.IsAndroid() ? "Android"
            : OperatingSystem.IsIOS() ? "iOS"
#endif
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Other";
        return new DiagnosticSnapshot(appVersion, platform, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.Version.Major, Environment.Version.Minor, freeBytes, instanceCount);
    }
}

public sealed record DiagnosticField(string Key, string Label, string Value);
public sealed record DiagnosticFinding(string Code, string Severity);
public sealed record DiagnosticMirror(string Provider, bool Available, long LatencyMilliseconds);
public sealed record DiagnosticIssue(string Code, string Summary);

public sealed class DiagnosticReport
{
    internal DiagnosticReport(DateTimeOffset generatedUtc, DiagnosticSections includedSections, IReadOnlyList<DiagnosticField> fields)
    {
        GeneratedUtc = generatedUtc;
        IncludedSections = includedSections;
        Fields = fields;
    }

    public DateTimeOffset GeneratedUtc { get; }
    public DiagnosticSections IncludedSections { get; }
    public IReadOnlyList<DiagnosticField> Fields { get; }

    public DiagnosticReport SelectFields(IEnumerable<string> keys)
    {
        if (keys is null) throw new ArgumentNullException(nameof(keys));
        string[] requested = keys.Take(129).ToArray();
        if (requested.Length > 128) throw new ArgumentException("Too many diagnostic fields were selected.", nameof(keys));
        var selected = new HashSet<string>(requested, StringComparer.Ordinal);
        if (selected.Any(key => !Fields.Any(field => field.Key == key)))
            throw new ArgumentException("The selected diagnostic field does not exist.", nameof(keys));
        return new DiagnosticReport(GeneratedUtc, IncludedSections, Array.AsReadOnly(Fields.Where(field => selected.Contains(field.Key)).ToArray()));
    }

    public string ToJson()
    {
        if (Fields is null) throw new ArgumentNullException(nameof(Fields));
        if (Fields.Count > 128 || Fields.Any(field => field is null || field.Key is null || field.Label is null || field.Value is null
            || field.Key.Length > 96 || field.Label.Length > 96 || field.Value.Length > 256))
            throw new InvalidDataException("The diagnostic report contains fields outside its limits.");
        var payload = new
        {
            schema_version = 1,
            generated_utc = GeneratedUtc.ToUniversalTime().ToString("O"),
            included_sections = IncludedSections.ToString(),
            fields = Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
            privacy = new[] { "No account data, credentials, device identifiers, file paths, or raw logs are included." }
        };
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 16 * 1024)
            throw new InvalidDataException("The diagnostic report exceeded its size limit.");
        return json;
    }

    public async Task WritePackageAsync(Stream destination, CancellationToken cancellation = default)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (!destination.CanWrite) throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(ToJson());
        cancellation.ThrowIfCancellationRequested();
        if (destination.CanSeek) { destination.Position = 0; destination.SetLength(0); }
        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("diagnostics.json", CompressionLevel.Fastest);
            using Stream output = entry.Open();
            await output.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
        }
        await destination.FlushAsync(cancellation).ConfigureAwait(false);
    }

    public async Task SavePackageAsync(string destination, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("A destination is required.", nameof(destination));
        string fullPath = Path.GetFullPath(destination);
        string folder = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException("A destination folder is required.", nameof(destination));
        string temporary = Path.Combine(folder, ".hell-diagnostics-" + Guid.NewGuid().ToString("N") + ".tmp");
        cancellation.ThrowIfCancellationRequested();
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                await WritePackageAsync(stream, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
            else File.Move(temporary, fullPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class DiagnosticReportBuilder
{
    private static readonly Regex VersionPattern = new(@"\A\d{1,3}(?:\.\d{1,3}){1,3}(?:[-+][A-Za-z0-9.-]{1,24})?\z", RegexOptions.CultureInvariant);

    public static DiagnosticReport Build(DiagnosticSnapshot snapshot, DiagnosticSections sections, DateTimeOffset generatedUtc)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if ((sections & ~DiagnosticSections.All) != 0) throw new ArgumentOutOfRangeException(nameof(sections));
        if (snapshot.InstanceCount is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(snapshot.InstanceCount));
        if (snapshot.RuntimeMajor is < 0 or > 100 || snapshot.RuntimeMinor is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(snapshot.RuntimeMajor));
        if (snapshot.FreeBytes is < 0) throw new ArgumentOutOfRangeException(nameof(snapshot.FreeBytes));

        var fields = new List<DiagnosticField>(7);
        if (sections.HasFlag(DiagnosticSections.Application))
            fields.Add(new("app.version", "Application version", SafeVersion(snapshot.AppVersion)));
        if (sections.HasFlag(DiagnosticSections.Environment))
        {
            fields.Add(new("system.platform", "Operating system", SafePlatform(snapshot.Platform)));
            fields.Add(new("system.architecture", "Process architecture", SafeArchitecture(snapshot.Architecture)));
            fields.Add(new("system.runtime", ".NET runtime", $"{snapshot.RuntimeMajor}.{snapshot.RuntimeMinor}"));
        }
        if (sections.HasFlag(DiagnosticSections.Storage))
            fields.Add(new("storage.available_bytes", "Available storage", snapshot.FreeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable"));
        if (sections.HasFlag(DiagnosticSections.Library))
            fields.Add(new("launcher.instance_count", "Installed instances", snapshot.InstanceCount.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (sections.HasFlag(DiagnosticSections.LaunchChecks))
        {
            foreach (DiagnosticFinding finding in (snapshot.LaunchFindings ?? Array.Empty<DiagnosticFinding>()).Take(40))
            {
                string severity = finding.Severity == "warn" ? "warning" : finding.Severity;
                if (!IsCode(finding.Code) || severity is not ("info" or "warning" or "error")) continue;
                AddUnique(fields, new($"launch_check.{finding.Code}", "Launch check", severity));
            }
        }
        if (sections.HasFlag(DiagnosticSections.Network))
        {
            foreach (DiagnosticMirror mirror in (snapshot.Mirrors ?? Array.Empty<DiagnosticMirror>()).Take(8))
            {
                if (!IsProvider(mirror.Provider) || mirror.LatencyMilliseconds is < 0 or > 600_000) continue;
                AddUnique(fields, new($"mirror.{mirror.Provider}", "Download source", mirror.Available
                    ? $"available; {mirror.LatencyMilliseconds} ms" : "unavailable"));
            }
        }
        if (sections.HasFlag(DiagnosticSections.RecentIssues))
        {
            int index = 0;
            foreach (DiagnosticIssue issue in (snapshot.RecentIssues ?? Array.Empty<DiagnosticIssue>()).Take(8))
            {
                if (!IsCode(issue.Code)) continue;
                string summary = SafeSummary(issue.Summary);
                if (summary.Length == 0) continue;
                AddUnique(fields, new($"recent_issue.{index++}", "Recent issue", $"{issue.Code}: {summary}"));
            }
        }

        return new DiagnosticReport(generatedUtc.ToUniversalTime(), sections, fields.AsReadOnly());
    }

    public static string SanitizeMessage(string? value) => SafeSummary(value);

    private static string SafeVersion(string? value) => value is { Length: <= 40 } && VersionPattern.IsMatch(value) ? value : "unknown";
    private static string SafePlatform(string? value) => value is "Windows" or "Linux" or "macOS" or "Android" or "iOS" or "Other" ? value : "Other";
    private static string SafeArchitecture(string? value) => value is "X64" or "X86" or "Arm64" or "Arm" or "Wasm" or "S390x" or "LoongArch64" ? value : "Other";
    private static bool IsCode(string value) => value is { Length: <= 48 } && Regex.IsMatch(value, @"\A[a-z0-9_]+\z", RegexOptions.CultureInvariant);
    private static bool IsProvider(string value) => value is "official" or "bmclapi" or "mc_mirror" or "mc_mirror_west";
    private static string SafeSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Length > 2048) value = value.Substring(0, 2048);
        if (Regex.IsMatch(value, @"(?i)(?:token|ticket|secret|password|authorization|bearer|api[_-]?key|credential)"))
            return "Sensitive diagnostic content omitted.";
        string line = value.Split(new[] { '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        line = Regex.Replace(line, @"(?i)\bauthorization\b\s*[:=]\s*(?:Bearer|Basic)\s+[^\s,;]+", "authorization=[redacted]");
        line = Regex.Replace(line, @"(?i)\bBearer\s+[^\s,;]+", "Bearer [redacted]");
        line = Regex.Replace(line, @"(?i)\b(access[_-]?token|refresh[_-]?token|client[_-]?ticket|reader[_-]?token|api[_-]?key|password|secret|authorization|bearer|token)\b\s*[:=]\s*[^\s,;]+", "$1=[redacted]");
        line = Regex.Replace(line, @"(?i)https?://[^\s\]}),;]+", "[url]");
        line = Regex.Replace(line, @"\\\\[^\\\s<>\""']+\\[^\s<>\""']+", "[path]");
        line = Regex.Replace(line, @"(?i)\b[A-Z]:\\[^\s<>\""']+", "[path]");
        line = Regex.Replace(line, @"(?i)(?:/home|/users|/mnt|/private/var|/var/folders|/tmp|/opt|/srv|/workspace)/[^\s<>\""']+", "[path]");
        line = Regex.Replace(line, @"\b[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}\b", "[email]");
        line = Regex.Replace(line, @"\b(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?\b", "[address]");
        line = Regex.Replace(line, @"\s+", " ").Trim();
        return line.Length <= 160 ? line : line.Substring(0, 160);
    }
    private static void AddUnique(List<DiagnosticField> fields, DiagnosticField field)
    {
        if (!fields.Any(existing => existing.Key == field.Key)) fields.Add(field);
    }
}
