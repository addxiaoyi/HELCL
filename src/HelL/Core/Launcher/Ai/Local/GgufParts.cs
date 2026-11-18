using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher.Ai.Local;

public static class GgufParts
{
    private static readonly Regex SplitName = new Regex(@"\A(?<base>.+)-(?<index>\d{5})-of-(?<count>\d{5})\.gguf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<string> Order(IEnumerable<string> names)
    {
        var parts = names.ToList();
        if (parts.Count == 0) throw new IOException("没有可导入的模型文件。");
        if (parts.Count == 1 && !SplitName.IsMatch(parts[0])) return parts;
        var matches = parts.Select(name => (Name: name, Match: SplitName.Match(name))).ToList();
        if (matches.Any(part => !part.Match.Success)) throw new IOException("不能将不同模型或非分片文件混合导入。");
        string prefix = matches[0].Match.Groups["base"].Value;
        int count = int.Parse(matches[0].Match.Groups["count"].Value);
        if (count < 1 || count > 1024 || count != parts.Count || matches.Any(part => part.Match.Groups["base"].Value != prefix || int.Parse(part.Match.Groups["count"].Value) != count))
            throw new IOException("GGUF 分片不完整或来自不同模型，请补齐同一组全部分片。");
        var ordered = matches.OrderBy(part => int.Parse(part.Match.Groups["index"].Value)).ToList();
        for (int i = 0; i < count; i++)
            if (int.Parse(ordered[i].Match.Groups["index"].Value) != i + 1)
                throw new IOException("GGUF 分片有缺失或重复，请检查编号。");
        return ordered.Select(part => part.Name).ToList();
    }

    public static List<string> LocalFiles(string selected)
    {
        string path = Path.GetFullPath(selected);
        var match = SplitName.Match(Path.GetFileName(path));
        if (!match.Success) return new List<string> { path };
        string folder = Path.GetDirectoryName(path)!;
        string prefix = match.Groups["base"].Value;
        var names = Directory.EnumerateFiles(folder, "*.gguf").Select(Path.GetFileName)
            .Where(name => SplitName.Match(name).Groups["base"].Value == prefix).ToList();
        return Order(names!).Select(name => Path.Combine(folder, name)).ToList();
    }

    public static List<ModelFile> Group(IEnumerable<ModelFile> files, ModelHardware hardware)
    {
        var grouped = new List<ModelFile>();
        foreach (var group in files.GroupBy(file => SplitName.IsMatch(file.Name) ? SplitName.Match(file.Name).Groups["base"].Value : file.Name, StringComparer.Ordinal))
        {
            var parts = group.ToList();
            var first = parts[0];
            if (!SplitName.IsMatch(first.Name)) { grouped.Add(first); continue; }
            string error = "";
            List<string> ordered = new List<string>();
            try { ordered = Order(parts.Select(part => part.Name)); }
            catch (IOException ex) { error = ex.Message; }
            long total = 0;
            try { foreach (var part in parts) total = checked(total + part.Size); }
            catch (OverflowException) { error = "分片总大小超出可支持范围。"; }
            bool supported = error.Length == 0 && parts.All(part => part.Supported);
            if (error.Length == 0 && !supported) error = "部分分片缺少大小或 SHA-256，无法验证。";
            var sorted = ordered.Count > 0 ? ordered.Select(name => parts.Single(part => part.Name == name)).ToList() : parts;
            grouped.Add(new ModelFile { Repository = first.Repository, Revision = first.Revision, Name = sorted[0].Name,
                Size = total, Supported = supported, Parts = sorted, Fit = ModelRules.Assess(total, hardware, true),
                Note = supported ? "完整分片组 · " + parts.Count + " 个文件 · 逐片校验后整体导入" : error });
        }
        return grouped.OrderBy(file => file.Size).ToList();
    }

    public static void ValidateHeader(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能导入链接或重解析点模型文件。");
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        if (file.Length < 24 || reader.ReadUInt32() != 0x46554747 || reader.ReadUInt32() is not (2 or 3))
            throw new IOException("该文件不是支持的 GGUF v2/v3 模型。");
    }
}
