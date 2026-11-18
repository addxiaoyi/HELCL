using System.Text.Json;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using Xunit;

namespace HelL.Tests;

public sealed class LibraryExperienceTests
{
    [Fact]
    public void SaveRowsAcceptActualBackendMetadata()
    {
        var row = JsonSerializer.Deserialize<SaveRow>("{\"name\":\"Survival\",\"path\":\"C:/game/saves/Survival\",\"icon\":\"C:/game/saves/Survival/icon.png\",\"bytes\":2048,\"mtime\":1780000000}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(row);
        Assert.Equal("Survival", row.Name);
        Assert.Equal(1780000000L, System.Convert.ToInt64(row.Mtime));
        Assert.Equal(2048, row.Bytes);
        Assert.EndsWith("icon.png", row.Icon);
    }

    [Fact]
    public void RecentPlaySkipsMissingInstancesAndRemovedVersions()
    {
        var histories = PyDict.Of("deleted", PyDict.Of("sessions", Py.L(PyDict.Of("start", 900L, "duration", 50L, "version", "a"))),
            "survival", PyDict.Of("sessions", Py.L(PyDict.Of("start", 800L, "duration", 20L, "version", "old"),
                PyDict.Of("start", 700L, "duration", 40L, "version", "1.21.1"))));
        var installed = new Dictionary<string, List<string>> { ["survival"] = new List<string> { "1.21.1" } };
        var recent = RecentPlay.Select(histories, installed, 1000);
        Assert.Equal("survival", recent["instance"]); Assert.Equal("1.21.1", recent["version"]); Assert.Equal(740L, recent["ended"]);
        installed.Clear(); Assert.Equal(false, RecentPlay.Select(histories, installed, 1000)["available"]);
    }

    [Fact]
    public void RecentPlayIgnoresInvalidOrFutureSessions()
    {
        var history = PyDict.Of("world", PyDict.Of("sessions", Py.L(PyDict.Of("start", long.MaxValue, "duration", 60L, "version", "v"),
            PyDict.Of("start", 800L, "duration", -5L, "version", "v"), PyDict.Of("start", "bad", "duration", 5L, "version", "v"))));
        Assert.Equal(false, RecentPlay.Select(history, new Dictionary<string, List<string>> { ["world"] = new List<string> { "v" } }, 1000)["available"]);
    }

    [Fact]
    public void InstanceArtworkPrefersNewestScreenshotAndFallsBackToWorldIcon()
    {
        string root = Path.Combine(Path.GetTempPath(), "hell-artwork-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "screenshots")); Directory.CreateDirectory(Path.Combine(root, "saves", "world"));
        try
        {
            byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
            string older = Path.Combine(root, "screenshots", "older.png"), newer = Path.Combine(root, "screenshots", "newer.png"), icon = Path.Combine(root, "saves", "world", "icon.png");
            File.WriteAllBytes(older, png); File.WriteAllBytes(newer, png); File.WriteAllBytes(icon, png);
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2)); File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(-1));
            Assert.Equal(newer, InstanceMedia.Find(new[] { root })["path"]);
            File.Delete(older); File.Delete(newer);
            Assert.Equal(icon, InstanceMedia.Find(new[] { root })["path"]);
            Assert.Equal("world", InstanceMedia.Find(new[] { root })["kind"]);
            Assert.False(InstanceMedia.IsLocalImage(icon, Path.Combine(root, "screenshots")));
            File.WriteAllText(icon, "not a png"); Assert.False(InstanceMedia.IsLocalImage(icon, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResourceCoversRejectUntrustedHostsAndPreferFeaturedGallery()
    {
        Assert.Equal("", CatalogPresentation.ImageUrl("https://cdn.modrinth.com.evil.test/image.png"));
        Assert.Equal("", CatalogPresentation.ImageUrl("file:///C:/private.png"));
        var project = PyDict.Of("gallery", Py.L(PyDict.Of("url", "https://cdn.modrinth.com/first.png", "featured", false), PyDict.Of("url", "https://cdn.modrinth.com/featured.png", "featured", true)));
        Assert.Equal("https://cdn.modrinth.com/featured.png", CatalogPresentation.Cover(project));
    }

    [Fact]
    public void ResourceSummaryKeepsOriginalAndValidatesTranslationIdentity()
    {
        var project = PyDict.Of("source", "modrinth", "slug", "unlisted-fixture", "name", "Test", "description", "Original summary");
        CatalogPresentation.Localize(project, new PyDict());
        Assert.Equal("Original summary", project["original_description"]);
        Assert.Equal("英文简介待翻译", project["localization"]);
        string id = CatalogPresentation.SummaryKey(project);
        var payload = PyDict.Of("items", Py.L(PyDict.Of("id", id, "summary", "中文简介。")));
        Assert.Equal("中文简介。", CatalogPresentation.ValidateTranslations(payload, new List<PyDict> { project })[id]);
        Assert.Throws<InvalidOperationException>(() => CatalogPresentation.ValidateTranslations(PyDict.Of("items", Py.L()), new List<PyDict> { project }));
    }

    [Theory]
    [InlineData("release", "正式版")]
    [InlineData("BETA", "测试版")]
    [InlineData("queued", "排队中")]
    [InlineData("vanilla", "原版")]
    [InlineData("Fabric", "Fabric")]
    public void TechnicalStatusIsLocalizedWithoutRenamingProducts(string raw, string expected) => Assert.Equal(expected, HelL.Services.DisplayLabels.Of(raw));
}
