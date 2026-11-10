using HelL.Core.Launcher;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class CredentialDefaultsTests
{
    [Fact]
    public void NoEmbeddedCredentialAndExplicitUserKeysArePreserved()
    {
        Assert.Equal("", Config.BuiltinCurseforgeKey());
        Assert.False(LegacyCredential.Matches("ai_api_key", "user-configured-test-key"));
        using var home = new HelL.Tests.TestHome();
        Config.CONFIG.Set("ai_api_key", "user-configured-test-key");
        Config.CONFIG.Set("curseforge_api_key", "user-cf-test-key");
        Config.CONFIG.Save();
        Config.Reset();
        Assert.Equal("user-configured-test-key", Config.CONFIG.Get("ai_api_key"));
        Assert.Equal("user-cf-test-key", Config.CurseforgeKey());
    }
}
