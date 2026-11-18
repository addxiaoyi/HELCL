using System.Reflection;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class CatalogArtworkTests
{
    [Fact]
    public void SearchGalleryUrlsAreNormalizedForTheUiContract()
    {
        var artwork = CatalogPresentation.Artwork(PyDict.Of("icon_url", "https://cdn.modrinth.com/icon.png", "gallery", Py.L("https://cdn.modrinth.com/shot.png")));
        var parsed = System.Text.Json.JsonSerializer.Deserialize<HelL.Models.CatalogItem>(PyJson.Dumps(artwork), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
        Assert.Single(parsed.Gallery);
        Assert.Equal("https://cdn.modrinth.com/shot.png", parsed.Gallery[0].Url);
    }

    [Fact]
    public void MissingProjectArtworkNeverBorrowsAnotherProjectsPicture()
    {
        var row = CatalogPresentation.Artwork(PyDict.Of("name", "Unknown resource", "icon_url", "https://unrelated.example/photo.png"));
        Assert.Equal("", row["icon_url"]);
        Assert.Equal("", row["cover_url"]);
        Assert.Empty(Py.ListOr(row["gallery"]));
    }
    [Fact]
    public void SharedSearchMappingKeepsTheSameProjectsArtwork()
    {
        var hit = PyDict.Of("title", "World fixture", "slug", "world-fixture", "source", "curseforge",
            "icon_url", "https://media.forgecdn.net/avatars/one.png", "gallery", Py.L(PyDict.Of("url", "https://media.forgecdn.net/screenshots/one.png")),
            "website_url", "https://www.curseforge.com/minecraft/worlds/world-fixture");
        var row = (PyDict)typeof(CatalogFiles).GetMethod("_hit_row", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { hit, "curseforge" });
        Assert.Equal(hit["icon_url"], row.Get("icon_url"));
        Assert.Equal("https://media.forgecdn.net/screenshots/one.png", row.Get("cover_url"));
        Assert.Equal(hit["website_url"], row.Get("website_url"));
    }
}
