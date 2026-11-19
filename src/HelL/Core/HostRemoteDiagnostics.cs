using System;
using System.Text.RegularExpressions;

namespace HelL.Core;

internal static class HostRemoteDiagnostics
{
    private static readonly TimeSpan MatchBudget = TimeSpan.FromMilliseconds(50);
    private static readonly Regex SensitiveLine = new Regex(@"authorization|bearer|token|ticket|password|passwd|secret|api.?key|device.?code|login.?code|user.?code|access.?code|验证码|设备码|授权码|登录码|密钥|令牌|密码", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchBudget);
    private static readonly Regex Url = new Regex(@"https?://[^\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchBudget);
    private static readonly Regex WindowsPath = new Regex(@"(?<![A-Za-z0-9])[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*", RegexOptions.CultureInvariant, MatchBudget);
    private static readonly Regex UncPath = new Regex(@"\\\\[^\s]+", RegexOptions.CultureInvariant, MatchBudget);
    private static readonly Regex UnixPath = new Regex(@"(?<![A-Za-z0-9])/(?:[^\s'""<>]+)", RegexOptions.CultureInvariant, MatchBudget);
    private static readonly Regex Email = new Regex(@"\b[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}\b", RegexOptions.CultureInvariant, MatchBudget);

    public static string Clean(string? value, int limit = 512)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Length > 16384) value = value.Substring(0, 16384);
        try
        {
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
            // Authentication output is omitted as a whole, including unfamiliar value formats.
            if (SensitiveLine.IsMatch(clean)) return "[REDACTED]";
            clean = Url.Replace(clean, "[URL]");
            clean = WindowsPath.Replace(clean, "[LOCAL PATH]");
            clean = UncPath.Replace(clean, "[LOCAL PATH]");
            clean = UnixPath.Replace(clean, "[LOCAL PATH]");
            clean = Email.Replace(clean, "[EMAIL]");
            return clean.Length <= limit ? clean : clean.Substring(0, limit);
        }
        catch (RegexMatchTimeoutException) { return "[REDACTED]"; }
    }
}
