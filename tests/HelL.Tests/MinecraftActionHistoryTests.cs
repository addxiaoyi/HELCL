using System;
using System.Linq;
using HelL.Core;
using HelL.Core.Launcher.Ai.Minecraft;
using Xunit;

namespace HelL.Tests;

public sealed class MinecraftActionHistoryTests
{
    private static PyDict Target => PyDict.Of("host", "127.0.0.1", "port", 25565L, "name", "HelLAI", "auth", "offline");

    [Fact]
    public void StopCannotBeOverwrittenByLateSuccess()
    {
        using var home = new TestHome();
        string id = MinecraftActionHistory.Begin("move_to", PyDict.Of("x", 5L), Target);
        MinecraftActionHistory.Interrupt(); MinecraftActionHistory.Finish(id, "completed", "late callback");
        var entry = (PyDict)MinecraftActionHistory.Snapshot().Single();
        Assert.Equal("stopped", entry.Get("state")); Assert.False(Py.Truthy(entry.Get("retryable")));
        Assert.False(string.IsNullOrEmpty(Py.S(entry.Get("ended_at"))));
    }

    [Theory]
    [InlineData("move_to", true)]
    [InlineData("look_at", true)]
    [InlineData("follow_player", true)]
    [InlineData("dig", false)]
    [InlineData("place", false)]
    [InlineData("craft", false)]
    public void OnlyFailedMovementCanBeReplayedAndOnlyInItsOwnWorld(string action, bool retryable)
    {
        using var home = new TestHome();
        string id = MinecraftActionHistory.Begin(action, new PyDict(), Target); MinecraftActionHistory.Finish(id, "failed", "fixture failure");
        if (!retryable) { Assert.Throws<InvalidOperationException>(() => MinecraftActionHistory.Retry(id, Target)); return; }
        Assert.Equal(id, MinecraftActionHistory.Retry(id, Target).Get("id"));
        var other = Target; other["port"] = 30000L;
        Assert.Throws<InvalidOperationException>(() => MinecraftActionHistory.Retry(id, other));
    }

    [Fact]
    public void RecordsSurviveDataDirectoryReloadWithoutStoringCredentials()
    {
        using var home = new TestHome(); var target = Target; target["token"] = "never-store-fixture";
        MinecraftActionHistory.Begin("dig", PyDict.Of("x", 1L, "token", "never-store-fixture"), target);
        using (var other = new TestHome()) Assert.Empty(MinecraftActionHistory.Snapshot());
        var entry = (PyDict)MinecraftActionHistory.Snapshot().Single();
        Assert.Equal("stopped", entry.Get("state")); Assert.DoesNotContain("never-store-fixture", PyJson.Dumps(entry));
    }
}
