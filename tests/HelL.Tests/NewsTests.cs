using System;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class NewsTests
{
    [Fact]
    public void OfficialCoverIsResolvedToAnAbsoluteUrl()
    {
        var feed = PyDict.Of("entries", Py.L(PyDict.Of("title", "Minecraft 26.4 Snapshot 7", "version", "26.4-snapshot-7",
            "id", "26-4-snapshot-7", "type", "snapshot", "image", PyDict.Of("url", "/v2/images/cover.jpg"),
            "shortText", "A new music disc.", "date", "2026-11-03T13:08:56Z")));
        var row = Assert.IsType<PyDict>(Assert.Single(News._rows_from(feed)));
        Assert.Equal("https://launchercontent.mojang.com/v2/images/cover.jpg", row["image"]);
        Assert.Equal("https://www.minecraft.net/en-us/article/minecraft-26-4-snapshot-7", row.Get("url"));
        Assert.Equal("snapshot", row.Get("type"));
    }

    [Fact]
    public void UnsafeCoverAndArticleLinksAreNotExposed()
    {
        var row = Assert.IsType<PyDict>(Assert.Single(News._rows_from(Py.L(PyDict.Of("title", "News",
            "image", "file:///C:/private.png", "readMoreLink", "javascript:alert(1)", "shortText", "<b>Safe</b> &amp; clear")))));
        Assert.Equal("", row["image"]);
        Assert.Equal("", row.Get("url"));
        Assert.Equal("Safe & clear", row["body"]);
    }

    [Theory]
    [InlineData("Minecraft 26.4 Snapshot 7", "Minecraft 26.4 快照 7")]
    [InlineData("Minecraft 26.3 Release Candidate 2", "Minecraft 26.3 候选版 2")]
    [InlineData("Minecraft 26.3 Pre-Release 3", "Minecraft 26.3 预发布版 3")]
    public void ReleaseTitlesUseChineseTerminology(string original, string expected) => Assert.Equal(expected, NewsLocalization.ChineseTitle(original));

    [Fact]
    public void ChangedSourceInvalidatesPreviouslyCompiledText()
    {
        var row = PyDict.Of("title", "Minecraft 26.4 Snapshot 7", "body", "original text");
        string key = NewsLocalization.Key(row);
        row["body"] = "new text";
        Assert.NotEqual(key, NewsLocalization.Key(row));
    }

    [Theory]
    [InlineData("Minecraft: Java Edition 26.3", "26.3", "minecraft-java-edition-26-3")]
    [InlineData("Minecraft 26.3 Pre-Release 3", "26.3-pre-3", "minecraft-26-3-pre-release-3")]
    public void ArticleLinksUseTheOfficialTitleSlug(string title, string version, string slug)
    {
        var row = (PyDict)Assert.Single(News._rows_from(Py.L(PyDict.Of("title", title, "version", version))));
        Assert.Equal("https://www.minecraft.net/en-us/article/" + slug, row["url"]);
    }

    [Fact]
    public void EnglishModeKeepsOriginalNewsAndUntranslatedChineseGetsAnHonestOverview()
    {
        var rows = Py.L(PyDict.Of("id", "future", "title", "Minecraft 99.1 Snapshot 1", "body", "A future official summary", "type", "snapshot"));
        Assert.Same(rows, NewsLocalization.Display(rows, "en"));
        var translated = (PyDict)Assert.Single(NewsLocalization.Display(rows, "zh_CN"));
        Assert.Equal("版本导读", translated["localization"]);
        Assert.Equal("A future official summary", translated["original_body"]);
        Assert.True(NewsLocalization.ContainsChinese((string)translated["body"]));
    }

    [Fact]
    public void TranslationRequiresEveryRequestedItemAndRejectsForeignIds()
    {
        var expected = new System.Collections.Generic.List<PyDict> { PyDict.Of("id", "one", "title", "Title", "body", "Original") };
        var valid = PyDict.Of("items", Py.L(PyDict.Of("id", "one", "title", "中文标题", "summary", "更新了方块。")));
        Assert.Single(NewsLocalization.ValidateTranslations(valid, expected));
        Assert.Throws<InvalidOperationException>(() => NewsLocalization.ValidateTranslations(PyDict.Of("items", Py.L(PyDict.Of("id", "other", "title", "中文标题", "summary", "更新了方块。"))), expected));
        Assert.Throws<InvalidOperationException>(() => NewsLocalization.ValidateTranslations(PyDict.Of("items", Py.L()), expected));
    }

    [Theory]
    [InlineData("https://launchercontent.mojang.com.evil.example/a.png")]
    [InlineData("https://user@launchercontent.mojang.com/a.png")]
    [InlineData("//evil.example/a.png")]
    [InlineData("http://launchercontent.mojang.com/a.png")]
    public void UntrustedImageHostsAreRejected(string url) => Assert.Equal("", News.OfficialUrl(url, true));

    [Theory]
    [InlineData("pulling manifest", "正在获取模型信息")]
    [InlineData("pulling abc123", "正在下载模型文件")]
    [InlineData("verifying sha256 digest", "正在校验模型文件")]
    public void DownloadProgressHasReadableChinese(string status, string expected) =>
        Assert.Equal(expected, HelL.Core.Launcher.Ai.Local.ModelRules.ProgressText(status));
}
