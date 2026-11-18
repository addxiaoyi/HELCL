using System.Collections.Generic;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class DashboardLibraryTests
{
    [Fact]
    public void RecentlyPlayedInstalledVersionComesFirstAndRemovedVersionsCannotLaunch()
    {
        var history = PyDict.Of("survival", PyDict.Of("sessions", new List<object> {
            PyDict.Of("version", "1.21.1", "start", 100L, "duration", 30L),
            PyDict.Of("version", "removed", "start", 400L, "duration", 30L) }),
            "creative", PyDict.Of("sessions", new List<object> { PyDict.Of("version", "1.20.4", "start", 200L, "duration", 20L) }));
        var installed = new Dictionary<string, List<string>> { ["empty"] = new(), ["survival"] = new() { "1.21.1", "1.21.2" }, ["creative"] = new() { "1.20.4" } };
        var rows = DashboardLibrary.Select(history, installed, 500);
        Assert.Equal(new[] { "creative", "survival", "empty" }, rows.Select(row => row.Instance));
        Assert.Equal("1.21.1", rows[1].Version); Assert.Equal(130, rows[1].LastPlayed); Assert.Empty(rows[2].Version);
    }
    [Theory]
    [InlineData("standard")]
    [InlineData("focus")]
    [InlineData("studio")]
    public void EveryPresetHasUniqueKnownModulesAndLaunchControls(string name)
    {
        var doc = DashboardPresets.Create(name);
        Assert.Contains(doc.items, card => card.type == "banner"); Assert.Contains(doc.items, card => card.type == "config");
        Assert.Equal(doc.items.Count, doc.items.Select(card => card.id).Distinct().Count());
        Assert.All(doc.items, card => Assert.True(UiLayout.IsKnownCard(card.type)));
        foreach (double width in new[] { 520d, 920d, 1400d })
        {
            var boxes = DashboardGeometry.Arrange(doc.items, width, type => { var min = UiLayout.MinSizeFor(type); return (min.w, min.h); });
            foreach (var box in boxes) foreach (var other in boxes.Where(other => other.Id != box.Id)) Assert.False(box.Overlaps(other, 0));
        }
    }
}
