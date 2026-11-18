using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class CatalogOrderTests
{
    [Fact]
    public void NewestAndUpdatedUseDifferentUpstreamDates()
    {
        var old = Project("older", 500) with { Published = "2020-05-01T00:00:00Z", Updated = "2026-11-12T00:00:00Z" };
        var recent = Project("recent", 10) with { Published = "2026-10-01T00:00:00Z", Updated = "2026-10-01T00:00:00Z" };
        var unknown = Project("unknown", 200);
        CatalogProject[] projects = [old, recent, unknown];
        Assert.Equal(["recent", "older", "unknown"], PortableCatalog.OrderProjects(projects, "newest").Select(project => project.Id));
        Assert.Equal(["older", "recent", "unknown"], PortableCatalog.OrderProjects(projects, "updated").Select(project => project.Id));
        Assert.Equal(["older", "unknown", "recent"], PortableCatalog.OrderProjects(projects, "downloads").Select(project => project.Id));
        Assert.Equal(projects, PortableCatalog.OrderProjects(projects, "relevance"));
        Assert.Throws<ArgumentException>(() => PortableCatalog.OrderProjects(projects, "invalid"));
    }

    private static CatalogProject Project(string id, long downloads) =>
        new(id, id, "modrinth", "mod", id, id, "author", "", "", "", downloads);
}
