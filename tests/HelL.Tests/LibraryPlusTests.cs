using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class LibraryPlusTests
{
    [Fact]
    public void CompatibilityNeverFallsBackToAnIncompatibleVersion()
    {
        var versions = new[] {
            PyDict.Of("version_number", "new-forge", "date_published", "2026-11-07", "game_versions", Py.L("1.21.1"), "loaders", Py.L("forge")),
            PyDict.Of("version_number", "fabric", "date_published", "2026-11-06", "game_versions", Py.L("1.21.1"), "loaders", Py.L("fabric"))
        };
        Assert.Equal("fabric", ProjectCompatibility.SelectVersion(versions, "1.21.1", "fabric")!["version_number"]);
        Assert.Null(ProjectCompatibility.SelectVersion(versions, "1.20.1", "fabric"));
        Assert.Equal("new-forge", ProjectCompatibility.SelectVersion(versions, "", "")!["version_number"]);
    }

    [Fact]
    public void CurseForgeSelectsNewestCompatibleFileRegardlessOfResponseOrder()
    {
        var files = new[] {
            PyDict.Of("id", 1L, "fileDate", "2026-11-01T08:00:00Z", "gameVersions", Py.L("1.21.1", "Fabric")),
            PyDict.Of("id", 2L, "fileDate", "2026-11-07T08:00:00Z", "gameVersions", Py.L("1.21.1", "Forge")),
            PyDict.Of("id", 3L, "fileDate", "2026-11-06T08:00:00Z", "gameVersions", Py.L("1.21.1", "Fabric")),
            PyDict.Of("id", 4L, "fileDate", "2026-11-07T09:00:00Z", "gameVersions", Py.L("1.20.1", "Fabric"))
        };
        Assert.Equal(3L, ProjectCompatibility.SelectCfFile(files, "1.21.1", "FABRIC")!["id"]);
        Assert.Equal(4L, ProjectCompatibility.SelectCfFile(files, "", "")!["id"]);
        Assert.Null(ProjectCompatibility.SelectCfFile(files, "1.21.10", "fabric"));
        Assert.Null(ProjectCompatibility.SelectCfFile(files, "1.21.1", "neoforge"));
    }

    [Fact]
    public void CurseForgeSeparatesLoadersFromGameVersionsAndIgnoresEnvironmentTags()
    {
        var files = new[] {
            PyDict.Of("gameVersions", Py.L("1.21.1", "Fabric", "Java 21", "Client", "1.21")),
            PyDict.Of("gameVersions", Py.L("1.21.1", "fabric", "NeoForge", "Server")),
            new PyDict()
        };
        var supported = ProjectCompatibility.CfSupport(files);
        Assert.Equal(new[] { "1.21.1", "1.21" }, Py.ListOr(supported.Get("game_versions")).Select(Py.S));
        Assert.Equal(new[] { "fabric", "neoforge" }, Py.ListOr(supported.Get("loaders")).Select(Py.S));
        Assert.Null(ProjectCompatibility.SelectCfFile(new[] { new PyDict() }, "1.21.1", "fabric"));
    }

    [Theory]
    [InlineData(1L, "embedded")]
    [InlineData(2L, "optional")]
    [InlineData(3L, "required")]
    [InlineData(5L, "incompatible")]
    [InlineData(99L, "unknown")]
    public void CurseForgeDependencyRelationsKeepTheirMeaning(long relation, string expected)
        => Assert.Equal(expected, ProjectCompatibility.CfDependencyKind(relation));

    [Fact]
    public void GalleryUsesOnlyTrustedArtworkAndKeepsTitles()
    {
        var gallery = ProjectCompatibility.Gallery(PyDict.Of("gallery", Py.L(
            PyDict.Of("url", "https://cdn.modrinth.com/one.webp", "title", "First", "featured", false),
            PyDict.Of("url", "https://cdn.modrinth.com/two.webp", "raw_url", "https://cdn.modrinth.com/raw.webp", "title", "<b>Featured</b>", "featured", true),
            PyDict.Of("url", "file:///C:/secret.png", "title", "invalid"))));
        Assert.Equal(2, gallery.Count);
        Assert.Equal("https://cdn.modrinth.com/raw.webp", ((PyDict)gallery[0])["url"]);
        Assert.Equal("Featured", ((PyDict)gallery[0])["title"]);
    }

    [Fact]
    public void ScreenshotsCanBePagedWithoutDuplicates()
    {
        using var home = new TestHome();
        string shots = Path.Combine(home.Dir, "screenshots"); Directory.CreateDirectory(shots);
        for (int i = 0; i < 31; i++) File.WriteAllBytes(Path.Combine(shots, i.ToString("D2") + ".png"), new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var first = InstanceArtwork.Screenshots(new[] { home.Dir, home.Dir }, 0, 24);
        var second = InstanceArtwork.Screenshots(new[] { home.Dir }, 24, 24);
        Assert.Equal(31L, first["total"]);
        Assert.Equal(24, ((List<object>)first["items"]).Count);
        Assert.Equal(7, ((List<object>)second["items"]).Count);
        Assert.Empty(((List<object>)first["items"]).Cast<PyDict>().Select(row => row["path"]).Intersect(((List<object>)second["items"]).Cast<PyDict>().Select(row => row["path"])));
        Assert.Throws<ArgumentException>(() => InstanceArtwork.Screenshots(new[] { home.Dir }, -1, 24));
        Assert.Throws<ArgumentException>(() => InstanceArtwork.Screenshots(new[] { home.Dir }, 0, 200));
    }

    [Fact]
    public void InvalidCoverDoesNotChangeInstanceMetadata()
    {
        using var home = new TestHome();
        var instance = new Instance("cover-test", true); instance.Create();
        string file = Path.Combine(home.Dir, "broken.png"); File.WriteAllText(file, "not an image");
        Assert.Throws<ArgumentException>(() => InstanceArtwork.SetCover(instance, file));
        Assert.False(Py.Truthy(instance.Meta().Get("custom_cover")));
    }

    [Fact]
    public void ScreenshotRefreshReturnsToLastValidPageAfterDeletion()
    {
        using var home = new TestHome();
        string folder = Path.Combine(home.Dir, "screenshots"); Directory.CreateDirectory(folder);
        for (int i = 0; i < 25; i++) File.WriteAllBytes(Path.Combine(folder, i + ".png"), new byte[] { 137, 80, 78, 71 });
        var last = InstanceArtwork.Screenshots(new[] { home.Dir }, 24, 24);
        File.Delete(Py.S(((PyDict)Py.ListOr(last.Get("items")).Single()).Get("path")));
        var refreshed = InstanceArtwork.Screenshots(new[] { home.Dir }, 24, 24);
        Assert.Equal(0L, refreshed.Get("offset"));
        Assert.Equal(24, Py.ListOr(refreshed.Get("items")).Count);
        foreach (string file in Directory.EnumerateFiles(folder)) File.Delete(file);
        Assert.Equal(0L, InstanceArtwork.Screenshots(new[] { home.Dir }, 24, 24).Get("offset"));
    }

    [Fact]
    public void UnknownJavaMajorIsNotReportedAsAnIncompatibleJavaZero()
    {
        var request = new HelL.Services.LaunchPreflightRequest { MinecraftVersion = "1.21.1", JavaPath = "自动选择", AllocatedMemoryMb = 2048 };
        Assert.DoesNotContain(HelL.Services.LaunchPreflightGate.Evaluate(request).Issues, issue => issue.Code == "JAVA_VERSION_MISMATCH");
    }
}
