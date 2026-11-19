using System.Text.Json;
using HelL.Remote.Stress;
using Xunit;

namespace HelL.Remote.Stress.Tests;

public sealed class StressSafeguardTests
{
    [Fact]
    public void DefaultsAreFiniteAndShort()
    {
        StressOptions options = StressOptions.Parse([]);
        Assert.Equal(3, options.Cycles);
        Assert.Equal(1, options.Pairs);
        Assert.Equal(TimeSpan.Zero, options.Period);
        Assert.Equal(20261118, options.Seed);
    }

    [Fact]
    public void ALongDurationIsPacedAcrossAnExplicitFiniteCycleCount()
    {
        StressOptions options = StressOptions.Parse(["--cycles", "180", "--pairs", "2", "--duration", "03:00:00", "--seed", "42"]);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Period);
        Assert.Equal(42, options.Seed);
    }

    [Theory]
    [InlineData("--cycles", "0")]
    [InlineData("--pairs", "17")]
    [InlineData("--cycles", "1001")]
    [InlineData("--timeout", "00:00:01")]
    [InlineData("--duration", "-00:00:01")]
    public void InvalidPressureBudgetsAreRejected(string flag, string value)
        => Assert.ThrowsAny<ArgumentException>(() => StressOptions.Parse([flag, value]));

    [Fact]
    public void IntentionalTombstoneCapacityCannotBeSilentlyExceeded()
        => Assert.Throws<ArgumentOutOfRangeException>(() => StressOptions.Parse(["--cycles", "200", "--pairs", "8"]));

    [Fact]
    public void DurationAndPeriodCannotConflict()
        => Assert.Throws<ArgumentException>(() => StressOptions.Parse(["--duration", "00:01:00", "--period", "00:00:01"]));

    [Fact]
    public void WorkspaceCleanupRefusesAMissingOwnershipMarker()
    {
        var workspace = new OwnedWorkspace();
        string marker = Path.Combine(workspace.Root, ".owner");
        string owner = File.ReadAllText(marker);
        try
        {
            File.WriteAllText(marker, "another-run");
            Assert.Throws<InvalidOperationException>(workspace.Remove);
            Assert.True(Directory.Exists(workspace.Root));
        }
        finally { File.WriteAllText(marker, owner); workspace.Remove(); }
        Assert.False(Directory.Exists(workspace.Root));
    }

    [Fact]
    public void ErrorReportsNeverSerializeAnExceptionCredentialPayload()
    {
        const string credential = "fictional-private-token-must-not-be-logged";
        StressError failure = StressError.From(new IOException(credential), 1, "snapshot");
        string json = JsonSerializer.Serialize(failure);
        Assert.DoesNotContain(credential, json);
        Assert.Equal(nameof(IOException), failure.Type);
        Assert.Equal("snapshot", failure.Stage);
    }

    [Fact]
    public async Task AnAlreadyCancelledRunStillCleansItsOwnedFixtureAndWritesAnHonestReceipt()
    {
        string report = Path.Combine(Path.GetTempPath(), "hell-stress-cancel-" + Guid.NewGuid().ToString("N") + ".json");
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        try
        {
            StressReport outcome = await new StressRun(StressOptions.Parse(["--cycles", "1", "--report", report])).RunAsync(stopped.Token);
            Assert.Equal("cancelled", outcome.Status);
            Assert.Equal(0, outcome.CompletedCycles);
            Assert.True(outcome.HostSessionsStopped && outcome.RelayStopped && outcome.OwnedWorkspaceRemoved);
            Assert.Equal(0, outcome.FinalSecretEntries);
            Assert.Equal(0, outcome.FinalFixtureTasks);
            Assert.True(File.Exists(report));
        }
        finally { File.Delete(report); }
    }
}
