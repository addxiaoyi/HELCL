using System;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai.Minecraft;
using Xunit;

namespace HelL.Tests;

public sealed class MinecraftExtensionTests
{
    [Fact]
    public void IntegrationsAndAutomaticModelStartupAreOptIn()
    {
        using var home = new TestHome();
        Config.CONFIG.Set("minecraft_model_enabled", true);
        Assert.False(MinecraftExtensions.FeiyuEnabled);
        Assert.False(MinecraftExtensions.ModelAutoStart);
        var error = Assert.Throws<InvalidOperationException>(MinecraftSession.AllowFeiyu);
        Assert.Contains("拓展", error.Message);
    }

    [Fact]
    public void EnablingAnExtensionOnlySavesThePreference()
    {
        using var home = new TestHome();
        MinecraftSession.Stop();
        MinecraftExtensions.Set("feiyu", true);
        MinecraftExtensions.Set("model_auto_start", true);
        Assert.True(MinecraftExtensions.FeiyuEnabled);
        Assert.True(MinecraftExtensions.ModelAutoStart);
        Assert.False(MinecraftSession.Running);
        Assert.Empty(MinecraftSession.Schemas());
        Assert.False(Py.Truthy(MinecraftModels.Status().Get("ready")));
        Config.Reset();
        Assert.True(MinecraftExtensions.FeiyuEnabled);
        Assert.True(MinecraftExtensions.ModelAutoStart);
        MinecraftExtensions.Set("feiyu", false);
        Assert.False(MinecraftExtensions.FeiyuEnabled);
    }

    [Fact]
    public void UnknownExtensionCannotChangePreferences()
    {
        using var home = new TestHome();
        Assert.Throws<ArgumentException>(() => MinecraftExtensions.Set("unknown", true));
        Assert.False(MinecraftExtensions.FeiyuEnabled);
        Assert.False(MinecraftExtensions.ModelAutoStart);
    }
}
