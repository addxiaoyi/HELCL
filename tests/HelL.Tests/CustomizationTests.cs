using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class CustomizationTests
{
    [Fact]
    public void EmptyCustomLayoutIsPreserved()
    {
        var doc = UiLayout.ParseDoc(PyDict.Of("items", new List<object>(), "grid", 8L));
        Assert.NotNull(doc); Assert.Empty(doc.items);
    }

    [Fact]
    public void InvalidGeometryCannotEscapeTheCanvasOrCrashWpf()
    {
        var item = new LayoutItem("notes", double.NaN, double.PositiveInfinity, double.NaN, -2);
        Assert.True(double.IsFinite(item.x) && double.IsFinite(item.y) && double.IsFinite(item.w) && double.IsFinite(item.h));
        Assert.True(item.x >= 0 && item.w > 0 && item.x + item.w <= 1);
    }

    [Theory]
    [InlineData(280)]
    [InlineData(620)]
    [InlineData(940)]
    [InlineData(1500)]
    public void ResponsiveProjectionHasNoOverlapAndPreservesSavedCoordinates(double width)
    {
        var doc = UiLayout.DefaultDoc(); string before = PyJson.Dumps(doc.ToDict());
        var boxes = DashboardGeometry.Arrange(doc.items, width, type => { var min = UiLayout.MinSizeFor(type); return (min.w, min.h); });
        Assert.Equal(before, PyJson.Dumps(doc.ToDict()));
        Assert.All(boxes, box => { Assert.True(box.X >= 0 && box.Right <= width + .01); Assert.True(box.Height >= 96); });
        foreach (var a in boxes) foreach (var b in boxes.Where(b => b.Id != a.Id)) Assert.False(a.Overlaps(b, 0));
    }
    [Fact]
    public void MagneticSnapAlignsEdgesAndSpacingButCanBeDisabled()
    {
        var neighbour = new DashboardBox("left", 20, 40, 200, 150);
        var moved = new DashboardBox("right", 235, 43, 180, 130);
        var snapped = DashboardGeometry.Snap(moved, new[] { neighbour }, 900, 8, "move", out var x, out var y);
        Assert.Equal(232, snapped.X); Assert.Equal(40, snapped.Y); Assert.NotNull(x); Assert.NotNull(y);
        var free = DashboardGeometry.Snap(moved, new[] { neighbour }, 900, 0, "move", out x, out y);
        Assert.Equal(235, free.X); Assert.Equal(43, free.Y); Assert.Null(x); Assert.Null(y);
    }
    [Fact]
    public void DraggedModuleKeepsItsPositionAndPushesNeighboursDown()
    {
        var cards = new[] { new LayoutItem("notes", 0, 0, .5, .2, "first"), new LayoutItem("notes", .1, .02, .5, .2, "active") };
        var boxes = DashboardGeometry.Arrange(cards, 1000, _ => (180, 130), "active");
        Assert.Equal(16, boxes.Single(box => box.Id == "active").Y);
        Assert.True(boxes.Single(box => box.Id == "first").Y >= 188);
    }
    [Fact]
    public void PluginModulesAndIndependentSettingsSurviveLayoutRoundTrip()
    {
        var doc = new LayoutDoc(new List<LayoutItem> {
            new("plugin:world-journal:main", 0, 1.8, .4, .2, "journal-a", settings: PyDict.Of("text", "基地 32 64 12")),
            new("notes", .5, 2.1, .4, .3, "journal-b", settings: PyDict.Of("text", "矿洞")) });
        var parsed = UiLayout.ParseDoc(PyJson.Loads(PyJson.Dumps(doc.ToDict())));
        Assert.Equal(2, parsed.items.Count); Assert.Equal(1.8, parsed.items[0].y); Assert.Equal("基地 32 64 12", parsed.items[0].settings.Get("text"));
        Assert.Equal("矿洞", parsed.items[1].settings.Get("text"));
    }
}
