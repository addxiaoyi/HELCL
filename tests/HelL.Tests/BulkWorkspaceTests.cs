using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class BulkWorkspaceTests
{
    private static Instance Game(string name)
    {
        var game = new Instance(name, forCreation: true); game.Create(); return game;
    }
    private static void Version(Instance game, string id, PyDict json)
    {
        string directory = Path.Combine(game.VersionsDir(), id); Directory.CreateDirectory(directory);
        Utils.WriteJson(Path.Combine(directory, id + ".json"), json);
    }

    [Fact]
    public void BatchGroupingValidatesEveryInstanceBeforeWriting()
    {
        using var home = new TestHome(); var first = Game("first"); var second = Game("second");
        first.SetMeta("group", "before");
        string corrupt = Path.Combine(second.path, ".instance.json"); File.WriteAllText(corrupt, "broken");
        Assert.Throws<InvalidDataException>(() => InstanceGroups.AssignMany(new[] { first.name, second.name }, "after"));
        Assert.Equal("before", first.Meta()["group"]); Assert.Equal("broken", File.ReadAllText(corrupt));
        Assert.Throws<ArgumentException>(() => InstanceGroups.AssignMany(new[] { first.name, "missing" }, "after"));
        Assert.Equal("before", first.Meta()["group"]);
    }

    [Fact]
    public void RenameGroupKeepsMembershipAndRejectsNameCollision()
    {
        using var home = new TestHome(); var first = Game("first"); var second = Game("second"); var third = Game("third");
        InstanceGroups.AssignMany(new[] { "first", "second", "first" }, "Friends"); InstanceGroups.Assign("third", "Other");
        Assert.Throws<ArgumentException>(() => InstanceGroups.Rename("Friends", "other"));
        Assert.Equal(2, InstanceGroups.Rename("friends", "朋友"));
        Assert.Equal("朋友", first.Meta()["group"]); Assert.Equal("朋友", second.Meta()["group"]); Assert.Equal("Other", third.Meta()["group"]);
    }

    [Fact]
    public void PinLimitRejectsEntireBatchAndUnpinKeepsOtherPins()
    {
        using var home = new TestHome(); var names = Enumerable.Range(0, 26).Select(i => Game("game" + i).name).ToArray();
        HomePreferences.PinMany(names.Take(23), true);
        Assert.Throws<ArgumentException>(() => HomePreferences.PinMany(names.Skip(23), true));
        Assert.Equal(23, Py.ListOr(Config.CONFIG.Get("ui_pinned_instances")).Count);
        HomePreferences.PinMany(names.Take(2), false);
        Assert.Equal(names.Skip(2).Take(21), Py.ListOr(Config.CONFIG.Get("ui_pinned_instances")).Select(Py.S));
    }

    [Theory]
    [InlineData("net.fabricmc:fabric-loader:0.16.0", "fabric")]
    [InlineData("net.neoforged:neoforge:21.1.0", "neoforge")]
    [InlineData("net.minecraftforge:forge:1.21.1-52.0.0", "forge")]
    [InlineData("org.quiltmc:quilt-loader:0.26.0", "quilt")]
    public void TargetReadsActualLibrariesAndInheritedVersion(string library, string loader)
    {
        using var home = new TestHome(); var game = Game("test");
        Version(game, "custom-name", PyDict.Of("id", "custom-name", "inheritsFrom", "1.21.1", "libraries", Py.L(PyDict.Of("name", library))));
        var target = CatalogTarget.Inspect(game, "custom-name");
        Assert.Equal("1.21.1", target.GameVersion); Assert.Equal(loader, target.Loader); Assert.Empty(target.Error);
    }

    [Fact]
    public void TargetRejectsInheritanceCyclesAndDoesNotGuessAcrossVersions()
    {
        using var home = new TestHome(); var game = Game("test");
        Version(game, "a", PyDict.Of("inheritsFrom", "b")); Version(game, "b", PyDict.Of("inheritsFrom", "a"));
        var cyclic = CatalogTarget.Inspect(game, "a");
        Assert.Empty(cyclic.GameVersion); Assert.Empty(cyclic.Loader); Assert.Contains("循环", cyclic.Error);
        var target = CatalogTarget.Common(new[] { new CatalogTarget { GameVersion = "1.21.1", Loader = "fabric" }, new CatalogTarget { GameVersion = "1.20.1", Loader = "forge" } });
        Assert.Empty(target.GameVersion); Assert.Empty(target.Loader); Assert.NotEmpty(target.Error);
        Version(game, "fabric-name-but-vanilla", PyDict.Of("id", "1.21.1", "mainClass", "net.minecraft.client.main.Main"));
        Assert.Equal("vanilla", CatalogTarget.Inspect(game, "fabric-name-but-vanilla").Loader);
    }

    [Theory]
    [InlineData("fabric", 4L)] [InlineData("forge", 1L)] [InlineData("neoforge", 6L)] [InlineData("quilt", 5L)]
    public void LoaderUsesCurseforgeEnum(string loader, long id) => Assert.Equal(id, CatalogLoader.CurseForge(loader));

    [Fact]
    public void AppearanceExportImportReloadPreservesAllMaterialFields()
    {
        using var home = new TestHome();
        var patch = PyDict.Of("ui_wallpaper_mode", "transparent", "ui_wallpaper_last_mode", "engine", "ui_glass_transparency", 100L,
            "ui_glass_cards", 67L, "ui_glass_controls", 39L, "ui_glass_popups", 12L, "ui_glass_frost", 17L);
        Themes.Apply(patch); Themes.SaveTheme("中文方案");
        string exported = Path.Combine(home.Dir, "export.json"); Themes.ExportTheme("中文方案", exported);
        Themes.DeleteTheme("中文方案"); Themes.Apply(AppearanceProfiles.Preset("solid", "local"));
        Assert.Equal("中文方案", Themes.ImportTheme(exported)); Themes.LoadTheme("中文方案");
        Config.Reset(); foreach (var entry in patch) Assert.Equal(entry.Value, Config.CONFIG.Get(entry.Key));
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("[]")]
    [InlineData("null")]
    public void CorruptThemeIsReportedInsteadOfSilentlyApplyingNothing(string content)
    {
        using var home = new TestHome(); Themes.SaveTheme("broken");
        string before = File.ReadAllText(Config.CONFIG_FILE);
        File.WriteAllText(Path.Combine(home.Dir, "themes", "broken.json"), content);
        var error = Assert.Throws<ValueError>(() => Themes.LoadTheme("broken"));
        Assert.Contains("主题包数据损坏", error.Message);
        Assert.Equal(before, File.ReadAllText(Config.CONFIG_FILE));
    }
}
