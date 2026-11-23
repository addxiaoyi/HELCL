using HelL.Core.Launcher;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class DownloadCredentialTests
{
    [Theory]
    [InlineData("https://api.curseforge.com/v1/mods/123")]
    [InlineData("https://API.CURSEFORGE.COM:443/v1/mods/123")]
    public void OfficialHttpsApiReceivesTheConfiguredKey(string url)
    {
        using var home = new TestHome();
        Config.CONFIG.Set("curseforge_api_key", "fixture-only-cf-key");
        Assert.Equal("fixture-only-cf-key", new DownloadManager().ExtraHeaders(url)["x-api-key"]);
    }

    [Theory]
    [InlineData("https://untrusted.invalid/api.curseforge.com")]
    [InlineData("https://untrusted.invalid/?next=https://api.curseforge.com/v1/mods")]
    [InlineData("https://untrusted.invalid/curseforge/v1/mods")]
    [InlineData("https://api.curseforge.com.untrusted.invalid/v1/mods")]
    [InlineData("https://sub.api.curseforge.com/v1/mods")]
    [InlineData("https://api.curseforge.com@untrusted.invalid/v1/mods")]
    [InlineData("https://user@api.curseforge.com/v1/mods")]
    [InlineData("http://api.curseforge.com/v1/mods")]
    [InlineData("https://api.curseforge.com:8443/v1/mods")]
    [InlineData("https://api.curseforge.com/files/mod.jar")]
    [InlineData("https://mod.mcimirror.top/curseforge/v1/mods")]
    [InlineData("/curseforge/v1/mods")]
    public void UntrustedOrNonApiTargetsNeverReceiveTheKey(string url)
    {
        using var home = new TestHome();
        Config.CONFIG.Set("curseforge_api_key", "fixture-only-cf-key");
        Assert.Empty(new DownloadManager().ExtraHeaders(url));
    }
}
