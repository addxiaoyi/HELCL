using System;
using System.IO;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class CatalogDocumentTests
{
    [Fact]
    public void SplittingPreservesCompleteDocumentAndSurrogatePairs()
    {
        string original = string.Concat(Enumerable.Repeat("## Changes\n\nUpdated resource rendering 🌲 and compatibility.\n\n", 110));
        var parts = CatalogDocument.Split(original);
        Assert.True(parts.Length > 2);
        Assert.Equal(original, string.Concat(parts));
        Assert.All(parts, part => { Assert.False(char.IsLowSurrogate(part[0])); Assert.False(char.IsHighSurrogate(part[^1])); });
        Assert.Throws<InvalidDataException>(() => CatalogDocument.Split(new string('a', CatalogDocument.MaxCharacters + 1)));
    }

    [Fact]
    public void HtmlKeepsParagraphsAndStripsExecutableContent()
    {
        string readable = CatalogDocument.Readable("<h2>Changes</h2><p>First</p><p>Second &amp; third</p><script>bad()</script>", true);
        Assert.Contains("## Changes", readable);
        Assert.Contains("\n\n", readable);
        Assert.Contains("Second & third", readable);
        Assert.DoesNotContain("bad()", readable);
    }

    [Fact]
    public void TranslationKeepsWhitespaceBetweenChunks()
    {
        using var home = new TestHome();
        string original = "First paragraph.\n\n";
        string key = Py.S(CatalogDocument.Create("modrinth", "fixture", "description", "", original)["key"]);
        Assert.Equal("第一段。\n\n", CatalogDocument.TranslatePart(key, 0, original, new PyDict(), text => text.Replace("First paragraph.", "第一段。")));
    }

    [Fact]
    public void ProtectedCodeIsNotSplitBetweenTranslationChunks()
    {
        string original = new string('a', 1500) + "\n```json\n" + new string('b', 500) + "\n```\nEnd";
        var parts = CatalogDocument.Split(original);
        Assert.Equal(original, string.Concat(parts));
        Assert.All(parts, part => Assert.Equal(0, System.Text.RegularExpressions.Regex.Matches(part, "```").Count % 2));
    }

    [Fact]
    public void TranslationRestoresCodeAndLinksAndCachesEachOriginalSeparately()
    {
        using var home = new TestHome();
        string original = "Read `config.json` at https://example.com/docs.\n";
        var document = CatalogDocument.Create("modrinth", "fixture", "description", "", original);
        string key = Py.S(document["key"]);
        int calls = 0;
        string translated = CatalogDocument.TranslatePart(key, 0, original, new PyDict(), text => { calls++; return "中文说明 " + text; });
        Assert.Contains("`config.json`", translated);
        Assert.Contains("https://example.com/docs", translated);
        Assert.DoesNotContain("HELLKEEP", translated);
        Assert.Equal(translated, CatalogDocument.TranslatePart(key, 0, original, new PyDict(), _ => throw new Exception("cache miss")));
        Assert.Equal(1, calls);
        Assert.Throws<InvalidDataException>(() => CatalogDocument.TranslatePart(key, 1, original, new PyDict(), _ => "缺少保护内容"));
        Assert.NotEqual(key, CatalogDocument.Create("modrinth", "other", "description", "", original)["key"]);
        Assert.NotEqual(key, CatalogDocument.Create("modrinth", "fixture", "changelog", "v1", original)["key"]);
    }

    [Fact]
    public void VersionOnlyChangelogNeedsNoModelAndKeepsItsExactLinks()
    {
        using var home = new TestHome();
        string original = "## [8.22.0](https://example.com/compare/v8.21...v8.22) (2026-10-31)";
        var document = CatalogDocument.Create("modrinth", "fixture", "changelog", "v8.22", original);
        Assert.Equal(original, Py.ListOr(document["translations"])[0]);
        Assert.Equal(original, CatalogDocument.TranslatePart(Py.S(document["key"]), 0, original, new PyDict(), _ => throw new Exception("must not invoke model")));
    }
}
