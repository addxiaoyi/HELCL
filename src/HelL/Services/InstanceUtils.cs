using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Services;

public static class InstanceUtils
{
    private static readonly HashSet<string> ReservedWindowsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly char[] InvalidChars = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

    public static bool IsValidInstanceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        if (trimmed.IndexOfAny(InvalidChars) >= 0)
        {
            return false;
        }

        if (ReservedWindowsNames.Contains(trimmed))
        {
            return false;
        }

        return true;
    }

    public static string ExtractVersionGroup(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            return "其它";
        }

        string clean = versionId.Trim();
        int dash = clean.IndexOf('-');
        if (dash > 0)
        {
            clean = clean.Substring(0, dash);
        }

        string[] parts = clean.Split('.');
        if (parts.Length >= 2)
        {
            return $"{parts[0]}.{parts[1]}";
        }

        return clean;
    }

    public static string GeneratePreRestoreBackupName(string originalSaveName)
    {
        string name = string.IsNullOrWhiteSpace(originalSaveName) ? "World" : originalSaveName.Trim();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return $"{name}_prerestore_{timestamp}.zip";
    }

    public static bool MatchesPinyinOrSubstr(string source, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        string s = source.Trim();
        string q = query.Trim();

        // 1. Direct case-insensitive substring
        if (s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // 2. English words initials (e.g. "Pure Vanilla" -> "pv")
        string initials = GetInitials(s);
        if (initials.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        // 3. Pinyin initial approximation for common Minecraft terminology
        string pyInitials = GetChinesePinyinInitials(s);
        if (pyInitials.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static string GetInitials(string text)
    {
        var sb = new StringBuilder();
        string[] words = text.Split(new char[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
        {
            if (w.Length > 0 && char.IsLetterOrDigit(w[0]))
            {
                sb.Append(w[0]);
            }
        }
        return sb.ToString();
    }

    private static string GetChinesePinyinInitials(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (c >= 0x4e00 && c <= 0x9fa5)
            {
                sb.Append(GetSinglePinyinInitial(c));
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static char GetSinglePinyinInitial(char c)
    {
        // Common Minecraft characters quick lookup
        switch (c)
        {
            case '生': return 's';
            case '电': return 'd';
            case '测': return 'c';
            case '试': return 's';
            case '红': return 'h';
            case '石': return 's';
            case '纯': return 'c';
            case '净': return 'j';
            case '机': return 'j';
            case '械': return 'x';
            case '模': return 'm';
            case '组': return 'z';
            case '整': return 'z';
            case '合': return 'h';
            case '包': return 'b';
            case '存': return 'c';
            case '档': return 'd';
            case '版': return 'b';
            case '本': return 'b';
            case '服': return 'f';
            case '务': return 'w';
            case '器': return 'q';
            case '高': return 'g';
            case '亮': return 'l';
            case '显': return 'x';
            case '示': return 's';
            case '光': return 'g';
            case '影': return 'y';
            case '资': return 'z';
            case '源': return 'y';
            case '地': return 'd';
            case '图': return 't';
            case '小': return 'x';
            default:
                // Fast GB2312 region mapping for Chinese character initial
                try
                {
                    byte[] bytes = Encoding.GetEncoding("GB2312").GetBytes(new char[] { c });
                    if (bytes.Length == 2)
                    {
                        int code = bytes[0] * 256 + bytes[1];
                        if (code >= 45217 && code <= 45252) return 'a';
                        if (code >= 45253 && code <= 45760) return 'b';
                        if (code >= 45761 && code <= 46317) return 'c';
                        if (code >= 46318 && code <= 46825) return 'd';
                        if (code >= 46826 && code <= 47009) return 'e';
                        if (code >= 47010 && code <= 47296) return 'f';
                        if (code >= 47297 && code <= 47613) return 'g';
                        if (code >= 47614 && code <= 48118) return 'h';
                        if (code >= 48119 && code <= 49061) return 'j';
                        if (code >= 49062 && code <= 49323) return 'k';
                        if (code >= 49324 && code <= 49895) return 'l';
                        if (code >= 49896 && code <= 50370) return 'm';
                        if (code >= 50371 && code <= 50613) return 'n';
                        if (code >= 50614 && code <= 50621) return 'o';
                        if (code >= 50622 && code <= 50905) return 'p';
                        if (code >= 50906 && code <= 51386) return 'q';
                        if (code >= 51387 && code <= 51445) return 'r';
                        if (code >= 51446 && code <= 52217) return 's';
                        if (code >= 52218 && code <= 52697) return 't';
                        if (code >= 52698 && code <= 52979) return 'w';
                        if (code >= 52980 && code <= 53688) return 'x';
                        if (code >= 53689 && code <= 54480) return 'y';
                        if (code >= 54481 && code <= 55289) return 'z';
                    }
                }
                catch { }
                return char.ToLowerInvariant(c);
        }
    }
}
