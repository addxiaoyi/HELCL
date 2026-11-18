using System;
using System.Collections.Generic;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase6MultiplayerAndWizardTests
{
    [Theory]
    [InlineData(new[] { "192.168.1.10" }, 25565, "192.168.1.10:25565")]
    [InlineData(new[] { "192.168.1.10" }, 54321, "192.168.1.10:54321")]
    [InlineData(new[] { "2001:db8::1" }, 25565, "[2001:db8::1]:25565")]
    [InlineData(new[] { "169.254.1.1", "192.168.1.20" }, 25565, "192.168.1.20:25565")]
    public void FormatLanAddresses_FormatsIPv4AndIPv6WithPort(string[] ips, int port, string expectedFirst)
    {
        var formatted = MultiplayerAndDialogUtils.FormatLanAddresses(ips, port);
        Assert.NotEmpty(formatted);
        Assert.Equal(expectedFirst, formatted[0]);
    }

    [Theory]
    [InlineData("  U/ABCD-1234-EFGH-5678  ", "U/ABCD-1234-EFGH-5678")]
    [InlineData("room: U/ABCD-1234-EFGH-5678", "U/ABCD-1234-EFGH-5678")]
    [InlineData("ABCD-1234-EFGH-5678", "U/ABCD-1234-EFGH-5678")]
    [InlineData("https://hell.launcher/join/U/ABCD-1234-EFGH-5678", "U/ABCD-1234-EFGH-5678")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void NormalizeRoomCode_ExtractsStandardRoomCode(string input, string expected)
    {
        string result = MultiplayerAndDialogUtils.NormalizeRoomCode(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("jei-1.20.1-forge-15.3.0.4.jar", "jei-1.20.1-forge-15.3.0.8.jar", ModVersionRelation.Newer)]
    [InlineData("cloth-config-11.1.118-fabric.jar", "cloth-config-11.1.118-fabric.jar", ModVersionRelation.Same)]
    [InlineData("sodium-fabric-0.5.8+mc1.20.4.jar", "sodium-fabric-0.5.3+mc1.20.4.jar", ModVersionRelation.Older)]
    public void CompareModVersions_IdentifiesUpgradeOrSame(string currentFile, string newFile, ModVersionRelation expected)
    {
        var relation = MultiplayerAndDialogUtils.CompareModVersions(currentFile, newFile);
        Assert.Equal(expected, relation);
    }

    [Theory]
    [InlineData(1000, 100, 2000, 100, SyncDirection.Pull)] // remote newer
    [InlineData(2000, 100, 1000, 100, SyncDirection.Push)] // local newer
    [InlineData(1000, 100, 1000, 100, SyncDirection.Identical)] // equal
    [InlineData(2000, 200, 2000, 100, SyncDirection.Conflict)] // same time, different size
    public void EvaluateSyncAction_DeterminesPushPullOrConflict(long localTime, long localSize, long remoteTime, long remoteSize, SyncDirection expected)
    {
        var action = MultiplayerAndDialogUtils.EvaluateSyncAction(localTime, localSize, remoteTime, remoteSize);
        Assert.Equal(expected, action);
    }

    [Fact]
    public void BatchEvaluateImport_CalculatesVersionStatistics()
    {
        var existing = new[] { "jei-1.20.1-15.3.0.4.jar", "cloth-config-11.1.118-fabric.jar" };
        var incoming = new[] { "jei-1.20.1-15.3.0.8.jar", "cloth-config-11.1.118-fabric.jar", "new-mod-1.0.jar" };

        var (newer, same, older) = MultiplayerAndDialogUtils.BatchEvaluateImport(existing, incoming);
        Assert.Equal(1, newer);
        Assert.Equal(1, same);
        Assert.Equal(0, older);
    }

    [Fact]
    public void DetectKnownLauncherProfiles_ReturnsStandardCandidates()
    {
        var candidates = MultiplayerAndDialogUtils.GetKnownLauncherCandidatePaths();
        Assert.NotNull(candidates);
        Assert.Contains(candidates, c => c.Name.Contains("PCL2") || c.Name.Contains("HMCL") || c.Name.Contains("Official"));
    }
}
