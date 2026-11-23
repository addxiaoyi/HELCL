using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class PortablePersistenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-persistence-" + Guid.NewGuid().ToString("N"));
    private readonly PortableLauncher launcher;

    public PortablePersistenceTests()
    {
        launcher = new PortableLauncher(root);
        launcher.CreateInstance("first");
        launcher.CreateInstance("second");
        launcher.SelectInstance("first");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSelectionOrPinWritePreservesMemoryRevisionAndDisk(bool pin)
    {
        long revision = Config.CONFIG.revision;
        byte[] before = File.ReadAllBytes(Config.CONFIG_FILE);
        string backup = Config.CONFIG_FILE + ".fixture";
        File.Move(Config.CONFIG_FILE, backup);
        Directory.CreateDirectory(Config.CONFIG_FILE);
        try
        {
            var error = Record.Exception(() => { if (pin) launcher.PinInstance("second", true); else launcher.SelectInstance("second"); });
            Assert.True(error is IOException or OSError, error?.ToString() ?? "Expected a configuration write failure.");
        }
        finally { Directory.Delete(Config.CONFIG_FILE); File.Move(backup, Config.CONFIG_FILE); }
        Assert.Equal("first", launcher.SelectedInstance);
        Assert.Equal(revision, Config.CONFIG.revision);
        Assert.Equal(before, File.ReadAllBytes(Config.CONFIG_FILE));
        Assert.DoesNotContain(launcher.LibraryList(), instance => instance.Pinned);
    }

    [Theory]
    [InlineData("not-json", false)]
    [InlineData("[]", false)]
    [InlineData("null", false)]
    [InlineData("not-json", true)]
    public void MetadataEditsRejectCorruptionAndPreserveOriginalBytes(string contents, bool java)
    {
        var instance = new Instance("first");
        string path = Path.Combine(instance.path, ".instance.json");
        File.WriteAllText(path, contents);
        Assert.Throws<InvalidDataException>(() => { if (java) instance.SetJavaPref("auto"); else launcher.SetReferenceVersion("first", "1.21.1", "fabric"); });
        Assert.Equal(contents, File.ReadAllText(path));
    }

    [Fact]
    public void MetadataWithInvalidUtf8IsPreservedWhenEdited()
    {
        var instance = new Instance("first");
        string path = Path.Combine(instance.path, ".instance.json");
        byte[] contents = [.. System.Text.Encoding.UTF8.GetBytes("{\"group\":\""), 0xff, .. System.Text.Encoding.UTF8.GetBytes("\"}")];
        File.WriteAllBytes(path, contents);

        Assert.Throws<InvalidDataException>(() => instance.SetJavaPref("auto"));

        Assert.Equal(contents, File.ReadAllBytes(path));
    }

    [Fact]
    public void CorruptMetadataPreventsRenameBeforeMovingDirectoryOrSettings()
    {
        launcher.PinInstance("first", true);
        string folder = launcher.InstanceFolder("first");
        string path = Path.Combine(folder, ".instance.json");
        File.WriteAllText(path, "corrupt metadata fixture");
        byte[] config = File.ReadAllBytes(Config.CONFIG_FILE);
        long revision = Config.CONFIG.revision;

        Assert.Throws<InvalidDataException>(() => launcher.RenameInstance("first", "renamed"));

        Assert.True(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(folder)!, "renamed")));
        Assert.Equal("corrupt metadata fixture", File.ReadAllText(path));
        Assert.Equal(config, File.ReadAllBytes(Config.CONFIG_FILE));
        Assert.Equal(revision, Config.CONFIG.revision);
        Assert.Equal("first", launcher.SelectedInstance);
    }

    [Fact]
    public void ReferenceVersionAndLoaderUpdateTogetherWithoutErasingOtherMetadata()
    {
        launcher.GroupInstance("first", "friends");
        launcher.SetReferenceVersion("first", "1.20.1", "forge");
        launcher.SetReferenceVersion("first", "1.21.1", "fabric");
        var metadata = new Instance("first").Meta();
        Assert.Equal("friends", metadata.Get("group"));
        Assert.Equal("1.21.1", metadata.Get("portable_reference_version"));
        Assert.Equal("fabric", metadata.Get("portable_reference_loader"));
    }

    public void Dispose() { Config.Reset(); Directory.Delete(root, recursive: true); }
}
