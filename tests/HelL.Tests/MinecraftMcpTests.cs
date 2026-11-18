using System;
using System.Collections.Generic;
using HelL.Core;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Minecraft;
using Xunit;

namespace HelL.Tests;

public sealed class MinecraftMcpTests
{
    [Theory]
    [InlineData("https://example.com", 25565, "HelLAI")]
    [InlineData("localhost", 0, "HelLAI")]
    [InlineData("localhost", 25565, "bad;name")]
    public void ConnectionRejectsInvalidTargets(string host, int port, string name)
        => Assert.Throws<ArgumentException>(() => MinecraftOptions.Validate(host, port, name, "offline", ""));

    [Fact]
    public void ObservationAndWorldChangesUseDistinctPermissions()
    {
        var rules = Array.Empty<AiRule>();
        Assert.Equal("allow", Permission.Decide(ToolMeta.Get("mcp_minecraft_observe"), new PyDict(), "default", rules).decision);
        Assert.Equal("ask", Permission.Decide(ToolMeta.Get("mcp_minecraft_dig"), new PyDict(), "default", rules).decision);
        Assert.Equal("deny", Permission.Decide(ToolMeta.Get("mcp_minecraft_move_to"), new PyDict(), "plan", rules).decision);
        Assert.Null(MinecraftSession.Meta("mcp_minecraft_chat"));
        Assert.Null(MinecraftSession.Meta("mcp_minecraft_connect"));
    }

    [Fact]
    public void DisconnectRevokesToolsAndDoesNotExposeCredentials()
    {
        using var home = new TestHome();
        MinecraftSession.Stop();
        Assert.Empty(MinecraftSession.Schemas());
        Assert.Throws<McpError>(() => MinecraftSession.Call("mcp_minecraft_observe", new PyDict()));
        Assert.Throws<InvalidOperationException>(() => MinecraftSession.SetToolsEnabled(true));
    }

    [Fact]
    public void ReleaseEmbedsTheLockedMinecraftRuntime()
    {
        foreach (string file in new[] { "package.json", "package-lock.json", "dist/server.js", "dist/game.js", "dist/tools.js", "dist/activities.js", "dist/terrain.js" })
            Assert.False(string.IsNullOrWhiteSpace(CoreResources.ReadText("minecraft/" + file)));
        var manifest = PyJson.Loads(CoreResources.ReadText("minecraft/package.json")) as PyDict;
        Assert.Equal("4.39.0", ((PyDict)manifest["dependencies"])["mineflayer"]);
    }

    [Fact]
    public void ClearingBotLoginOnlyRemovesItsJsonCache()
    {
        using var home = new TestHome();
        MinecraftSession.Stop();
        System.IO.Directory.CreateDirectory(MinecraftRuntime.Profiles);
        string cached = System.IO.Path.Combine(MinecraftRuntime.Profiles, "test-live-cache.json");
        string unrelated = System.IO.Path.Combine(home.Dir, "accounts.json");
        System.IO.File.WriteAllText(cached, "{}"); System.IO.File.WriteAllText(unrelated, "{}");
        MinecraftRuntime.ClearLogin();
        Assert.False(System.IO.File.Exists(cached)); Assert.True(System.IO.File.Exists(unrelated));
    }

    [Fact]
    public void DedicatedRuntimeAndModelStorageCanUseAnotherLocalDrive()
    {
        using var home = new TestHome();
        string runtime = System.IO.Path.Combine(home.Dir, "game-runtime");
        string models = System.IO.Path.Combine(home.Dir, "game-models");
        HelL.Core.Launcher.Config.CONFIG.Set("minecraft_runtime_root", runtime);
        HelL.Core.Launcher.Config.CONFIG.Set("minecraft_model_root", models);
        Assert.StartsWith(runtime + System.IO.Path.DirectorySeparatorChar, MinecraftRuntime.DirectoryPath);
        Assert.Equal(models, MinecraftModels.Root);
    }
}
