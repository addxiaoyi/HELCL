using HelL.Core;
using HelL.Core.Launcher;
using HelL.Tests;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class FrontendSharedServicesTests
{
    [Fact]
    public void CatalogPageRespectsItsRequestedSizeInsteadOfAHiddenFortyItemCap()
    {
        using var home = new TestHome();
        var rows = CatalogSearch.Search("mod", "", "modrinth", PyDict.Of("offset", 30L, "limit", 12L),
            request => Enumerable.Range(30, 20).Select(index => (object)PyDict.Of("source", request.Source,
                "id", "project-" + index, "slug", "project-" + index, "title", "Project " + index)).ToList());
        Assert.Equal(12, rows.Count);
    }

    [Fact]
    public void RecentSortingCanReadEveryInstanceBeyondTheDashboardPreviewLimit()
    {
        var installed = Enumerable.Range(0, 60).ToDictionary(index => "instance-" + index,
            _ => new List<string> { "1.21.1" });
        Assert.Equal(60, DashboardLibrary.Select(new PyDict(), installed, 1000, 60).Count);
    }

    [Fact]
    public void ResourceRecoveryRestoresVerifiedBytesAndNeverOverwritesANewFile()
    {
        using var home = new TestHome();
        var instance = new Instance("recycle-test", forCreation: true);
        instance.Create();
        string original = Path.Combine(instance.path, "mods", "fixture.jar");
        File.WriteAllText(original, "old verified fixture");
        RecycledResource entry = ResourceRecycle.Move(instance, "fixture.jar", "mod");
        Assert.False(File.Exists(original));
        Assert.Equal("old verified fixture", File.ReadAllText(entry.Recycled));
        File.WriteAllText(original, "new installed fixture");
        Assert.Throws<IOException>(() => ResourceRecycle.Restore(instance, entry.Receipt));
        Assert.Equal("new installed fixture", File.ReadAllText(original));
        File.Delete(original);
        Assert.Equal(original, ResourceRecycle.Restore(instance, entry.Receipt));
        Assert.Equal("old verified fixture", File.ReadAllText(original));
    }

    [Fact]
    public void ResourceRecoveryRejectsTraversalTamperingAndForeignInstanceReceipts()
    {
        using var home = new TestHome();
        var instance = new Instance("source", forCreation: true);
        instance.Create();
        var other = new Instance("other", forCreation: true);
        other.Create();
        Assert.Throws<ArgumentException>(() => ResourceRecycle.Move(instance, "../outside.jar", "mod"));
        string original = Path.Combine(instance.path, "resourcepacks", "fixture.zip");
        File.WriteAllText(original, "verified bytes");
        var entry = ResourceRecycle.Move(instance, "fixture.zip", "resourcepack");
        Assert.Throws<InvalidDataException>(() => ResourceRecycle.Restore(other, entry.Receipt));
        File.WriteAllText(entry.Recycled, "tampered");
        Assert.Throws<InvalidDataException>(() => ResourceRecycle.Restore(instance, entry.Receipt));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public void InstallationEstimateReportsOnlyPublishedSizesAndUnknownWhenAbsent()
    {
        using var home = new TestHome();
        var metadata = PyDict.Of("downloads", PyDict.Of("client", PyDict.Of("size", 1024L)),
            "libraries", Py.L(PyDict.Of("downloads", PyDict.Of("artifact", PyDict.Of("size", 512L)))));
        var plan = InstallationPlan.FromMetadata(home.Dir, "1.21.1", metadata);
        Assert.Equal(1536L, plan.DownloadBytes);
        Assert.Contains("不含", plan.Summary);
        Assert.Null(InstallationPlan.FromMetadata(home.Dir, "1.21.1", new PyDict()).DownloadBytes);
    }
}
