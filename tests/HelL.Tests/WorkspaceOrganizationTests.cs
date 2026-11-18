using System;
using System.IO;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Models;
using Xunit;

namespace HelL.Tests;

public sealed class WorkspaceOrganizationTests
{
    [Fact]
    public void InstanceGroupSurvivesRenameWithoutChangingOtherMetadata()
    {
        using var home = new TestHome();
        var game = new Instance("survival", forCreation: true);
        game.Create(PyDict.Of("mc_version", "1.21.1", "note", "keep"));
        InstanceGroups.Assign("survival", "  生存联机  ");
        game.Rename("friends");
        Assert.Equal("生存联机", game.Meta()["group"]);
        Assert.Equal("keep", game.Meta()["note"]);
        Assert.Equal("1.21.1", game.Meta()["mc_version"]);
        InstanceGroups.Assign("friends", "");
        Assert.Equal("", game.Meta()["group"]);
    }

    [Fact]
    public void GroupRejectsMissingInstanceAndPreservesCorruptMetadata()
    {
        using var home = new TestHome();
        Assert.Throws<ArgumentException>(() => InstanceGroups.Assign("missing", "group"));
        var game = new Instance("broken", forCreation: true); game.Create();
        string metadata = Path.Combine(game.path, ".instance.json");
        File.WriteAllText(metadata, "[broken");
        Assert.Throws<InvalidDataException>(() => InstanceGroups.Assign("broken", "group"));
        Assert.Equal("[broken", File.ReadAllText(metadata));
        Assert.Throws<ArgumentException>(() => InstanceGroups.Normalize(new string('a', 33)));
        Assert.Throws<ArgumentException>(() => InstanceGroups.Normalize("a\nb"));
    }

    [Fact]
    public void CatalogSortingKeepsOriginalRelevanceAndPutsUnknownDatesLast()
    {
        var older = new CatalogItem { Name = "B", Downloads = 10, Updated = "2026-10-01T10:00:00Z" };
        var unknown = new CatalogItem { Name = "C", Downloads = 100, Updated = "bad" };
        var latest = new CatalogItem { Name = "A", Downloads = 10, Updated = "2026-11-01T10:00:00+08:00" };
        var rows = new[] { older, unknown, latest };
        Assert.Equal(rows, CatalogOrdering.Sort(rows, "relevance"));
        Assert.Equal(new[] { latest, older, unknown }, CatalogOrdering.Sort(rows, "updated"));
        Assert.Equal(new[] { unknown, latest, older }, CatalogOrdering.Sort(rows, "downloads"));
        Assert.Equal(new[] { latest, older, unknown }, CatalogOrdering.Sort(rows, "name"));
        Assert.Equal(older, rows[0]);
        Assert.Throws<ArgumentException>(() => CatalogOrdering.Sort(rows, "invalid"));
    }

    [Theory]
    [InlineData("engine")]
    [InlineData("windows")]
    [InlineData("local")]
    public void WallpaperPresetsKeepCurrentSourceAndDoNotDiscardPaths(string mode)
    {
        var patch = AppearanceProfiles.Preset("wallpaper-glass", mode);
        Assert.Equal(mode, patch["ui_wallpaper_mode"]);
        Assert.False(patch.ContainsKey("ui_background"));
        Assert.False(patch.ContainsKey("ui_wallpaper_engine_config"));
    }

    [Fact]
    public void ThemeApplyWhitelistExcludesAccountAndInstanceConfiguration()
    {
        using var home = new TestHome();
        string directory = Config.CONFIG.instances_dir;
        Themes.Apply(PyDict.Of("ui_glass_cards", 50L, "instances_dir", "elsewhere", "unrelated_secret", "never-save"));
        Assert.Equal(directory, Config.CONFIG.instances_dir);
        Assert.Equal(50L, Config.CONFIG.Get("ui_glass_cards"));
        Assert.False(Config.CONFIG.data.ContainsKey("unrelated_secret"));
        Assert.False(Themes.Current().ContainsKey("instances_dir"));
    }
}
