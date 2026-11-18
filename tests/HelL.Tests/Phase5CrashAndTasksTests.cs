using System;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase5CrashAndTasksTests
{
    [Theory]
    [InlineData(@"C:\Users\Administrator\AppData\Roaming\.minecraft\crash-reports\crash.txt", @"C:\Users\***\AppData\Roaming\.minecraft\crash-reports\crash.txt")]
    [InlineData(@"D:\Users\John_Doe\Desktop\client.log", @"D:\Users\***\Desktop\client.log")]
    [InlineData(@"Connecting to internal server 192.168.1.100:25565", @"Connecting to internal server 192.168.*.*:25565")]
    [InlineData(@"No sensitive info here", @"No sensitive info here")]
    public void PrivacyMasker_MasksSensitivePathAndIp(string input, string expected)
    {
        string masked = PrivacyMasker.Mask(input);
        Assert.Equal(expected, masked);
    }

    [Fact]
    public void CrashLogExpert_IdentifiesOutOfMemory()
    {
        string stackTrace = @"
java.lang.OutOfMemoryError: Java heap space
    at java.util.Arrays.copyOf(Arrays.java:3210)
    at net.minecraft.client.renderer.texture.TextureAtlas.load(TextureAtlas.java:120)
";
        var diagnosis = CrashLogExpert.Analyze(stackTrace, -1);
        Assert.True(diagnosis.IsIdentified);
        Assert.Equal(CrashIssueType.OutOfMemory, diagnosis.IssueType);
        Assert.Contains("内存不足", diagnosis.Reason);
        Assert.Contains("启动设置", diagnosis.Recommendation);
    }

    [Fact]
    public void CrashLogExpert_IdentifiesCulpritMod()
    {
        string stackTrace = @"
org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError: An unexpected critical error was encountered
Caused by: java.lang.NullPointerException: Registry Object not found
    at com.example.create_custom_blocks.Init.register(Init.java:45)
    at net.minecraftforge.eventbus.ASMEventHandler.invoke(ASMEventHandler.java:73)
    Suppressed: -- Mod File: /mods/create_custom_blocks-1.20.1-1.0.jar
";
        var diagnosis = CrashLogExpert.Analyze(stackTrace, 1);
        Assert.True(diagnosis.IsIdentified);
        Assert.Equal(CrashIssueType.ModConflictOrError, diagnosis.IssueType);
        Assert.Equal("create_custom_blocks-1.20.1-1.0.jar", diagnosis.SuspectedMod);
    }

    [Fact]
    public void CrashLogExpert_IdentifiesGraphicsDriverCrash()
    {
        string stackTrace = @"
# Problematic frame:
# C  [nvoglv64.dll+0x9bf800]
#
# An error occurred while executing the native library
";
        var diagnosis = CrashLogExpert.Analyze(stackTrace, unchecked((int)0xC0000005));
        Assert.True(diagnosis.IsIdentified);
        Assert.Equal(CrashIssueType.GraphicsDriver, diagnosis.IssueType);
        Assert.Contains("显卡驱动", diagnosis.Reason);
    }

    [Theory]
    [InlineData(500, 1000, 100.0, 5, "5秒")]
    [InlineData(1000, 1000, 100.0, 0, "完成")]
    [InlineData(0, 1000, 0.0, -1, "计算中...")]
    [InlineData(0, 10000, 100.0, 100, "1分40秒")]
    public void TaskProgressHelper_CalculatesEtaAccurately(long cur, long total, double bps, int expectedSecs, string expectedText)
    {
        var eta = TaskProgressHelper.CalculateEta(cur, total, bps);
        Assert.Equal(expectedSecs, eta.RemainingSeconds);
        Assert.Equal(expectedText, eta.FormattedRemaining);
    }

    [Fact]
    public void TaskProgressHelper_FormatsTransferStatus()
    {
        long cur = 10 * 1024 * 1024; // 10 MB
        long total = 20 * 1024 * 1024; // 20 MB
        double bps = 2 * 1024 * 1024; // 2 MB/s

        string status = TaskProgressHelper.FormatStatus(cur, total, bps);
        Assert.Contains("10.0 MB / 20.0 MB", status);
        Assert.Contains("2.0 MB/s", status);
        Assert.Contains("剩余 5秒", status);
    }

    [Theory]
    [InlineData(0, 0, "小时", "0分钟")]
    [InlineData(1800, 0.5, "小时", "30分钟")]
    [InlineData(3600, 1.0, "小时", "1小时")]
    [InlineData(7320, 2.0, "小时", "2小时 2分钟")]
    public void PlaytimeFormatter_FormatsUnitsAndSubtext(long secs, double expectedHours, string expectedUnit, string expectedSub)
    {
        var result = PlaytimeFormatter.Format(secs);
        Assert.Equal(expectedHours, result.ValueHours, precision: 1);
        Assert.Equal(expectedUnit, result.Unit);
        Assert.Equal(expectedSub, result.Subtext);
    }
}
