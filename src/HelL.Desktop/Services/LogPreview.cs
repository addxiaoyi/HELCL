using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;

namespace HelL.Desktop.Services;

internal static class LogPreview
{
    internal const int MaxBytes = 64 * 1024;
    internal static string ReadTail(string path, int lines = 120)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        file.Position = Math.Max(0, file.Length - MaxBytes);
        using var reader = new StreamReader(file, Encoding.UTF8, true);
        if (file.Position != 0) reader.ReadLine();
        string content = reader.ReadToEnd();
        return Redact(string.Join(Environment.NewLine, content.Split('\n').TakeLast(lines)));
    }

    internal static string Redact(string text)
    {
        try { return RedactContent(text); }
        catch (RegexMatchTimeoutException) { return "[无法在限定时间内过滤此内容，已省略；请手动选择较短的日志。]"; }
    }

    private static string RedactContent(string text)
    {
        var lines = text.Split('\n').Select(line => Regex.IsMatch(line,
            @"(?i)token|secret|password|passwd|authorization|bearer|api.?key|设备码|授权码|密码|密钥|令牌", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)) ? "[敏感行已隐藏]" : line);
        string clean = string.Join("\n", lines);
        clean = Regex.Replace(clean, @"(?i)https?://[^\s]+", "[地址已隐藏]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        // Unquoted paths can contain spaces, so hide the remaining line rather than expose a name or suffix.
        clean = Regex.Replace(clean, @"(?i)""(?:[A-Z]:[\\/]|\\\\)[^""\r\n]*""|'(?:[A-Z]:[\\/]|\\\\)[^'\r\n]*'|(?<![A-Z0-9])(?:[A-Z]:[\\/]|\\\\)[^\r\n]*",
            "[本机路径]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        // Multiple path components distinguish local absolute paths from ordinary /help commands.
        clean = Regex.Replace(clean, @"""/(?:[^""/\r\n]+/)+[^""\r\n]*""|'/(?:[^'/\r\n]+/)+[^'\r\n]*'|(?<![\w])/(?:[^/\r\n]+/)+[^\r\n]*",
            "[本机路径]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        clean = Regex.Replace(clean, @"\b[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@(?:[A-Z0-9-]+\.)+[A-Z]{2,}\b", "[邮箱]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        clean = Regex.Replace(clean, @"(?<![\w])\[?[0-9A-Fa-f:.]*:[0-9A-Fa-f:.]*:[0-9A-Fa-f:.]+(?:%[A-Za-z0-9_.-]+)?\]?", match =>
        {
            string candidate = match.Value.Trim('[', ']');
            string addressText = candidate.TrimEnd('.');
            return IPAddress.TryParse(addressText, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetworkV6
                ? "[网络地址]" + candidate[addressText.Length..] : match.Value;
        }, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        clean = Regex.Replace(clean, @"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?!\w|\.\d)", match =>
            IPAddress.TryParse(match.Value, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetwork ? "[网络地址]" : match.Value,
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return clean;
    }
}
