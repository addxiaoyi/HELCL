using System;
using System.Collections.Generic;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class CatalogVersionTests
{
    [Fact]
    public void OfficialVersionsWinAndSortByDateInsteadOfResponseOrder()
    {
        var calls = new List<string>();
        var rows = CatalogVersions.Read(new[] { "https://official.test", "https://mirror.test" }, "/versions", new PyDict(), "date_published", url =>
        {
            calls.Add(url);
            return Py.L(PyDict.Of("id", "old", "date_published", "2024-01-01T12:00:00Z"),
                PyDict.Of("id", "new", "date_published", "2026-01-01T00:00:00Z"), PyDict.Of("id", "invalid", "date_published", "bad"));
        }, false);
        Assert.Single(calls); Assert.StartsWith("https://official.test", calls[0]);
        Assert.Equal("new", ((PyDict)rows[0])["id"]);
        Assert.Equal("官方实时列表", ((PyDict)rows[0])["version_source"]);
        Assert.Equal("invalid", ((PyDict)rows[2])["id"]);
    }

    [Fact]
    public void MirrorFallbackIsLabelledAndEmptyOfficialResultDoesNotDropFilters()
    {
        var rows = CatalogVersions.Read(new[] { "https://official.test", "https://mirror.test" }, "/versions", PyDict.Of("gameVersion", "1.21.1"), "fileDate", url =>
        {
            Assert.Contains("gameVersion=1.21.1", url);
            if (url.Contains("official")) throw new System.IO.IOException("offline");
            return PyDict.Of("data", Py.L(PyDict.Of("id", 2L, "fileDate", "2026-01-01")));
        }, true);
        Assert.NotEmpty(Py.S(((PyDict)rows[0])["version_warning"]));
        int calls = 0;
        Assert.Empty(CatalogVersions.Read(new[] { "https://official.test", "https://mirror.test" }, "/versions", new PyDict(), "fileDate", _ => { calls++; return PyDict.Of("data", Py.L()); }, true));
        Assert.Equal(1, calls);
    }
}
