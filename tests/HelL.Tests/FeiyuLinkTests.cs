using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Minecraft;
using Xunit;

namespace HelL.Tests;

public sealed class FeiyuLinkTests
{
    [Fact]
    public async Task ScopedBridgeRejectsUntrustedRequestsAndRemovesItsDiscovery()
    {
        using var home = new TestHome();
        string previous = Environment.GetEnvironmentVariable("HELL_FEIYU_LINK");
        string file = Path.Combine(home.Dir, "link.json");
        Environment.SetEnvironmentVariable("HELL_FEIYU_LINK", file);
        try
        {
            FeiyuLink.Open();
            var link = (PyDict)Utils.ReadJson(file);
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            string url = Py.S(link.Get("endpoint")) + "/status";
            Assert.Equal(403, (int)(await http.GetAsync(url)).StatusCode);
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + Py.S(link.Get("token")));
            http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://example.com");
            Assert.Equal(403, (int)(await http.GetAsync(url)).StatusCode);
            http.DefaultRequestHeaders.Remove("Origin");
            var response = (PyDict)PyJson.Loads(await http.GetStringAsync(url));
            Assert.False(((PyDict)response.Get("data")).ContainsKey("login"));
            Assert.False(((PyDict)response.Get("data")).ContainsKey("options"));
            using var body = new StringContent("{\"name\":\"chat\",\"arguments\":{}}", System.Text.Encoding.UTF8, "application/json");
            Assert.Equal(400, (int)(await http.PostAsync(Py.S(link.Get("endpoint")) + "/action", body)).StatusCode);
            FeiyuLink.Close(); Assert.False(File.Exists(file));
        }
        finally { FeiyuLink.Close(); Environment.SetEnvironmentVariable("HELL_FEIYU_LINK", previous); }
    }

    [Fact]
    public void TreeHarvestKeepsDeletePermissionAndNoDisconnectedDelegation()
    {
        Assert.Equal("delete", MinecraftSession.Meta("mcp_minecraft_collect_wood").side_effect);
        Assert.Equal("deny", Permission.Decide(MinecraftSession.Meta("mcp_minecraft_collect_wood"), new PyDict(), "plan", Array.Empty<AiRule>()).decision);
        MinecraftSession.Stop();
        Assert.Throws<InvalidOperationException>(MinecraftSession.AllowFeiyu);
    }
}
