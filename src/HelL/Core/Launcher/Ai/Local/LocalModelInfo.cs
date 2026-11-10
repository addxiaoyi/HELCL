using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class LocalModelInfo
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Parameters { get; set; } = "";
    public string Quantization { get; set; } = "";
    public bool Running { get; set; }
    public string Path { get; set; } = "";
}

public sealed class ModelHardware
{
    public long TotalMemory { get; set; }
    public long AvailableMemory { get; set; }
    public long FreeDisk { get; set; }
    public long VideoMemory { get; set; }
    public string Gpu { get; set; } = "未检测到独立显卡";
    public string DiskPath { get; set; } = "";
    public string Note { get; set; } = "显存仅供参考；推荐按可用系统内存估算，长上下文会额外占用内存。";
}

public sealed class ModelFit
{
    public string Level { get; set; } = "unknown";
    public string Label { get; set; } = "大小未知";
    public string Reason { get; set; } = "请先查询模型文件大小。";
    public long EstimatedMemory { get; set; }
    public bool CanDownload { get; set; } = true;
}

public sealed class LibraryModel
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Parameters { get; set; } = "";
    public long Size { get; set; }
    public ModelFit Fit { get; set; } = new ModelFit();
}

public sealed class ModelFile
{
    public string Repository { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Name { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public bool Supported { get; set; }
    public string Note { get; set; } = "";
    public ModelFit Fit { get; set; } = new ModelFit();
    public List<ModelFile> Parts { get; set; } = new List<ModelFile>();
}

public sealed class ModelJob
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "idle";
    public string Message { get; set; } = "";
    public long Completed { get; set; }
    public long Total { get; set; }
    public string Kind { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string Source { get; set; } = "";
    public List<ModelFile> Files { get; set; } = new List<ModelFile>();
    public string CreatedAt { get; set; } = "";
    public bool Durable { get; set; }
    public ModelJob Copy() => (ModelJob)MemberwiseClone();
}

public sealed class LocalModelStatus
{
    public string Endpoint { get; set; } = "";
    public string DiscoveredEndpoint { get; set; } = "";
    public string RuntimePath { get; set; } = "";
    public string Version { get; set; } = "";
    public string Error { get; set; } = "";
    public bool Online { get; set; }
    public bool Managed { get; set; }
    public ModelHardware Hardware { get; set; } = new ModelHardware();
    public List<LocalModelInfo> Models { get; set; } = new List<LocalModelInfo>();
    public ModelJob Job { get; set; } = new ModelJob();
}

public static class ModelRules
{
    public const long GiB = 1024L * 1024 * 1024;
    public const string DefaultEndpoint = "http://127.0.0.1:11434";
    public const string ManagedEndpoint = "http://127.0.0.1:11435";

    public static string Endpoint(string address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) ||
            !uri.IsLoopback || (uri.Scheme != "http" && uri.Scheme != "https") ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("本地服务地址须为本机 HTTP 地址，例如 http://127.0.0.1:11434。不要附加 /v1。");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static string ModelName(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > 180 || !Regex.IsMatch(name, @"\A[a-zA-Z0-9][a-zA-Z0-9._:/-]*\z") || name.Contains("..") || name.Contains("://"))
            throw new ArgumentException("模型名称无效，请使用 qwen3:4b 这样的仓库名称。");
        if (name.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("此模型在云端运行，请选择可下载的本地模型标签。");
        return name;
    }

    public static bool CanDownload(bool runtimeOnline, bool supported, ModelFit fit) => runtimeOnline && supported && fit != null && fit.CanDownload;

    public static ModelFit Assess(long size, ModelHardware hardware, bool import = false)
    {
        if (size <= 0) return new ModelFit();
        if (size > (long.MaxValue - GiB) / (import ? 2 : 1) || size > long.MaxValue - size / 3 - GiB)
            return new ModelFit { Level = "unknown", Label = "模型大小超出可估算范围", Reason = "模型文件过大，无法安全估算内存与磁盘需求。", CanDownload = false };
        long memory = size + size / 3 + GiB;
        long disk = size * (import ? 2 : 1) + GiB;
        if (hardware.FreeDisk > 0 && disk > hardware.FreeDisk)
            return new ModelFit { Level = "disk", Label = "磁盘空间不足", Reason = "下载与导入需要约 " + FormatBytes(disk) + " 空间。", EstimatedMemory = memory, CanDownload = false };
        if (hardware.AvailableMemory <= 0)
            return new ModelFit { EstimatedMemory = memory, Reason = "未读取到可用内存；预计至少需要 " + FormatBytes(memory) + "。" };
        if (memory <= Math.Max(0, hardware.AvailableMemory - 2 * GiB))
            return new ModelFit { Level = "recommended", Label = "适合这台电脑", Reason = "预计占用 " + FormatBytes(memory) + " 内存，并为系统预留至少 2 GB。", EstimatedMemory = memory };
        return new ModelFit { Level = "large", Label = "当前内存偏紧", Reason = "预计需要 " + FormatBytes(memory) + "，当前可用 " + FormatBytes(hardware.AvailableMemory) + "；可以下载，运行可能很慢或失败。", EstimatedMemory = memory };
    }

    public static string FormatBytes(long size) => size >= GiB ? (size / (double)GiB).ToString("0.0") + " GB"
        : size >= 1048576 ? (size / 1048576d).ToString("0") + " MB"
        : size >= 1024 ? (size / 1024d).ToString("0.0") + " KB" : Math.Max(0, size) + " B";

    public static string ProgressText(string status)
    {
        if (I18nCore.CurrentLanguage() == "en") return status;
        if (status.StartsWith("pulling manifest", StringComparison.OrdinalIgnoreCase)) return "正在获取模型信息";
        if (status.StartsWith("pulling ", StringComparison.OrdinalIgnoreCase)) return "正在下载模型文件";
        if (status.StartsWith("verifying sha256", StringComparison.OrdinalIgnoreCase)) return "正在校验模型文件";
        if (status.StartsWith("using existing layer", StringComparison.OrdinalIgnoreCase)) return "复用已下载的模型文件";
        if (status.StartsWith("parsing GGUF", StringComparison.OrdinalIgnoreCase)) return "正在读取 GGUF 模型";
        if (status.StartsWith("writing manifest", StringComparison.OrdinalIgnoreCase)) return "正在登记模型";
        if (status.StartsWith("removing unused layers", StringComparison.OrdinalIgnoreCase)) return "正在整理模型文件";
        if (status == "success") return "已完成";
        return status;
    }

    public static List<LibraryModel> Catalog(ModelHardware hardware)
    {
        // Approximate Q4 downloads; Ollama reports the exact layer sizes during a pull.
        var entries = new[] {
            ("qwen3:0.6b", "0.6B", 0.523, "入门尝试 · 下载小，适合基础对话"),
            ("qwen3:1.7b", "1.7B", 1.4, "轻量中文对话 · 低内存设备"),
            ("qwen3:4b", "4B", 2.5, "日常问答 · 中文与代码"),
            ("qwen3:8b", "8B", 5.2, "通用助手 · 更完整的回答"),
            ("qwen3:14b", "14B", 9.3, "复杂问答 · 需要更多内存"),
            ("qwen3:30b", "30B MoE", 19.0, "混合专家 · 所有权重仍需存储"),
            ("qwen3:32b", "32B", 20.0, "大模型 · 适合高内存工作站"),
            ("llama3.3:70b", "70B", 43.0, "大型通用模型 · 高内存与显存需求"),
            ("qwen3:235b", "235B MoE", 142.0, "超大模型 · 多卡或大内存工作站")
        };
        return entries.Select(entry => {
            long bytes = (long)(entry.Item3 * 1000000000);
            return new LibraryModel { Name = entry.Item1, Parameters = entry.Item2, Size = bytes,
                Description = entry.Item4, Fit = Assess(bytes, hardware) };
        }).ToList();
    }
}
