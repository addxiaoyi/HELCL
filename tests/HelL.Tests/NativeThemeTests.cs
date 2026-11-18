using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class NativeThemeTests
{
    [Fact]
    public void FreshProfilesUseTheDarkThemeWithoutChangingPersistedChoices()
    {
        using var home = new TestHome();
        Assert.True(Py.Truthy(Config.CONFIG.Get("ui_dark")));
        Assert.Equal("#A99AFF", Config.CONFIG.Get("theme_color"));
        Assert.Equal(0L, Config.CONFIG.Get("ui_glass_transparency"));
        Assert.Equal(0L, Config.CONFIG.Get("ui_glass_frost"));

        Config.CONFIG.Update(PyDict.Of("ui_dark", false, "theme_color", "#2E9B6B",
            "ui_glass_transparency", 100L, "ui_glass_frost", 30L));
        Config.CONFIG.Save();
        Config.Reset();

        Assert.False(Py.Truthy(Config.CONFIG.Get("ui_dark")));
        Assert.Equal("#2E9B6B", Config.CONFIG.Get("theme_color"));
        Assert.Equal(100L, Config.CONFIG.Get("ui_glass_transparency"));
        Assert.Equal(30L, Config.CONFIG.Get("ui_glass_frost"));
        Assert.False(JsonSerializer.Deserialize<UiPrefs>("{\"Dark\":false}").Dark);
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void DefaultTextAndPrimaryActionsHaveReadableContrast(string mode)
    {
        var palette = ReadPalette(mode);
        foreach (string surface in new[] { "B.Canvas", "B.Paper" })
            foreach (string ink in new[] { "B.Ink", "B.InkMuted", "B.InkFaint", "B.AccentText" })
                Assert.True(Contrast(palette[ink], palette[surface]) >= 4.5,
                    $"{mode}: {ink} must remain readable on {surface}.");
        Assert.True(Contrast(palette["B.OnAccentStrong"], palette["B.AccentStrong"]) >= 4.5);
    }

    private static Dictionary<string, string> ReadPalette(string mode)
    {
        var document = XDocument.Load(Path.Combine(TestPaths.Root(), "src", "HelL", "Themes", $"Palette.{mode}.xaml"));
        XNamespace key = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root.Elements().Where(entry => entry.Name.LocalName == "SolidColorBrush")
            .ToDictionary(entry => (string)entry.Attribute(key + "Key"), entry => (string)entry.Attribute("Color"));
    }

    private static double Contrast(string first, string second)
    {
        double light = Luminance(first), dark = Luminance(second);
        return (Math.Max(light, dark) + .05) / (Math.Min(light, dark) + .05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            double value = byte.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber) / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(1) + .7152 * Channel(3) + .0722 * Channel(5);
    }
}
