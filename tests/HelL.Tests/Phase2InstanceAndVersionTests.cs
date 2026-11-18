using System;
using System.Collections.Generic;
using Xunit;
using HelL.Services;

namespace HelL.Tests;

public class Phase2InstanceAndVersionTests
{
    [Theory]
    [InlineData("生电测试", "sdcs", true)]
    [InlineData("生电测试", "生电", true)]
    [InlineData("红石机械", "hs", true)]
    [InlineData("Pure Vanilla 1.20", "vanilla", true)]
    [InlineData("Pure Vanilla 1.20", "pv", true)]
    [InlineData("纯净整合", "abc", false)]
    public void PinyinMatcher_MatchesPinyinInitialsAndSubstrings(string name, string query, bool expectedMatch)
    {
        bool matched = InstanceUtils.MatchesPinyinOrSubstr(name, query);
        Assert.Equal(expectedMatch, matched);
    }

    [Theory]
    [InlineData("1.20.1", "1.20")]
    [InlineData("1.20.4-fabric", "1.20")]
    [InlineData("1.21.2", "1.21")]
    [InlineData("1.7.10", "1.7")]
    [InlineData("26.3", "26.3")]
    [InlineData("26.1.2", "26.1")]
    public void VersionGrouping_ExtractsMajorMinorKey(string versionId, string expectedGroup)
    {
        string group = InstanceUtils.ExtractVersionGroup(versionId);
        Assert.Equal(expectedGroup, group);
    }

    [Theory]
    [InlineData("ValidName", true)]
    [InlineData("1.20 生电整合", true)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("name:test", false)]
    [InlineData("pack*", false)]
    [InlineData("CON", false)]
    [InlineData("PRN", false)]
    [InlineData("   ", false)]
    [InlineData("", false)]
    public void ValidateInstanceName_RejectsInvalidWindowsCharsAndReservedNames(string name, bool expectedValid)
    {
        bool valid = InstanceUtils.IsValidInstanceName(name);
        Assert.Equal(expectedValid, valid);
    }

    [Fact]
    public void GeneratePreRestoreBackupName_ContainsOriginalNameAndTimestamp()
    {
        string original = "MySave";
        string backup = InstanceUtils.GeneratePreRestoreBackupName(original);
        Assert.StartsWith("MySave_prerestore_", backup);
        Assert.EndsWith(".zip", backup);
    }
}
