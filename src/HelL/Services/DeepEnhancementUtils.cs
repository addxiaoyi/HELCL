using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HelL.Services;

// ==========================================
// 1. Modrinth (.mrpack) index.json 解析与元数据
// ==========================================

public sealed class ModrinthIndexFile
{
    public string Path { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public string Sha512 { get; set; } = "";
    public long FileSize { get; set; }
    public List<string> Downloads { get; set; } = new List<string>();
}

public sealed class ModrinthModpackIndex
{
    public int FormatVersion { get; set; } = 1;
    public string Game { get; set; } = "minecraft";
    public string VersionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string PrimaryLoaderType { get; set; } = "";
    public string PrimaryLoaderVersion { get; set; } = "";
    public Dictionary<string, string> Dependencies { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public List<ModrinthIndexFile> Files { get; set; } = new List<ModrinthIndexFile>();
    public bool IsValid { get; set; }
}

public static class ModrinthIndexHelper
{
    public static ModrinthModpackIndex Parse(string json)
    {
        var result = new ModrinthModpackIndex();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("formatVersion", out var fvProp))
            {
                result.FormatVersion = fvProp.GetInt32();
            }

            if (root.TryGetProperty("game", out var gameProp))
            {
                result.Game = gameProp.GetString() ?? "";
            }

            if (root.TryGetProperty("versionId", out var vidProp))
            {
                result.VersionId = vidProp.GetString() ?? "";
            }

            if (root.TryGetProperty("name", out var nameProp))
            {
                result.Name = nameProp.GetString() ?? "";
            }

            if (root.TryGetProperty("summary", out var sumProp))
            {
                result.Summary = sumProp.GetString() ?? "";
            }

            if (root.TryGetProperty("dependencies", out var depProp) && depProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in depProp.EnumerateObject())
                {
                    string depKey = prop.Name.ToLowerInvariant();
                    string depVal = prop.Value.GetString() ?? "";
                    result.Dependencies[depKey] = depVal;

                    if (depKey == "minecraft")
                    {
                        result.GameVersion = depVal;
                    }
                    else if (string.IsNullOrEmpty(result.PrimaryLoaderType))
                    {
                        result.PrimaryLoaderType = depKey;
                        result.PrimaryLoaderVersion = depVal;
                    }
                }
            }

            if (root.TryGetProperty("files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var fileEl in filesProp.EnumerateArray())
                {
                    var file = new ModrinthIndexFile();
                    if (fileEl.TryGetProperty("path", out var pProp))
                    {
                        file.Path = pProp.GetString() ?? "";
                    }
                    if (fileEl.TryGetProperty("fileSize", out var fsProp))
                    {
                        file.FileSize = fsProp.GetInt64();
                    }

                    if (fileEl.TryGetProperty("hashes", out var hashProp) && hashProp.ValueKind == JsonValueKind.Object)
                    {
                        if (hashProp.TryGetProperty("sha1", out var sha1Prop))
                        {
                            file.Sha1 = sha1Prop.GetString() ?? "";
                        }
                        if (hashProp.TryGetProperty("sha512", out var sha512Prop))
                        {
                            file.Sha512 = sha512Prop.GetString() ?? "";
                        }
                    }

                    if (fileEl.TryGetProperty("downloads", out var downProp) && downProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in downProp.EnumerateArray())
                        {
                            string url = d.GetString();
                            if (!string.IsNullOrEmpty(url))
                            {
                                file.Downloads.Add(url);
                            }
                        }
                    }

                    result.Files.Add(file);
                }
            }

            result.IsValid = !string.IsNullOrEmpty(result.Game) &&
                             (!string.IsNullOrEmpty(result.Name) || !string.IsNullOrEmpty(result.GameVersion) || result.Files.Count > 0);
        }
        catch
        {
            result.IsValid = false;
        }

        return result;
    }
}

// ==========================================
// 2. 启动前多维综合体检门禁 (Launch Preflight Pipeline)
// ==========================================

public enum PreflightSeverity
{
    Notice,
    Warning,
    Error
}

public sealed class PreflightIssue
{
    public PreflightSeverity Severity { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string Suggestion { get; set; } = "";

    public PreflightIssue() { }

    public PreflightIssue(PreflightSeverity severity, string code, string message, string suggestion)
    {
        Severity = severity;
        Code = code;
        Message = message;
        Suggestion = suggestion;
    }
}

public sealed class LaunchPreflightRequest
{
    public string MinecraftVersion { get; set; } = "";
    public string JavaPath { get; set; } = "";
    public int JavaMajorVersion { get; set; }
    public bool IsJava64Bit { get; set; } = true;
    public int AllocatedMemoryMb { get; set; }
    public int SystemAvailableMemoryMb { get; set; }
    public string InstanceDirectory { get; set; } = "";
    public List<string> InstalledMods { get; set; } = new List<string>();
}

public sealed class PreflightEvaluationReport
{
    public bool CanProceed => !HasErrors;
    public bool HasErrors => Issues.Any(i => i.Severity == PreflightSeverity.Error);
    public bool HasWarnings => Issues.Any(i => i.Severity == PreflightSeverity.Warning);
    public List<PreflightIssue> Issues { get; } = new List<PreflightIssue>();
}

public static class LaunchPreflightGate
{
    public static PreflightEvaluationReport Evaluate(LaunchPreflightRequest request)
    {
        var report = new PreflightEvaluationReport();
        if (request == null) return report;

        // 1. 32 位 Java 与内存分配超限判定
        if (!request.IsJava64Bit && request.AllocatedMemoryMb > 1536)
        {
            report.Issues.Add(new PreflightIssue(
                PreflightSeverity.Error,
                "JAVA_32BIT_MEM_LIMIT",
                $"当前使用的是 32 位 Java，但分配了 {request.AllocatedMemoryMb}MB 内存（上限为 1536MB），启动将必定崩溃。",
                "请在设置或版本配置中切换为 64 位 Java，或将内存降低至 1024MB。"));
        }

        // 2. Minecraft 与 Java 版本兼容性判定
        bool isCompatible = JavaEnvDetector.IsCompatible(request.MinecraftVersion, request.JavaMajorVersion, request.IsJava64Bit);
        if (request.JavaMajorVersion > 0 && !isCompatible)
        {
            string recJava = JavaEnvDetector.GetRecommendedJavaMajor(request.MinecraftVersion).ToString();
            report.Issues.Add(new PreflightIssue(
                PreflightSeverity.Error,
                "JAVA_VERSION_MISMATCH",
                $"当前 Minecraft {request.MinecraftVersion} 与选定的 Java {request.JavaMajorVersion} 不兼容，必须使用 Java {recJava} 或更高。",
                $"建议在 Java 设置中安装或选择 Java {recJava}。"));
        }

        // 3. 空闲物理内存预警判定
        if (request.SystemAvailableMemoryMb > 0 && request.AllocatedMemoryMb > (request.SystemAvailableMemoryMb * 0.85))
        {
            report.Issues.Add(new PreflightIssue(
                PreflightSeverity.Warning,
                "MEM_EXCEEDS_AVAILABLE",
                $"分配的内存 ({request.AllocatedMemoryMb}MB) 接近或超过系统当前空闲物理内存 ({request.SystemAvailableMemoryMb}MB)，可能导致系统换页卡死。",
                "建议调小 JVM 分配内存，或关闭系统内其他消耗内存的后台程序。"));
        }

        return report;
    }
}

// ==========================================
// 3. 分块下载色块调度矩阵 (Download Block Matrix)
// ==========================================

public enum DownloadBlockState
{
    Pending,
    Downloading,
    Verifying,
    Completed,
    Failed
}

public sealed class DownloadBlock
{
    public int Index { get; }
    public long StartOffset { get; }
    public long EndOffset { get; }
    public DownloadBlockState State { get; set; } = DownloadBlockState.Pending;
    public long DownloadedBytes { get; set; }
    public int RetryCount { get; set; }

    public DownloadBlock(int index, long startOffset, long endOffset)
    {
        Index = index;
        StartOffset = startOffset;
        EndOffset = endOffset;
    }
}

public sealed class DownloadBlockMatrix
{
    private readonly List<DownloadBlock> _blocks = new List<DownloadBlock>();
    private readonly object _lock = new object();

    public long TotalBytes { get; }
    public long BlockSize { get; }

    public int TotalBlocks => _blocks.Count;

    public int CompletedBlocks
    {
        get
        {
            lock (_lock)
            {
                return _blocks.Count(b => b.State == DownloadBlockState.Completed);
            }
        }
    }

    public double ProgressRatio
    {
        get
        {
            lock (_lock)
            {
                if (_blocks.Count == 0) return 1.0;
                return (double)CompletedBlocks / _blocks.Count;
            }
        }
    }

    public DownloadBlock this[int index]
    {
        get
        {
            lock (_lock)
            {
                return _blocks[index];
            }
        }
    }

    public DownloadBlockMatrix(long totalBytes, long blockSize)
    {
        TotalBytes = Math.Max(0, totalBytes);
        BlockSize = Math.Max(1024, blockSize);

        if (TotalBytes > 0)
        {
            long count = (long)Math.Ceiling((double)TotalBytes / BlockSize);
            for (int i = 0; i < count; i++)
            {
                long start = i * BlockSize;
                long end = Math.Min(TotalBytes - 1, (i + 1) * BlockSize - 1);
                _blocks.Add(new DownloadBlock(i, start, end));
            }
        }
    }

    public int AcquireNextPendingBlock()
    {
        lock (_lock)
        {
            var block = _blocks.FirstOrDefault(b => b.State == DownloadBlockState.Pending);
            if (block != null)
            {
                block.State = DownloadBlockState.Downloading;
                return block.Index;
            }
            return -1;
        }
    }

    public void MarkBlockCompleted(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _blocks.Count)
            {
                _blocks[index].State = DownloadBlockState.Completed;
                _blocks[index].DownloadedBytes = _blocks[index].EndOffset - _blocks[index].StartOffset + 1;
            }
        }
    }

    public void MarkBlockFailed(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _blocks.Count)
            {
                _blocks[index].State = DownloadBlockState.Pending;
                _blocks[index].RetryCount++;
            }
        }
    }
}
