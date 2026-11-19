using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace HelL.Services;

public sealed class CurseForgeModpackFile
{
    public long ProjectId { get; set; }
    public long FileId { get; set; }
    public bool Required { get; set; } = true;

    public CurseForgeModpackFile() { }

    public CurseForgeModpackFile(long projectId, long fileId, bool required = true)
    {
        ProjectId = projectId;
        FileId = fileId;
        Required = required;
    }
}

public sealed class CurseForgeModpackManifest
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string PrimaryLoaderType { get; set; } = "";
    public string PrimaryLoaderVersion { get; set; } = "";
    public List<CurseForgeModpackFile> Files { get; set; } = new List<CurseForgeModpackFile>();
    public string Overrides { get; set; } = "overrides";
    public bool IsValid { get; set; }
}

public static class CurseForgeManifestHelper
{
    public static CurseForgeModpackManifest Parse(string json)
    {
        var manifest = new CurseForgeModpackManifest();
        if (string.IsNullOrWhiteSpace(json))
        {
            return manifest;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("name", out var nameProp))
            {
                manifest.Name = nameProp.GetString() ?? "";
            }
            if (root.TryGetProperty("version", out var verProp))
            {
                manifest.Version = verProp.GetString() ?? "";
            }
            if (root.TryGetProperty("author", out var authProp))
            {
                manifest.Author = authProp.GetString() ?? "";
            }
            if (root.TryGetProperty("overrides", out var ovProp))
            {
                manifest.Overrides = ovProp.GetString() ?? "overrides";
            }

            if (root.TryGetProperty("minecraft", out var mcProp))
            {
                if (mcProp.TryGetProperty("version", out var mcVerProp))
                {
                    manifest.GameVersion = mcVerProp.GetString() ?? "";
                }

                if (mcProp.TryGetProperty("modLoaders", out var loadersProp) && loadersProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var loaderEl in loadersProp.EnumerateArray())
                    {
                        if (loaderEl.TryGetProperty("id", out var idProp))
                        {
                            string rawId = idProp.GetString() ?? "";
                            int dash = rawId.IndexOf('-');
                            if (dash > 0)
                            {
                                manifest.PrimaryLoaderType = rawId.Substring(0, dash).ToLowerInvariant();
                                manifest.PrimaryLoaderVersion = rawId.Substring(dash + 1);
                            }
                            else
                            {
                                manifest.PrimaryLoaderType = rawId.ToLowerInvariant();
                            }
                            break;
                        }
                    }
                }
            }

            if (root.TryGetProperty("files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var fEl in filesProp.EnumerateArray())
                {
                    long pId = 0;
                    long fId = 0;
                    bool req = true;

                    if (fEl.TryGetProperty("projectID", out var pProp) && pProp.TryGetInt64(out var pVal))
                    {
                        pId = pVal;
                    }
                    if (fEl.TryGetProperty("fileID", out var fIdProp) && fIdProp.TryGetInt64(out var fVal))
                    {
                        fId = fVal;
                    }
                    if (fEl.TryGetProperty("required", out var reqProp))
                    {
                        req = reqProp.GetBoolean();
                    }

                    if (pId > 0 && fId > 0)
                    {
                        manifest.Files.Add(new CurseForgeModpackFile(pId, fId, req));
                    }
                }
            }

            manifest.IsValid = !string.IsNullOrWhiteSpace(manifest.GameVersion);
        }
        catch
        {
            manifest.IsValid = false;
        }

        return manifest;
    }

    public static string Generate(CurseForgeModpackManifest manifest)
    {
        if (manifest == null) return "{}";

        string loaderId = !string.IsNullOrWhiteSpace(manifest.PrimaryLoaderType)
            ? $"{manifest.PrimaryLoaderType}-{manifest.PrimaryLoaderVersion}"
            : "forge-latest";

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WriteStartObject("minecraft");
        writer.WriteString("version", manifest.GameVersion ?? "");
        writer.WriteStartArray("modLoaders");
        writer.WriteStartObject();
        writer.WriteString("id", loaderId);
        writer.WriteBoolean("primary", true);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteString("manifestType", "minecraftModpack");
        writer.WriteNumber("manifestVersion", 1);
        writer.WriteString("name", manifest.Name ?? "");
        writer.WriteString("version", manifest.Version ?? "1.0.0");
        writer.WriteString("author", manifest.Author ?? "");

        writer.WriteStartArray("files");
        foreach (var file in manifest.Files ?? Enumerable.Empty<CurseForgeModpackFile>())
        {
            writer.WriteStartObject();
            writer.WriteNumber("projectID", file.ProjectId);
            writer.WriteNumber("fileID", file.FileId);
            writer.WriteBoolean("required", file.Required);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteString("overrides", string.IsNullOrWhiteSpace(manifest.Overrides) ? "overrides" : manifest.Overrides);

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

public sealed class LanDiscoveryInfo
{
    public string RawMotd { get; set; } = "";
    public string CleanMotd { get; set; } = "";
    public int Port { get; set; }
    public bool IsValid { get; set; }
}

public static class LanDiscoveryParser
{
    private static readonly Regex MotdPattern = new Regex(@"\[MOTD\](.*?)\[/MOTD\]", RegexOptions.Compiled);
    private static readonly Regex AdPattern = new Regex(@"\[AD\](\d+)\[/AD\]", RegexOptions.Compiled);

    public static LanDiscoveryInfo Parse(string rawPacket)
    {
        var info = new LanDiscoveryInfo();
        if (string.IsNullOrWhiteSpace(rawPacket))
        {
            return info;
        }

        var motdMatch = MotdPattern.Match(rawPacket);
        var adMatch = AdPattern.Match(rawPacket);

        if (!motdMatch.Success || !adMatch.Success)
        {
            return info;
        }

        if (!int.TryParse(adMatch.Groups[1].Value, out int port) || port < 1 || port > 65535)
        {
            return info;
        }

        info.RawMotd = motdMatch.Groups[1].Value;
        info.CleanMotd = MinecraftColorParser.StripColorCodes(info.RawMotd);
        info.Port = port;
        info.IsValid = true;

        return info;
    }
}

public enum SettingsSaveStatus
{
    Clean,
    Pending,
    Saving,
    Error
}

public sealed class SettingsAutoSaveController : IDisposable
{
    private readonly Action _onSaveAction;
    private readonly int _debounceDelayMs;
    private Timer _timer;
    private readonly object _lock = new object();

    public SettingsSaveStatus Status { get; private set; } = SettingsSaveStatus.Clean;
    public bool IsDirty => Status == SettingsSaveStatus.Pending;

    public SettingsAutoSaveController(Action onSaveAction, int debounceDelayMs = 300)
    {
        _onSaveAction = onSaveAction ?? throw new ArgumentNullException(nameof(onSaveAction));
        _debounceDelayMs = Math.Max(10, debounceDelayMs);
    }

    public void MarkDirty()
    {
        lock (_lock)
        {
            Status = SettingsSaveStatus.Pending;

            if (_timer == null)
            {
                _timer = new Timer(OnTimerTick, null, _debounceDelayMs, Timeout.Infinite);
            }
            else
            {
                _timer.Change(_debounceDelayMs, Timeout.Infinite);
            }
        }
    }

    private void OnTimerTick(object state)
    {
        FlushSave();
    }

    public void FlushSave()
    {
        lock (_lock)
        {
            if (Status != SettingsSaveStatus.Pending)
            {
                return;
            }

            Status = SettingsSaveStatus.Saving;
            _timer?.Dispose();
            _timer = null;
        }

        try
        {
            _onSaveAction();
            lock (_lock)
            {
                Status = SettingsSaveStatus.Clean;
            }
        }
        catch
        {
            lock (_lock)
            {
                Status = SettingsSaveStatus.Error;
            }
            throw;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}

public static class SafeFileOperationHelper
{
    public static T ExecuteWithRetry<T>(Func<T> operation, int maxRetries = 3, int initialDelayMs = 50)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));

        int attempt = 0;
        while (true)
        {
            try
            {
                attempt++;
                return operation();
            }
            catch (Exception ex) when (attempt <= maxRetries && (ex is IOException || ex is UnauthorizedAccessException))
            {
                int delay = initialDelayMs * (int)Math.Pow(2, attempt - 1);
                Thread.Sleep(delay);
            }
        }
    }

    public static void AtomicWrite(string targetPath, Action<Stream> writeAction)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentNullException(nameof(targetPath));
        if (writeAction == null) throw new ArgumentNullException(nameof(writeAction));

        string dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempFile = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writeAction(stream);
                stream.Flush(true);
            }

            ExecuteWithRetry(() =>
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tempFile, targetPath);
                return true;
            }, maxRetries: 3, initialDelayMs: 50);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // 临时文件清理失败不影响主流程
            }
        }
    }
}
