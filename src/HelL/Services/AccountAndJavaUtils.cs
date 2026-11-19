using System;
using System.Text.RegularExpressions;

namespace HelL.Services;

public static class AuthlibUrlParser
{
    public static string NormalizeAuthlibUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        string text = raw.Trim();
        if (text.StartsWith("authlib-injector:yggdrasil:", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring("authlib-injector:yggdrasil:".Length).Trim();
        }
        else if (text.StartsWith("authlib-injector:", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring("authlib-injector:".Length).Trim();
        }

        return text;
    }
}

public class JavaCompatibilityResult
{
    public bool IsCompatible { get; set; }
    public bool HasWarning { get; set; }
    public string ErrorMessage { get; set; } = "";
    public string WarningMessage { get; set; } = "";
}

public static class JavaEnvDetector
{
    public static int GetRequiredJavaMajor(string mcVersion)
    {
        if (string.IsNullOrWhiteSpace(mcVersion)) return 8;

        string clean = mcVersion.Trim();
        int dash = clean.IndexOf('-');
        if (dash > 0) clean = clean.Substring(0, dash);

        if (clean.StartsWith("26.", StringComparison.Ordinal) || clean == "26")
        {
            return 25;
        }

        if (clean.StartsWith("1.21", StringComparison.Ordinal) ||
            clean == "1.20.5" || clean == "1.20.6")
        {
            return 21;
        }

        if (clean.StartsWith("1.18", StringComparison.Ordinal) ||
            clean.StartsWith("1.19", StringComparison.Ordinal) ||
            clean.StartsWith("1.20", StringComparison.Ordinal))
        {
            return 17;
        }

        if (clean.StartsWith("1.17", StringComparison.Ordinal))
        {
            return 16;
        }

        return 8;
    }

    public static JavaCompatibilityResult CheckCompatibility(string mcVersion, int javaMajor, bool is64Bit = true)
    {
        var result = new JavaCompatibilityResult { IsCompatible = true };
        int required = GetRequiredJavaMajor(mcVersion);

        if (javaMajor < required)
        {
            result.IsCompatible = false;
            result.ErrorMessage = $"Minecraft {mcVersion} 需要 Java {required} 或更高版本，当前分配的是 Java {javaMajor}。";
            return result;
        }

        if (!is64Bit)
        {
            result.HasWarning = true;
            result.WarningMessage = "检测到 32 位 Java 运行时，最大仅支持分配约 1.5GB 内存。强烈建议安装并切换为 64 位 Java。";
        }

        return result;
    }

    public static bool IsCompatible(string mcVersion, int javaMajor, bool is64Bit = true)
    {
        return CheckCompatibility(mcVersion, javaMajor, is64Bit).IsCompatible;
    }

    public static int GetRecommendedJavaMajor(string mcVersion)
    {
        return GetRequiredJavaMajor(mcVersion);
    }
}

public class SkinLayerToggles
{
    public bool Hat { get; set; } = true;
    public bool Jacket { get; set; } = true;
    public bool LeftSleeve { get; set; } = true;
    public bool RightSleeve { get; set; } = true;
    public bool LeftPants { get; set; } = true;
    public bool RightPants { get; set; } = true;
    public bool Cape { get; set; } = true;
}

public static class AccountAndJavaUtils
{
    public static (string Badge, bool IsWarning) FormatJavaArchitectureBadge(bool is64Bit)
    {
        return is64Bit ? ("64 位 (推荐)", false) : ("32 位 (内存受限)", true);
    }

    public static string GetSkinAngleLabel(bool isBack)
    {
        return isBack ? "背面视角 (点击看正面)" : "正面视角 (点击看背面)";
    }
}
