using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Services;

public enum CrashIssueType
{
    Unknown,
    OutOfMemory,
    ModConflictOrError,
    GraphicsDriver,
    JavaVersionMismatch,
    MixinFailure,
    DuplicateMods,
    MissingDependency,
    ModIncompatibility
}

public sealed class CrashDiagnosisResult
{
    public bool IsIdentified { get; set; }
    public CrashIssueType IssueType { get; set; } = CrashIssueType.Unknown;
    public string Reason { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string SuspectedMod { get; set; } = "";
}

public static class PrivacyMasker
{
    private static readonly Regex UserPathRegex = new Regex(@"([a-zA-Z]:\\Users\\)[^\\]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PrivateIpRegex = new Regex(@"\b(192\.168|10\.\d{1,3}|172\.(?:1[6-9]|2\d|3[0-1]))\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);

    public static string Mask(string input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        string masked = UserPathRegex.Replace(input, "$1***");
        masked = PrivateIpRegex.Replace(masked, match =>
        {
            string prefix = match.Groups[1].Value;
            return prefix + ".*.*";
        });
        return masked;
    }
}

public static class CrashLogExpert
{
    private static readonly Regex ModFileRegex = new Regex(@"(?:Mod File:.*?|/mods/|\\mods\\)([a-zA-Z0-9_\-\.\+]+\.jar)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MixinConfigRegex = new Regex(@"\[([a-zA-Z0-9_\-\.\+]+\.json):.*?\]", RegexOptions.Compiled);
    private static readonly Regex MixinTargetRegex = new Regex(@"in\s+([a-zA-Z0-9_\.\$]+)", RegexOptions.Compiled);
    private static readonly Regex DuplicateModRegex = new Regex(@"Duplicate (?:versions for )?mod ID '([^']+)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NoClassDefRegex = new Regex(@"(?:NoClassDefFoundError|ClassNotFoundException):\s*([a-zA-Z0-9_\/\.\$]+)", RegexOptions.Compiled);

    public static CrashDiagnosisResult Analyze(string logOrDetail, int? exitCode)
    {
        var result = new CrashDiagnosisResult();
        string text = logOrDetail ?? "";

        // 1. 内存不足判定
        if (text.Contains("OutOfMemoryError") || text.Contains("Java heap space") || text.Contains("Failed to allocate"))
        {
            result.IsIdentified = true;
            result.IssueType = CrashIssueType.OutOfMemory;
            result.Reason = "由于 Minecraft 内存不足导致游戏崩溃。";
            result.Recommendation = "建议在启动设置或版本独立配置中调大分配内存（例如 4096 MB 或更高）。";
            return result;
        }

        // 2. 显卡驱动判定
        if (text.Contains("nvoglv64.dll") || text.Contains("atio6axx.dll") || text.Contains("ig9ic64.dll") ||
            text.Contains("ig7ic64.dll") || text.Contains("ig4ic64.dll") ||
            ((exitCode == -1073741819 || exitCode == unchecked((int)0xC0000005)) && text.Contains("Problematic frame")))
        {
            result.IsIdentified = true;
            result.IssueType = CrashIssueType.GraphicsDriver;
            result.Reason = "检测到显卡驱动冲突或图形渲染库崩溃。";
            result.Recommendation = "建议更新或重装独立显卡驱动程序，并暂时禁用可能冲突的光影包。";
            return result;
        }

        // 3. Java 版本不匹配判定
        if (text.Contains("UnsupportedClassVersionError"))
        {
            result.IsIdentified = true;
            result.IssueType = CrashIssueType.JavaVersionMismatch;
            result.Reason = "Java 运行环境版本与该 Minecraft 版本或模组所需版本不兼容。";
            result.Recommendation = "请检查 Java 设置，确保 1.17+ 使用 Java 17，1.20.5+ 使用 Java 21。";
            return result;
        }

        // 4. 重复安装模组判定 (DuplicateMods)
        if (text.Contains("Duplicate versions for mod ID") || text.Contains("DuplicateModsFoundException"))
        {
            var dupMatch = DuplicateModRegex.Match(text);
            string modId = dupMatch.Success ? dupMatch.Groups[1].Value : "未知";
            result.IsIdentified = true;
            result.IssueType = CrashIssueType.DuplicateMods;
            result.SuspectedMod = modId;
            result.Reason = $"检测到重复安装的模组 ID：'{modId}'。";
            result.Recommendation = $"请在 mods 目录下查找并删除重复或较旧版本的 '{modId}' 模组文件。";
            return result;
        }

        // 5. 互斥冲突判定 (ModIncompatibility)
        if (text.Contains("Incompatible mods found") || text.Contains("is incompatible with"))
        {
            result.IsIdentified = true;
            result.IssueType = CrashIssueType.ModIncompatibility;
            result.Reason = "检测到已安装的模组之间存在严重互斥或冲突（例如 OptiFine 与 Sodium）。";
            result.Recommendation = "请根据冲突提示删除其中一个不兼容的模组。";
            return result;
        }

        // 6. 明确定位到的具体 Mod 文件异常 (ModConflictOrError)
        var modMatch = ModFileRegex.Match(text);
        if (modMatch.Success && modMatch.Groups.Count > 1)
        {
            result.IsIdentified = true;
            result.IssueType = CrashIssueType.ModConflictOrError;
            result.SuspectedMod = modMatch.Groups[1].Value.Trim();
            result.Reason = $"模组引发严重异常，可疑文件：{result.SuspectedMod}";
            result.Recommendation = $"建议暂时禁用或删除 mods 文件夹下的 {result.SuspectedMod}，或更新该模组至兼容版本。";
            return result;
        }

        // 7. Mixin 注入错误判定 (MixinFailure)
        if (text.Contains("MixinTransformerError") || text.Contains("Critical injection failure") || text.Contains("InjectionError"))
        {
            string targetClass = "未知类";
            var targetMatch = MixinTargetRegex.Match(text);
            if (targetMatch.Success)
            {
                string fullClass = targetMatch.Groups[1].Value.TrimEnd('.');
                targetClass = fullClass.Split('.').LastOrDefault() ?? fullClass;
            }

            string culpritMod = "";
            var configMatch = MixinConfigRegex.Match(text);
            if (configMatch.Success)
            {
                string jsonFile = configMatch.Groups[1].Value;
                culpritMod = jsonFile.Split('.')[0];
            }

            result.IsIdentified = true;
            result.IssueType = CrashIssueType.MixinFailure;
            result.SuspectedMod = culpritMod;
            result.Reason = $"Mixin 字节码注入失败（注入目标类：{targetClass}，可疑来源模组：{culpritMod}）。";
            result.Recommendation = "通常由于模组与游戏版本不匹配或模组间字节码冲突引起，建议升级或排除可疑模组。";
            return result;
        }

        // 8. 缺少前置或依赖类缺失 (MissingDependency)
        if (text.Contains("NoClassDefFoundError") || text.Contains("ClassNotFoundException"))
        {
            var noClassMatch = NoClassDefRegex.Match(text);
            string missingClass = noClassMatch.Success ? noClassMatch.Groups[1].Value.Replace('/', '.') : "";
            string shortName = missingClass.Split('.').LastOrDefault() ?? missingClass;

            result.IsIdentified = true;
            result.IssueType = CrashIssueType.MissingDependency;
            result.Reason = $"运行时缺少关键类依赖：{shortName} ({missingClass})";
            result.Recommendation = "通常由于缺少必要的前置模组（例如 Fabric API、Cloth Config 等），请下载安装对应前置。";
            return result;
        }

        return result;
    }
}

public sealed class EtaResult
{
    public int RemainingSeconds { get; set; }
    public string FormattedRemaining { get; set; } = "";
}

public static class TaskProgressHelper
{
    public static EtaResult CalculateEta(long currentBytes, long totalBytes, double bytesPerSecond)
    {
        if (totalBytes <= 0 || currentBytes >= totalBytes)
        {
            return new EtaResult { RemainingSeconds = 0, FormattedRemaining = "完成" };
        }
        if (bytesPerSecond <= 0)
        {
            return new EtaResult { RemainingSeconds = -1, FormattedRemaining = "计算中..." };
        }

        long remainingBytes = totalBytes - currentBytes;
        int seconds = (int)Math.Ceiling(remainingBytes / bytesPerSecond);
        string formatted;
        if (seconds < 60)
        {
            formatted = $"{seconds}秒";
        }
        else if (seconds < 3600)
        {
            int m = seconds / 60;
            int s = seconds % 60;
            formatted = s > 0 ? $"{m}分{s}秒" : $"{m}分钟";
        }
        else
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            formatted = $"{h}小时{m}分";
        }

        return new EtaResult { RemainingSeconds = seconds, FormattedRemaining = formatted };
    }

    public static string FormatStatus(long currentBytes, long totalBytes, double bytesPerSecond)
    {
        double curMb = currentBytes / (1024.0 * 1024.0);
        double totMb = totalBytes / (1024.0 * 1024.0);
        double spdMb = bytesPerSecond / (1024.0 * 1024.0);

        var eta = CalculateEta(currentBytes, totalBytes, bytesPerSecond);
        string etaText = eta.RemainingSeconds >= 0 ? $" · 剩余 {eta.FormattedRemaining}" : "";
        return $"{curMb:F1} MB / {totMb:F1} MB · {spdMb:F1} MB/s{etaText}";
    }
}

public sealed class PlaytimeDisplayResult
{
    public double ValueHours { get; set; }
    public string Unit { get; set; } = "小时";
    public string Subtext { get; set; } = "";
}

public static class PlaytimeFormatter
{
    public static PlaytimeDisplayResult Format(long totalSeconds)
    {
        double hours = Math.Round((double)totalSeconds / 3600.0, 1);
        string sub;
        if (totalSeconds < 60)
        {
            sub = "0分钟";
        }
        else if (totalSeconds < 3600)
        {
            long mins = totalSeconds / 60;
            sub = $"{mins}分钟";
        }
        else
        {
            long h = totalSeconds / 3600;
            long m = (totalSeconds % 3600) / 60;
            sub = m > 0 ? $"{h}小时 {m}分钟" : $"{h}小时";
        }

        return new PlaytimeDisplayResult
        {
            ValueHours = hours,
            Unit = "小时",
            Subtext = sub
        };
    }
}

public static class CrashPromptFormatter
{
    public static string BuildCrashPrompt(
        string instance, string version, int? exitCode, string exitHint,
        string headline, string summary, string detail, string directFile, string iface)
    {
        var list = new List<string> { "【启动器注入 · 对用户不可见】本次对话的崩溃上下文：" };
        var list2 = new List<string>();
        if (!string.IsNullOrWhiteSpace(instance)) list2.Add($"实例：{instance}");
        if (!string.IsNullOrWhiteSpace(version)) list2.Add($"版本：{version}");
        if (exitCode.HasValue)
        {
            string arg = string.IsNullOrWhiteSpace(exitHint) ? "" : $"（{exitHint}）";
            list2.Add($"退出码：{exitCode.Value}{arg}");
        }
        if (list2.Count > 0) list.Add(string.Join("\n", list2));

        var sections = new (string text, string title, int maxLen)[]
        {
            (PrivacyMasker.Mask(headline), "现象", 400),
            (PrivacyMasker.Mask(summary), "摘要", 600),
            (PrivacyMasker.Mask(detail), "详细信息", 4000)
        };
        foreach (var (text, title, maxLen) in sections)
        {
            string clipped = (text ?? "").Trim();
            if (clipped.Length > maxLen) clipped = clipped.Substring(0, maxLen) + "…（已截断）";
            if (clipped.Length > 0) list.Add(title + "：\n" + clipped);
        }

        if (!string.IsNullOrWhiteSpace(directFile))
        {
            list.Add($"完整日志文件：{PrivacyMasker.Mask(directFile)}");
        }

        list.Add("接口状态：" + iface);
        return string.Join("\n\n", list);
    }
}

