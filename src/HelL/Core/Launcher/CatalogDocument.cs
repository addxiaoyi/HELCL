using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HelL.Core.Launcher;

public static class CatalogDocument
{
    public const int MaxCharacters = 160000;
    private const int ChunkCharacters = 700;
    private static readonly Regex Protected = new(@"```[\s\S]*?```|`[^`\r\n]+`|!?\[[^\]\r\n]*\]\(https?://[^\s<>]+?\)|https?://[^\s<>\]\)]+|\*{1,3}|(?m:^#{1,6} +)|\n+", RegexOptions.Compiled);

    public static string Readable(string text, bool html)
    {
        if (text.Length > MaxCharacters) throw new InvalidDataException("介绍或更新日志过长，请在项目主页查看全文。");
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        if (!html) return Regex.Replace(text, @"</?(?:details|summary)\b[^>]*>", "", RegexOptions.IgnoreCase).Trim();
        text = Regex.Replace(text, @"<(script|style|iframe)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<pre\b[^>]*>([\s\S]*?)</pre>", match => "\n\n```\n" + Regex.Replace(match.Groups[1].Value, "<[^>]*>", "") + "\n```\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<a\\b[^>]*href=[\"'](https?://[^\"']+)[\"'][^>]*>([\\s\\S]*?)</a>",
            match => "[" + Regex.Replace(match.Groups[2].Value, "<[^>]*>", "") + "](" + match.Groups[1].Value + ")", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<h([1-6])\b[^>]*>", match => "\n\n" + new string('#', int.Parse(match.Groups[1].Value)) + " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(?:p|div|h[1-6]|ul|ol|table|tr|pre)\b[^>]*>|<br\s*/?>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]*>", "");
        return Regex.Replace(WebUtility.HtmlDecode(text), @"\n[ \t]*\n(?:[ \t]*\n)+", "\n\n").Trim();
    }

    public static string[] Split(string text)
    {
        if (text.Length > MaxCharacters) throw new InvalidDataException("文档过长，请在项目主页查看全文。");
        var parts = new List<string>();
        var protectedSpans = Protected.Matches(text).Cast<Match>().ToArray();
        for (int offset = 0; offset < text.Length;)
        {
            int length = Math.Min(ChunkCharacters, text.Length - offset);
            if (offset + length < text.Length)
            {
                int line = text.LastIndexOf('\n', offset + length - 1, length);
                if (line >= offset + length / 2) length = line - offset + 1;
                var crossing = protectedSpans.FirstOrDefault(span => span.Index < offset + length && span.Index + span.Length > offset + length);
                if (crossing != null) length = crossing.Index > offset ? crossing.Index - offset : crossing.Length;
                if (char.IsHighSurrogate(text[offset + length - 1])) length--;
            }
            parts.Add(text.Substring(offset, length)); offset += length;
        }
        return parts.ToArray();
    }

    public static PyDict Create(string source, string project, string section, string version, string text, bool html = false)
    {
        string original = Readable(text, html);
        string key = Hash(source + "\n" + project + "\n" + section + "\n" + version + "\n" + original);
        var parts = Split(original);
        var translated = parts.Select((part, index) => (object)Cached(key, index, part)).ToList();
        return PyDict.Of("key", key, "section", section, "version", version, "original", original,
            "parts", parts.Cast<object>().ToList(), "translations", translated,
            "manual", parts.Select((part, index) => (object)(DocumentCorrections.Read(key, index, part) != null)).ToList());
    }

    public static string TranslatePart(string key, int index, string original, PyDict settings, Func<string, string>? translate = null)
    {
        bool verbatim = IsVerbatim(original);
        if (!Regex.IsMatch(key, @"\A[a-f0-9]{64}\z") || index < 0 || index > 1000 || original.Length > (verbatim ? MaxCharacters : ChunkCharacters))
            throw new ArgumentException("翻译段落无效，请重新加载文档。");
        string cached = Cached(key, index, original);
        if (cached.Length > 0) return cached;
        if (verbatim) return original;
        if (string.IsNullOrWhiteSpace(original)) return original;
        var tokens = new List<string>();
        string prompt = Protected.Replace(original, match => { tokens.Add(match.Value); return "HELLKEEP" + (tokens.Count - 1).ToString("D3") + "TOKEN"; });
        string translated = translate != null ? translate(prompt) : LocalCatalogTranslator.TranslateDocument(prompt, settings);
        if (translated.Length == 0 || translated.Length > 12000 || !ResourceNames.ContainsChinese(translated) && Regex.IsMatch(Protected.Replace(original, ""), "[a-zA-Z]{3}"))
            throw new InvalidDataException("本地模型未返回有效中文译文，原文已保留，可重试。");
        for (int token = 0; token < tokens.Count; token++)
        {
            string marker = "HELLKEEP" + token.ToString("D3") + "TOKEN";
            if (Regex.Matches(translated, marker).Count != 1) throw new InvalidDataException("译文改变了代码或链接，原文已保留，可重试。");
            translated = translated.Replace(marker, tokens[token]);
        }
        translated = Regex.Match(original, @"\A\s*").Value + translated.Trim() + Regex.Match(original, @"\s*\z").Value;
        lock (DocumentCorrections.Gate)
        {
            string? correction = DocumentCorrections.Read(key, index, original);
            if (correction != null) return correction;
            string path = CachePath(key, index, original);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".new", translated, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(path + ".new", path, null);
            else File.Move(path + ".new", path);
        }
        return translated;
    }

    private static string Cached(string key, int index, string original)
    {
        string? correction = DocumentCorrections.Read(key, index, original);
        if (correction != null) return correction;
        if (IsVerbatim(original)) return original;
        string path = CachePath(key, index, original);
        try { return File.Exists(path) && new FileInfo(path).Length <= 128000 ? File.ReadAllText(path, Encoding.UTF8) : ""; }
        catch (IOException ex) { System.Diagnostics.Trace.TraceWarning("CatalogDocument.Cache: " + ex.Message); return ""; }
    }
    public static string CurrentTranslation(string key, int index, string original) => Cached(key, index, original);
    private static bool IsVerbatim(string text) => !Regex.IsMatch(Protected.Replace(text, ""), @"\p{L}");
    private static string CachePath(string key, int index, string text) => Path.Combine(Utils.ROOT, "cache", "catalog-documents", Hash("v3:" + key + ":" + index + ":" + text) + ".txt");
    private static string Hash(string value)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
}
