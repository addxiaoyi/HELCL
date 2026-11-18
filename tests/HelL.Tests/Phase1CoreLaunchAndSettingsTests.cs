using System;
using System.Collections.Generic;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase1CoreLaunchAndSettingsTests
{
    [Theory]
    [InlineData("127.0.0.1:25565", "127.0.0.1", "25565")]
    [InlineData("127.0.0.1", "127.0.0.1", "25565")]
    [InlineData("[2001:db8::1]:25565", "2001:db8::1", "25565")]
    [InlineData("[2001:db8::1]", "2001:db8::1", "25565")]
    [InlineData("mc.hypixel.net:25566", "mc.hypixel.net", "25566")]
    [InlineData("mc.hypixel.net", "mc.hypixel.net", "25565")]
    public void ParseServerAddress_HandlesIPv4IPv6AndDefaultPort(string input, string expectedHost, string expectedPort)
    {
        var (host, port) = LaunchUtils.ParseServerAddress(input);
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Fact]
    public void ExpandPlaceholders_SubstitutesGameDirAndVersionId()
    {
        string template = "run.bat --dir \"{game_directory}\" --ver \"{version_id}\"";
        string expanded = LaunchUtils.ExpandPlaceholders(template, "D:\\games\\mc", "1.20.1-fabric");
        Assert.Equal("run.bat --dir \"D:\\games\\mc\" --ver \"1.20.1-fabric\"", expanded);
    }

    [Theory]
    [InlineData(8, "ZGC", false)]
    [InlineData(8, "G1GC", true)]
    [InlineData(11, "ZGC", false)]
    [InlineData(17, "ZGC", true)]
    [InlineData(21, "ZGC", true)]
    [InlineData(25, "Shenandoah", true)]
    public void GcCompatibility_ValidatesJavaVersionSupport(int javaMajor, string gcPreset, bool expectedSupported)
    {
        bool supported = LaunchUtils.IsGcSupportedForJava(javaMajor, gcPreset);
        Assert.Equal(expectedSupported, supported);
    }

    [Fact]
    public void AikarFlags_GeneratesExpectedOptimalParameters()
    {
        string flags = LaunchUtils.GetAikarOptimizedFlags(8192);
        Assert.Contains("-XX:+UseG1GC", flags);
        Assert.Contains("-XX:G1NewSizePercent=", flags);
        Assert.Contains("-XX:InitiatingHeapOccupancyPercent=", flags);
    }

    [Fact]
    public void LogBuffer_CapsCapacityAndAppendsWithoutFullJoin()
    {
        var buffer = new VirtualLogBuffer(maxLines: 500);
        for (int i = 0; i < 600; i++)
        {
            buffer.AppendLine($"[INFO] Log line {i}");
        }
        Assert.Equal(500, buffer.LineCount);
        Assert.StartsWith("[INFO] Log line 100", buffer.GetLine(0));
        Assert.EndsWith("[INFO] Log line 599", buffer.GetLine(499));
    }
}
