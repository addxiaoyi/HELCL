using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Plugins;
using HelL.Core.Launcher.Sync;
using Xunit;

namespace HelL.Tests;

public sealed class HomeWorkspaceTests
{
    [Fact]
    public void PinsDropMissingInstancesAndDoNotDuplicate()
    {
        var pins = HomePreferences.Pins(Py.L("a", "deleted", "b", "a"), new[] { "a", "b" });
        Assert.Equal(new[] { "a", "b" }, pins);
        Assert.Equal(new[] { "b", "a" }, HomePreferences.SetPinned(pins, "a", true));
        Assert.Equal(new[] { "b" }, HomePreferences.SetPinned(pins, "a", false));
        Assert.Throws<ArgumentException>(() => HomePreferences.SetPinned(Enumerable.Range(0, 24).Select(n => "instance" + n), "new", true));
    }
    [Fact]
    public void PinnedInstancesRankAheadOfUnpinnedRecentSessions()
    {
        var installed = new Dictionary<string, List<string>> { ["pinned"] = new() { "v" }, ["recent"] = new() { "v" } };
        var history = PyDict.Of("recent", PyDict.Of("sessions", Py.L(PyDict.Of("version", "v", "start", 800L, "duration", 30L))));
        var rows = DashboardLibrary.Select(history, installed, 1000, 12, new[] { "pinned" });
        Assert.Equal("pinned", rows[0].Instance); Assert.True(rows[0].Pinned); Assert.Equal(830, rows[1].LastPlayed);
    }
    [Fact]
    public void SidebarCanBeEmptyAndUnknownSectionsAreExcluded()
    {
        Assert.Empty(HomePreferences.NormalizeSections(Py.L()));
        Assert.Equal(new[] { "worlds", "clock" }, HomePreferences.NormalizeSections(Py.L("worlds", "bad", "clock", "worlds")));
    }
    [Fact]
    public void RecentWorldsUseMetadataInsteadOfDirectoryTimeAndDeduplicateRoots()
    {
        using var home = new TestHome();
        var saves = Path.Combine(home.Dir, "saves");
        string first = WorldFixture.Create(saves, "folder-a", "世界甲", 1729770000000L);
        string second = WorldFixture.Create(saves, "folder-b", "世界乙", 1729780000000L);
        File.SetLastWriteTimeUtc(Path.Combine(first, "level.dat"), DateTime.UtcNow);
        var worlds = RecentWorlds.Read(new[] { ("game", "1.20.1", home.Dir), ("game", "other", home.Dir) }, 1800000000, 6);
        Assert.Equal(2, worlds.Count); Assert.Equal("世界乙", worlds[0].Name); Assert.Equal("folder-b", worlds[0].Folder);
        Assert.False(worlds[0].Estimated); Assert.Equal(1729780000L, worlds[0].LastPlayed);
    }
    [Fact]
    public void MissingAndCorruptWorldMetadataDoNotCrashHome()
    {
        using var home = new TestHome();
        Directory.CreateDirectory(Path.Combine(home.Dir, "saves", "missing"));
        var corrupt = Path.Combine(home.Dir, "saves", "corrupt"); Directory.CreateDirectory(corrupt); File.WriteAllText(Path.Combine(corrupt, "level.dat"), "broken");
        var worlds = RecentWorlds.Read(new[] { ("game", "", home.Dir) }, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 6);
        Assert.Single(worlds); Assert.True(worlds[0].Estimated); Assert.Equal("corrupt", worlds[0].Name);
    }
    [Fact]
    public void LevelDatRejectsDecompressionBombsAndDeepCompounds()
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionMode.Compress, true)) gzip.Write(new byte[LevelDat.MaxBytes + 1], 0, LevelDat.MaxBytes + 1);
        Assert.Null(LevelDat.Read(packed.ToArray()));
        var deep = Enumerable.Range(0, 100).SelectMany(_ => new byte[] { 10, 0, 0 }).Concat(new byte[100]).ToArray();
        Assert.Null(LevelDat.Read(deep));
    }
    [Fact]
    public void PluginDetailsRejectOutsideScreenshotsAndInvalidNotes()
    {
        var manifest = PluginCatalog.Builtins()[0].Manifest;
        manifest.Screenshots.Add("../private.png"); Assert.Throws<InvalidDataException>(manifest.Validate);
        manifest.Screenshots.Clear(); manifest.Screenshots.Add("assets/preview.png"); manifest.Validate();
        manifest.Releases.Add(new PluginRelease { Version = "1.0.0", Date = "not-a-date", Notes = "updated" }); Assert.Throws<InvalidDataException>(manifest.Validate);
        manifest.Releases[0].Date = "2026-11-09"; manifest.Validate();
    }
    [Fact]
    public void PackageCannotAdvertiseMissingScreenshots()
    {
        using var home = new TestHome();
        var manifest = PluginCatalog.Builtins()[0].Manifest; manifest.Screenshots.Add("assets/missing.png");
        string file = Path.Combine(home.Dir, "missing.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) using (var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open())) writer.Write(manifest.ToJson());
        Assert.Throws<InvalidDataException>(() => new PluginStore(Path.Combine(home.Dir, "installed")).InstallPackage(file));
    }
}
