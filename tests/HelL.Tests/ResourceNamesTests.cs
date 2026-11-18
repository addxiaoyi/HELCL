using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class ResourceNamesTests
{
    [Fact]
    public void CreateUsesItsOwnCurseForgeProject()
    {
        Assert.Equal("328085", ResourceNames.Find("mod", "curseforge", "create", "328085", "Create").CurseforgeId);
        Assert.Equal(328085L, Catalog.LookupModAlias("机械动力")[1]);
        var jei = ResourceNames.Find("mod", "curseforge", "", "238222", "Just Enough Items (JEI)");
        Assert.NotEqual("机械动力", jei?.Chinese);
    }

    [Fact]
    public void TranslationCacheAndUiKeysSeparateProjectsWithoutSlugs()
    {
        var first = PyDict.Of("source", "curseforge", "slug", "", "id", 123L, "name", "First", "description", "");
        var second = PyDict.Of("source", "curseforge", "slug", "", "id", 456L, "name", "Second", "description", "");
        Assert.NotEqual(CatalogPresentation.SummaryKey(first), CatalogPresentation.SummaryKey(second));
        var item = System.Text.Json.JsonSerializer.Deserialize<HelL.Models.CatalogItem>("{\"Source\":\"curseforge\",\"Slug\":\"\",\"Id\":123}");
        Assert.Equal("curseforge:123", item.Key);
    }

    [Fact]
    public void ExpandedQueriesAlwaysKeepTheOriginal()
    {
        var queries = ResourceNames.Queries("shader", "光影");
        Assert.Contains("光影", queries);
        Assert.Contains("shaders", queries);
        Assert.InRange(queries.Count, 1, 4);
    }

    [Fact]
    public void FailedTranslatedSearchIsNotMaskedByEmptyChineseResults()
    {
        Assert.Throws<InvalidOperationException>(() => CatalogSearch.Search("mod", "钠", "modrinth", new PyDict(), request =>
            ResourceNames.ContainsChinese(request.Query) ? new List<object>() : throw new System.IO.IOException("offline")));
    }

    [Theory]
    [InlineData("mod", "sodium", "Sodium", "钠")]
    [InlineData("modpack", "fabulously-optimized", "Fabulously Optimized", "难以置信的优化")]
    [InlineData("resourcepack", "fresh-animations", "Fresh Animations", "生动动画")]
    [InlineData("shader", "complementary-reimagined", "Complementary Shaders - Reimagined", "互补光影：重制版")]
    [InlineData("datapack", "terralith", "Terralith", "Terralith 地形扩展")]
    [InlineData("world", "skyblock", "SkyBlock", "空岛生存")]
    public void CatalogNamesKeepOriginalIdentity(string kind, string slug, string original, string chinese)
    {
        var row = PyDict.Of("kind", kind, "source", "modrinth", "slug", slug, "name", original, "description", "Test description");
        CatalogPresentation.Localize(row, new PyDict());
        Assert.Equal(chinese, row.Get("chinese_name"));
        Assert.Equal(original, row.Get("name"));
        Assert.Equal(slug, row.Get("slug"));
    }

    [Theory]
    [InlineData("entities", "实体")]
    [InlineData("tweaks", "细节调整")]
    [InlineData("fantasy", "奇幻")]
    [InlineData("parkour", "跑酷")]
    [InlineData("environment", "环境")]
    [InlineData("models", "模型")]
    [InlineData("Utility & QoL", "实用与体验改善")]
    [InlineData("512x and Higher", "512× 及更高分辨率")]
    public void ResourceCategoriesHaveChineseLabels(string category, string chinese) =>
        Assert.Equal(chinese, CatalogPresentation.Category(category));

    [Theory]
    [InlineData("mod", "钠", "Sodium")]
    [InlineData("modpack", "精致优化", "Fabulously Optimized")]
    [InlineData("resourcepack", "生动动画", "Fresh Animations")]
    [InlineData("shader", "互补光影", "Complementary Shaders - Reimagined")]
    [InlineData("datapack", "连锁采集", "VeinMiner")]
    [InlineData("world", "空岛", "SkyBlock")]
    public void ChineseQueriesExpandForEveryResourceKind(string kind, string query, string expected)
    {
        Assert.Contains(expected, ResourceNames.Queries(kind, query));
        Assert.Contains(query, ResourceNames.Queries(kind, query));
    }

    [Fact]
    public void SearchUsesSelectedSourceAndFiltersForEveryTranslatedQuery()
    {
        var calls = new System.Collections.Concurrent.ConcurrentBag<CatalogSearchRequest>();
        var hits = CatalogSearch.Search("shader", "互补光影", "CurseForge", PyDict.Of("game_version", "1.21.1", "category", "高性能"), request =>
        {
            calls.Add(request);
            return Py.L(PyDict.Of("source", "curseforge", "id", 123L, "slug", "complementary-reimagined", "title", "Complementary Shaders - Reimagined"));
        });
        Assert.All(calls, request => { Assert.Equal("curseforge", request.Source); Assert.Equal("1.21.1", request.Filters["game_version"]); Assert.Equal("高性能", request.Filters["category"]); });
        Assert.Single(hits);
        Assert.Equal("互补光影：重制版", ((PyDict)hits[0])["chinese_name"]);
    }

    [Fact]
    public void SourceIdsRemainSeparateAndExactChineseMatchRanksFirst()
    {
        var hits = CatalogSearch.Search("mod", "钠", "all", new PyDict(), request => Py.L(
            PyDict.Of("source", request.Source, "slug", "sodium-extra", "title", "Sodium Extra", "downloads", 999999999L),
            PyDict.Of("source", request.Source, "slug", "sodium", "title", "Sodium", "downloads", 100L)));
        Assert.Equal(4, hits.Count);
        Assert.Equal("sodium", ((PyDict)hits[0])["slug"]);
        Assert.Equal("sodium", ((PyDict)hits[1])["slug"]);
    }

    [Fact]
    public void UnmappedChineseAndEnglishQueriesRemainAvailable()
    {
        Assert.Equal(new[] { "Unknown New Project" }, ResourceNames.Queries("mod", "Unknown New Project"));
        Assert.Equal(new[] { "不存在的专有名词" }, ResourceNames.Queries("mod", "不存在的专有名词"));
        Assert.Contains("horror parkour", ResourceNames.Queries("world", "恐怖跑酷"));
        Assert.DoesNotContain(ResourceNames.Queries("mod", "OptiFine"), query => query.Contains("Iris"));
    }

    [Fact]
    public void CompleteNetworkFailureIsNotAnEmptySearch()
    {
        Assert.Throws<InvalidOperationException>(() => CatalogSearch.Search("world", "空岛", "curseforge", new PyDict(), _ => throw new System.IO.IOException("offline")));
        Assert.Empty(CatalogSearch.Search("world", "空岛", "modrinth", new PyDict(), _ => throw new Exception("must not be called")));
    }

    [Fact]
    public void CommunityCsvPreservesQuotedNamesAndSourceSlugs()
    {
        const string csv = "# attribution\n1,cf-slug,mr-slug,\"中文,名称\",\"English, Name\",ABBR,modid;alias\n2,no-cn,,English Only,,,\n";
        var rows = CommunityNames.Parse(csv, "mod");
        var row = Assert.Single(rows);
        Assert.Equal("中文,名称", row.Chinese);
        Assert.Equal("English, Name", row.English);
        Assert.Equal("mr-slug", row.Modrinth);
        Assert.Equal("cf-slug", row.Curseforge);
        Assert.Contains("alias", row.Aliases);
        Assert.Throws<System.IO.InvalidDataException>(() => CommunityNames.Parse("1,broken", "mod"));
        var legacy = Assert.Single(CommunityNames.Parse("20103,,don\"t-die,补药趋势啊！,Don't Die!,DTD,dont_die", "mod"));
        Assert.Equal("补药趋势啊！", legacy.Chinese);
    }

    [Fact]
    public void KeywordNamesPreserveBrandsVersionsAndOriginalIdentity()
    {
        Assert.Equal("Voidblock - 空岛 1.21.1", ResourceTerms.TranslateTitle("Voidblock - Skyblock 1.21.1"));
        Assert.Equal("FreshAnimationsPlus", ResourceTerms.TranslateTitle("FreshAnimationsPlus"));
        var project = PyDict.Of("kind", "world", "source", "curseforge", "id", 422034L, "name", "Voidblock - Skyblock", "description", "");
        CatalogPresentation.Localize(project, new PyDict());
        Assert.Equal("Voidblock - Skyblock", project["name"]);
        Assert.Equal("关键词译名", project["name_localization"]);
    }

    [Fact]
    public void LocalNameTranslationKeepsUpstreamNameAndDoesNotInventSummary()
    {
        var row = PyDict.Of("kind", "world", "source", "curseforge", "id", 99999L, "name", "New World", "original_description", "");
        string id = CatalogPresentation.SummaryKey(row);
        CatalogPresentation.Localize(row, PyDict.Of(id + ":name", "新世界"));
        Assert.Equal("新世界", row["chinese_name"]);
        Assert.Equal("New World", row["name"]);
        Assert.Equal("本地译名", row["name_localization"]);
        Assert.Equal("", CatalogPresentation.ValidateTranslations(PyDict.Of("items", Py.L(PyDict.Of("id", id, "summary", ""))), new List<PyDict> { row })[id]);
    }
}
