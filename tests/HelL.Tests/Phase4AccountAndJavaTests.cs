using System;
using System.Collections.Generic;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase4AccountAndJavaTests
{
    [Theory]
    [InlineData("authlib-injector:yggdrasil:https://skin.example.com/api/yggdrasil", "https://skin.example.com/api/yggdrasil")]
    [InlineData("authlib-injector:https://littleskin.cn/api/yggdrasil", "https://littleskin.cn/api/yggdrasil")]
    [InlineData("https://myauth.com/api", "https://myauth.com/api")]
    [InlineData("   https://trimmed.com/api   ", "https://trimmed.com/api")]
    public void AuthlibUrlParser_ExtractsCleanHttpUrl(string raw, string expectedClean)
    {
        string clean = AuthlibUrlParser.NormalizeAuthlibUrl(raw);
        Assert.Equal(expectedClean, clean);
    }

    [Theory]
    [InlineData("1.20.5", 8, true, false)] // Java 8 cannot run MC 1.20.5
    [InlineData("1.20.5", 21, true, true)] // Java 21 can run MC 1.20.5
    [InlineData("1.16.5", 8, true, true)]  // Java 8 64-bit runs 1.16.5 fine
    [InlineData("1.18.2", 8, true, false)] // MC 1.18 needs Java 17
    [InlineData("1.18.2", 17, true, true)]
    [InlineData("26.3", 17, true, false)]  // MC 26 needs Java 25
    [InlineData("26.3", 25, true, true)]
    public void JavaEnvDetector_ValidatesCompatibility(string mcVersion, int javaMajor, bool is64Bit, bool expectedCompatible)
    {
        var result = JavaEnvDetector.CheckCompatibility(mcVersion, javaMajor, is64Bit);
        Assert.Equal(expectedCompatible, result.IsCompatible);
    }

    [Fact]
    public void JavaEnvDetector_WarnsOn32Bit()
    {
        var result = JavaEnvDetector.CheckCompatibility("1.12.2", 8, is64Bit: false);
        Assert.True(result.IsCompatible);
        Assert.True(result.HasWarning);
        Assert.Contains("32", result.WarningMessage);
    }

    [Fact]
    public void SkinModelParts_TogglesLayersIndependently()
    {
        var parts = new SkinLayerToggles();
        Assert.True(parts.Hat);
        Assert.True(parts.Jacket);
        Assert.True(parts.Cape);

        parts.Hat = false;
        Assert.False(parts.Hat);
        Assert.True(parts.Jacket);
    }
}
