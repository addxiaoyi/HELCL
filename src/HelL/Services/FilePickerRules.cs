using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Services;

public sealed class FileTypeFilter
{
    public string Label { get; }
    public string[] Patterns { get; }

    internal FileTypeFilter(string label, string[] patterns)
    {
        Label = label;
        Patterns = patterns;
    }

    public bool Matches(string name) => Patterns.Any(pattern => pattern == "*" || pattern == "*.*" ||
        Regex.IsMatch(name, "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    public string DefaultExtension => Patterns.FirstOrDefault(pattern => pattern.StartsWith("*.", StringComparison.Ordinal)
        && pattern.IndexOf('*', 1) < 0 && pattern.IndexOf('?') < 0)?.Substring(1) ?? "";

    public override string ToString() => Label;
}

public static class FilePickerRules
{
    public static IReadOnlyList<FileTypeFilter> ParseFilters(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return new[] { new FileTypeFilter(I18n.L("所有文件"), new[] { "*" }) };
        string[] parts = filter.Split('|');
        if (parts.Length % 2 != 0) throw new ArgumentException("File filter must contain label/pattern pairs.", nameof(filter));
        var filters = new List<FileTypeFilter>();
        for (int i = 0; i < parts.Length; i += 2)
        {
            string[] patterns = parts[i + 1].Split(';').Select(pattern => pattern.Trim()).Where(pattern => pattern.Length > 0).ToArray();
            if (patterns.Length == 0) throw new ArgumentException("File filter cannot have an empty pattern.", nameof(filter));
            filters.Add(new FileTypeFilter(parts[i], patterns));
        }
        return filters;
    }

    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(I18n.L("请填写文件名称"));
        if (name == "." || name == ".." || name.EndsWith(" ", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal)
            || name.Any(character => character < 32 || "<>:\"/\\|?*".IndexOf(character) >= 0))
            throw new ArgumentException(I18n.L("名称不能包含路径或特殊字符，也不能以空格或句点结尾"));
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
            Regex.IsMatch(stem, "^(COM|LPT)[1-9¹²³]$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException(I18n.L("这个名称由 Windows 保留，请换一个名称"));
        return name;
    }

    public static string SavePath(string directory, string name, FileTypeFilter filter)
    {
        ValidateName(name);
        if (Path.GetExtension(name).Length == 0) name += filter.DefaultExtension;
        return Path.GetFullPath(Path.Combine(directory, name));
    }
}
