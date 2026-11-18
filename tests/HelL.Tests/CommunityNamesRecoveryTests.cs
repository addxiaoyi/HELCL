using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class CommunityNamesRecoveryTests
{
    [Fact]
    public void OfflineRefreshKeepsDiskCacheAndReconnectReplacesIt()
    {
        using var home = new TestHome();
        using var network = new IndexServer();
        CommunityNames.Ensure(true, network);
        string file = Path.Combine(home.Dir, "cache", "resource-names", "mod.csv");
        string before = File.ReadAllText(file);
        network.Offline = true;
        CommunityNames.Ensure(true, network);
        Assert.NotEmpty(CommunityNames.Entries);
        Assert.NotEmpty(CommunityNames.LastError);
        Assert.Equal(before, File.ReadAllText(file));
        Utils.SetRoot(Path.Combine(home.Dir, "empty"));
        CommunityNames.Ensure(true, network);
        Assert.Empty(CommunityNames.Entries);
        Assert.Contains("Sodium", ResourceNames.Queries("mod", "钠"));
        Utils.SetRoot(home.Dir);
        CommunityNames.Ensure(false, network);
        Assert.Equal(200, CommunityNames.Entries.Count);
        network.Offline = false; network.Revision = "新版";
        CommunityNames.Ensure(true, network);
        Assert.Empty(CommunityNames.LastError);
        Assert.All(CommunityNames.Entries, row => Assert.StartsWith("新版", row.Chinese));
        Assert.NotEqual(before, File.ReadAllText(file));
    }

    [Fact]
    public void MalformedRefreshAndFreshCorruptCacheRecoverWithoutLosingGoodData()
    {
        using var home = new TestHome();
        using var network = new IndexServer();
        CommunityNames.Ensure(true, network);
        string file = Path.Combine(home.Dir, "cache", "resource-names", "mod.csv");
        string before = File.ReadAllText(file);
        network.Malformed = true;
        CommunityNames.Ensure(true, network);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.Equal(200, CommunityNames.Entries.Count);
        string corruptHome = Path.Combine(home.Dir, "corrupt");
        Directory.CreateDirectory(Path.Combine(corruptHome, "cache", "resource-names"));
        foreach (string kind in new[] { "mod", "modpack" }) File.WriteAllText(Path.Combine(corruptHome, "cache", "resource-names", kind + ".csv"), "invalid");
        Utils.SetRoot(corruptHome); network.Malformed = false;
        CommunityNames.Ensure(false, network);
        Assert.Equal(200, CommunityNames.Entries.Count);
        Assert.Empty(CommunityNames.LastError);
    }

    private sealed class IndexServer : HttpMessageHandler
    {
        internal bool Offline, Malformed;
        internal string Revision = "原始";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Offline) throw new HttpRequestException("isolated offline transport");
            string csv = Malformed ? "invalid csv" : string.Join("\n", Enumerable.Range(1, 100).Select(i =>
                $"{i},fixture-{i},fixture-{i},{Revision}模组{i},Fixture {i},F{i},fixture{i}"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(csv) });
        }
    }
}
