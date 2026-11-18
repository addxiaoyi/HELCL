using System.Collections.Generic;
using HelL.Services;
using Xunit;

namespace HelL.Tests;

public sealed class DownloadRetryTests
{
    [Fact]
    public void RetryFreezesTheOriginalTargetAndExcludesDestructiveOrAuthenticatedOperations()
    {
        var args = new Dictionary<string, object> { ["instance"] = "original", ["version"] = "1.21.1" };
        DownloadRetries.Register("fixture", "install_game", args); args["instance"] = "changed";
        using var saved = System.Text.Json.JsonDocument.Parse(DownloadRetries.Find("fixture").ArgumentsJson);
        Assert.Equal("original", saved.RootElement.GetProperty("instance").GetString());
        foreach (string method in new[] { "launch_game", "delete_instance", "install_world", "install_modpack", "login_microsoft" })
        {
            DownloadRetries.Register(method, method, new { token = "never-store" }); Assert.Null(DownloadRetries.Find(method));
        }
        DownloadRetries.Remove("fixture"); Assert.Null(DownloadRetries.Find("fixture"));
    }
}
