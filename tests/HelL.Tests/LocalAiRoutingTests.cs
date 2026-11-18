using HelL.Core;
using HelL.Core.Launcher.Ai;
using Xunit;

namespace HelL.Tests;

public class LocalAiRoutingTests
{
    [Fact]
    public void LocalEndpointNeverUsesCloudCredentials()
    {
        var settings = PyDict.Of("ai_mode", "local", "ai_local_url", "http://127.0.0.1:11435",
            "ai_local_model", "qwen3:0.6b", "ai_model", "cloud-model", "ai_api_key", "cloud-secret");
        var endpoint = Client.ResolveEndpoint(settings);
        Assert.Equal("http://127.0.0.1:11435/v1/chat/completions", endpoint["url"]);
        Assert.Equal("qwen3:0.6b", endpoint["model"]);
        Assert.False(((PyDict)endpoint["headers"]).ContainsKey("Authorization"));
        Assert.Equal("qwen3:0.6b", Client.CurrentModel(settings));
    }

    [Fact]
    public void LocalEndpointRejectsRemoteHosts()
    {
        var settings = PyDict.Of("ai_mode", "local", "ai_local_url", "https://example.com", "ai_local_model", "qwen3:4b");
        Assert.ThrowsAny<System.Exception>(() => Client.ResolveEndpoint(settings));
    }

    [Fact]
    public void PublicGatewayHonorsTheSelectedModel()
    {
        var settings = PyDict.Of("ai_mode", "public", "ai_gateway_url", "https://gateway.example", "ai_model", "chosen-model");
        Assert.Equal("chosen-model", Client.CurrentModel(settings));
        Assert.Equal("chosen-model", Client.ResolveEndpoint(settings)["model"]);
    }
}
