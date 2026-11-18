using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class DocumentCorrections
{
    internal static readonly object Gate = new();
    private sealed class Correction { public string Text { get; set; } = ""; }

    public static string? Read(string key, int index, string original)
    {
        string path = PathFor(key, index, original);
        lock (Gate)
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 256000) throw new InvalidDataException("手动译文文件过大，已保留原文件。");
            return (JsonSerializer.Deserialize<Correction>(File.ReadAllText(path)) ?? throw new InvalidDataException("手动译文文件损坏，已保留原文件。")).Text;
        }
    }

    public static string Save(string key, int index, string original, string text, string expected, bool reset = false)
    {
        string path = PathFor(key, index, original);
        if (!reset && (string.IsNullOrWhiteSpace(text) || text.Length > 12000 || text.IndexOf('\0') >= 0))
            throw new ArgumentException("请输入有效译文，单段最多 12000 字符。");
        lock (Gate)
        {
            if (CatalogDocument.CurrentTranslation(key, index, original) != expected) throw new InvalidOperationException("本段译文已改变，请重新载入后编辑。");
            if (reset)
            {
                if (File.Exists(path)) File.Delete(path);
                return CatalogDocument.CurrentTranslation(key, index, original);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string staged = path + "." + Guid.NewGuid().ToString("N") + ".new";
            try
            {
                File.WriteAllText(staged, JsonSerializer.Serialize(new Correction { Text = text }), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            return text;
        }
    }

    private static string PathFor(string key, int index, string original)
    {
        if (!Regex.IsMatch(key ?? "", @"\A[a-f0-9]{64}\z") || index < 0 || index > 1000 || original.Length > CatalogDocument.MaxCharacters)
            throw new ArgumentException("文档段落无效，请重新加载。");
        using var hash = SHA256.Create();
        string id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key + ":" + index + ":" + original))).Replace("-", "").ToLowerInvariant();
        return Path.Combine(Utils.ROOT, "resource-translations", id + ".json");
    }
}
