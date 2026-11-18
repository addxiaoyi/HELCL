using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase3NetworkAndCatalogTests
{
    [Theory]
    [InlineData("§a[Hypixel] §6Welcome to §cSkyBlock!", "[Hypixel] Welcome to SkyBlock!")]
    [InlineData("§l§cWarning! §rServer restarts in 5m", "Warning! Server restarts in 5m")]
    [InlineData("Normal text without codes", "Normal text without codes")]
    [InlineData("", "")]
    public void MinecraftColorParser_StripsColorCodes(string raw, string expectedClean)
    {
        string clean = MinecraftColorParser.StripColorCodes(raw);
        Assert.Equal(expectedClean, clean);
    }

    [Fact]
    public void MinecraftColorParser_ParsesColorSegments()
    {
        string raw = "§aGreen §cRed §rReset";
        var segments = MinecraftColorParser.ParseColorRuns(raw);
        Assert.Equal(3, segments.Count);
        Assert.Equal("Green ", segments[0].Text);
        Assert.Equal("#55FF55", segments[0].HexColor);
        Assert.Equal("Red ", segments[1].Text);
        Assert.Equal("#FF5555", segments[1].HexColor);
        Assert.Equal("Reset", segments[2].Text);
        Assert.Equal("#FFFFFF", segments[2].HexColor);
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(25565, new byte[] { 0xDD, 0xC7, 0x01 })]
    public void VarInt_EncodesAndDecodesCorrectly(int value, byte[] expectedBytes)
    {
        byte[] encoded = MinecraftPingProtocol.EncodeVarInt(value);
        Assert.Equal(expectedBytes, encoded);

        using var ms = new MemoryStream(encoded);
        int decoded = MinecraftPingProtocol.DecodeVarInt(ms);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void DependencyResolver_FindsMissingRequiredMods()
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fabric-api", "cloth-config" };
        var deps = new List<ModDependency>
        {
            new ModDependency("fabric-api", isRequired: true),
            new ModDependency("sodium", isRequired: true),
            new ModDependency("iris", isRequired: false)
        };

        var missing = DependencyResolver.GetMissingRequired(installed, deps);
        Assert.Single(missing);
        Assert.Equal("sodium", missing[0].ModId);
    }
}
