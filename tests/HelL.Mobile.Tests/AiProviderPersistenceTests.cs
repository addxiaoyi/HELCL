using HelL.Core;
using HelL.Core.Launcher;
using HelL.Core.Platform;
using HelL.Desktop.Services;
using HelL.Mobile.Ai;
using HelL.Portable;
using Xunit;

namespace HelL.Mobile.Tests;

public sealed class AiProviderPersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-provider-save-" + Guid.NewGuid().ToString("N"));
    private readonly Keys keys = new();

    public AiProviderPersistenceTests() => _ = new PortableLauncher(root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedProfileWriteRetainsConfigurationRevisionAndOnlyOriginalKey(bool desktop)
    {
        Save(desktop, "original", "original-key");
        byte[] before = File.ReadAllBytes(Config.CONFIG_FILE);
        long revision = Config.CONFIG.revision;
        string oldId = Py.S(Config.CONFIG.Get(KeyField(desktop)));
        string backup = Config.CONFIG_FILE + ".fixture";
        File.Move(Config.CONFIG_FILE, backup);
        Directory.CreateDirectory(Config.CONFIG_FILE);
        try
        {
            var error = Record.Exception(() => Save(desktop, "replacement", "replacement-key"));
            Assert.True(error is IOException or OSError, error?.ToString() ?? "Expected a configuration write failure.");
        }
        finally { Directory.Delete(Config.CONFIG_FILE); File.Move(backup, Config.CONFIG_FILE); }
        Assert.Equal(oldId, Py.S(Config.CONFIG.Get(KeyField(desktop))));
        Assert.Equal(revision, Config.CONFIG.revision);
        Assert.Equal(before, File.ReadAllBytes(Config.CONFIG_FILE));
        Assert.Equal("original-key", Assert.Single(keys.Values).Value);
        Assert.Equal(("original", "original-key"), Read(desktop));
    }

    [Theory]
    [InlineData(false, "receipt")]
    [InlineData(false, "verify")]
    [InlineData(true, "receipt")]
    [InlineData(true, "verify")]
    public void KeyStoreFailureRemovesTheAttemptedKeyAndKeepsTheOldProfile(bool desktop, string failure)
    {
        Save(desktop, "original", "original-key");
        byte[] before = File.ReadAllBytes(Config.CONFIG_FILE);
        keys.Failure = failure;
        Assert.Throws<IOException>(() => Save(desktop, "replacement", "replacement-key"));
        keys.Failure = "";
        Assert.Equal(before, File.ReadAllBytes(Config.CONFIG_FILE));
        Assert.Equal("original-key", Assert.Single(keys.Values).Value);
        Assert.Equal(("original", "original-key"), Read(desktop));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulReplacementKeepsOnlyReferencedKeyAndNoKeyInConfig(bool desktop)
    {
        Save(desktop, "original", "original-key");
        Save(desktop, "replacement", "replacement-key");
        string id = Py.S(Config.CONFIG.Get(KeyField(desktop)));
        Assert.Equal(id, Assert.Single(keys.Values).Key);
        Assert.Equal(("replacement", "replacement-key"), Read(desktop));
        string file = File.ReadAllText(Config.CONFIG_FILE);
        Assert.DoesNotContain("replacement-key", file);
        Assert.DoesNotContain("original-key", file);
        Save(desktop, "public-model", "", "public");
        Assert.Empty(keys.Values);
        Assert.Equal(("public-model", ""), Read(desktop));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadingDuringRotationReturnsACompleteProfile(bool desktop)
    {
        Save(desktop, "original", "original-key");
        using var reading = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        keys.BeforeRead = () =>
        {
            reading.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Credential read was not released.");
        };
        var snapshot = Task.Run(() => Read(desktop));
        Task? rotate = null;
        try
        {
            Assert.True(reading.Wait(TimeSpan.FromSeconds(10)));
            keys.BeforeRead = null;
            rotate = Task.Run(() => Save(desktop, "replacement", "replacement-key"));
            await Task.WhenAny(rotate, Task.Delay(250));
        }
        finally
        {
            keys.BeforeRead = null;
            release.Set();
            if (rotate != null) await rotate.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(("original", "original-key"), await snapshot.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(("replacement", "replacement-key"), Read(desktop));
        Assert.Equal("replacement-key", Assert.Single(keys.Values).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileDescriptionsNeverContainCredentials(bool desktop)
    {
        string description = desktop
            ? new DesktopAiProfile("custom", "https://fixture.invalid/v1", "model", "private-fixture-key").ToString()
            : new MobileAiProfile("custom", "https://fixture.invalid/v1", "model", "private-fixture-key").ToString();
        Assert.DoesNotContain("private-fixture-key", description);
    }

    private static string KeyField(bool desktop) => desktop ? "desktop_ai_key_id" : "mobile_ai_key_id";
    private void Save(bool desktop, string model, string key, string mode = "custom")
    {
        if (desktop) DesktopAiProfile.Save(Config.CONFIG, keys, new(mode, "https://fixture.invalid/v1", model, key));
        else MobileAiProfile.Save(Config.CONFIG, keys, new(mode, "https://fixture.invalid/v1", model, key));
    }

    private (string Model, string Key) Read(bool desktop)
    {
        if (desktop) { var profile = DesktopAiProfile.Read(Config.CONFIG, keys); return (profile.Model, profile.ApiKey); }
        var mobile = MobileAiProfile.Read(Config.CONFIG, keys); return (mobile.Model, mobile.ApiKey);
    }

    public void Dispose() { Config.Reset(); Directory.Delete(root, recursive: true); }

    private sealed class Keys : ISecretStore
    {
        internal System.Collections.Concurrent.ConcurrentDictionary<string, string> Values { get; } = new();
        internal string Failure { get; set; } = "";
        internal Action? BeforeRead { get; set; }
        private string latest = "";
        public string GetPassword(string service, string username)
        {
            BeforeRead?.Invoke();
            return Failure == "verify" && username == latest ? "" : Values.GetValueOrDefault(username, "");
        }
        public void SetPassword(string service, string username, string password)
        {
            latest = username;
            Values[username] = password;
            if (Failure == "receipt") throw new IOException("Controlled lost key-store write receipt.");
        }
        public void DeletePassword(string service, string username) => Values.TryRemove(username, out _);
    }
}
