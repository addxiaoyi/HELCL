using System.Text;
using System.Text.RegularExpressions;

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
        var lines = text.Split('\n').Select(line => Regex.IsMatch(line,
            @"(?i)token|secret|password|passwd|authorization|bearer|api.?key|设备码|授权码|密码|密钥|令牌", RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)) ? "[敏感行已隐藏]" : line);
        string clean = string.Join("\n", lines);
        clean = Regex.Replace(clean, @"(?i)https?://[^\s]+", "[地址已隐藏]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        clean = Regex.Replace(clean, @"(?i)(?:[A-Z]:\\|/(?:home|users|var|tmp|mnt)/)[^\s]+", "[本机路径]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return clean;
    }
}
