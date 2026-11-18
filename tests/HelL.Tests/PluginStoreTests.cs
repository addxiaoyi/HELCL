using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using HelL.Core.Launcher.Plugins;
using Xunit;

namespace HelL.Tests;

public sealed class PluginStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hell-plugin-test-" + Guid.NewGuid().ToString("N"));
    private PluginStore Store => new(Path.Combine(_root, "installed"));
    public PluginStoreTests() { Directory.CreateDirectory(_root); }
    public void Dispose() { Directory.Delete(_root, true); }
    private string Package(string extra = "", string text = "")
    {
        string file = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open())) writer.Write(PluginCatalog.Builtins()[0].Manifest.ToJson());
        if (extra.Length > 0) using (var writer = new StreamWriter(zip.CreateEntry(extra).Open())) writer.Write(text);
        return file;
    }
    [Fact]
    public void InstallDisableReopenRepairAndUninstallArePersistent()
    {
        string file = Package(); var manifest = Store.InstallPackage(file);
        Assert.True(Store.List().Single().Enabled);
        Store.SetEnabled(manifest.Id, false); Assert.False(Store.List().Single().Enabled);
        Store.InstallPackage(file); Assert.False(Store.List().Single().Enabled);
        Store.SetEnabled(manifest.Id, true); Assert.True(Store.List().Single().Enabled);
        Store.Uninstall(manifest.Id); Assert.Empty(Store.List());
    }
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("assets/../../outside.png")]
    [InlineData("C:/outside.png")]
    [InlineData("assets/evil.dll")]
    [InlineData("assets\\evil.png")]
    [InlineData("assets/picture.png:payload")]
    public void InvalidPackageCannotChangeAnExistingInstallation(string extra)
    {
        Store.InstallBuiltin(PluginCatalog.Builtins()[0].Manifest);
        Assert.Throws<InvalidDataException>(() => Store.InstallPackage(Package(extra, "invalid")));
        Assert.Single(Store.List()); Assert.True(Store.List().Single().Enabled); Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
    }
    [Fact]
    public void HashMismatchAndCatalogIdentityMismatchAreRejected()
    {
        string file = Package();
        Assert.Throws<InvalidDataException>(() => Store.InstallPackage(file, new string('0', 64)));
        Assert.Throws<InvalidDataException>(() => Store.InstallPackage(file, expectedId: "another-plugin"));
        Assert.Empty(Store.List());
    }
    [Fact]
    public void VerifiedDownloadPreservesDisabledStateAndRejectsTampering()
    {
        byte[] package = File.ReadAllBytes(Package());
        string checksum = Convert.ToHexString(SHA256.HashData(package));
        var manifest = PluginCatalog.Builtins()[0].Manifest;
        Store.InstallDownload(package, checksum, manifest.Id, manifest.Version);
        Store.SetEnabled(manifest.Id, false);
        Assert.Throws<InvalidDataException>(() => Store.InstallDownload(package, new string('0', 64), manifest.Id, manifest.Version));
        Assert.False(Store.List().Single().Enabled);
        Store.InstallDownload(package, checksum, manifest.Id, manifest.Version);
        Assert.False(Store.List().Single().Enabled);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "installed"), ".download-*"));
    }
    [Fact]
    public void BrokenManifestIsVisibleAndCanBeRepaired()
    {
        var manifest = PluginCatalog.Builtins()[0].Manifest; Store.InstallBuiltin(manifest);
        File.WriteAllText(Path.Combine(_root, "installed", manifest.Id, "plugin.json"), "broken");
        Assert.NotEmpty(Store.List().Single().Error); Assert.False(Store.List().Single().Enabled);
        Store.InstallBuiltin(manifest); Assert.Empty(Store.List().Single().Error);
    }
    [Fact]
    public void UnsupportedInstalledPluginIsListedAsBroken()
    {
        var manifest = PluginCatalog.Builtins()[0].Manifest; Store.InstallBuiltin(manifest);
        manifest.ApiVersion = 99;
        File.WriteAllText(Path.Combine(_root, "installed", manifest.Id, "plugin.json"), manifest.ToJson());
        Assert.NotEmpty(Store.List().Single().Error); Assert.False(Store.List().Single().Enabled);
    }
    [Fact]
    public void UnsupportedApiAndExecutableLinksAreRejected()
    {
        var manifest = PluginCatalog.Builtins()[2].Manifest; manifest.ApiVersion = 2;
        Assert.Throws<InvalidDataException>(manifest.Validate); manifest.ApiVersion = 1;
        manifest.Modules[0].Settings["url"] = "file:///C:/Windows/System32/cmd.exe";
        Assert.Throws<InvalidDataException>(manifest.Validate);
    }
    [Fact]
    public void OlderPackageCannotReplaceInstalledVersion()
    {
        var manifest = PluginCatalog.Builtins()[0].Manifest; manifest.Version = "2.0.0"; Store.InstallBuiltin(manifest);
        Assert.Throws<InvalidDataException>(() => Store.InstallPackage(Package()));
        Assert.Equal("2.0.0", Store.List().Single().Manifest.Version);
    }
}
