using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? Path.GetTempPath(), "hell-workspace-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RenamingPreservesGroupPinsAndSelectedInstanceAcrossReload()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("中文生存");
        launcher.SelectInstance("中文生存");
        launcher.GroupInstance("中文生存", "与朋友联机");
        launcher.PinInstance("中文生存", true);
        launcher.RenameInstance("中文生存", "周末世界");
        var reopened = new PortableLauncher(root);
        var entry = Assert.Single(reopened.LibraryList());
        Assert.Equal("周末世界", entry.Name);
        Assert.Equal("与朋友联机", entry.Group);
        Assert.True(entry.Pinned);
        Assert.Equal("周末世界", reopened.SelectedInstance);
        Assert.Empty(entry.Cover);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("CON")]
    public void InvalidRenamePreservesInstance(string replacement)
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("保留世界");
        Assert.Throws<ArgumentException>(() => launcher.RenameInstance("保留世界", replacement));
        Assert.Equal("保留世界", Assert.Single(launcher.LibraryList()).Name);
    }

    [Fact]
    public void CorruptMetadataDoesNotBecomeAnEmptyEditableRecord()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("损坏元数据");
        string metadata = Path.Combine(launcher.InstanceFolder("损坏元数据"), ".instance.json");
        File.WriteAllText(metadata, "not json");
        Assert.Throws<InvalidDataException>(() => launcher.LibraryList());
        Assert.Equal("not json", File.ReadAllText(metadata));
    }

    [Fact]
    public void InterruptedGameCannotBeUsedAsResourceTarget()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("安装中");
        var game = new Instance("安装中");
        string folder = Path.Combine(game.VersionsDir(), "1.21.1");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "1.21.1.json"), "{\"id\":\"1.21.1\"}");
        File.WriteAllText(Path.Combine(folder, ".portable-install-pending"), "pending");
        Assert.Throws<InvalidOperationException>(() => new PortableCatalog().Target("安装中", "1.21.1"));
    }

    [Fact]
    public void MobileReferenceVersionFiltersResourcesButIsNotPlayable()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("手机实例");
        launcher.SetReferenceVersion("手机实例", "1.21.1");
        Assert.Equal("1.21.1", new PortableCatalog().Target("手机实例", "1.21.1").GameVersion);
        Assert.Empty(launcher.InstancesList().Single().Versions);
        Assert.Throws<InvalidOperationException>(() => launcher.PrepareDemo("手机实例", "1.21.1", "", new Progress<InstallProgress>(), default));
        Assert.Throws<ArgumentException>(() => launcher.SetReferenceVersion("手机实例", "../escape"));
        Assert.Equal("1.21.1", launcher.ReferenceVersion("手机实例"));
        launcher.SetReferenceVersion("手机实例", "1.21.1", "fabric");
        Assert.Equal("fabric", new PortableCatalog().Target("手机实例", "1.21.1").Loader);
        Assert.Empty(launcher.InstancesList().Single().Versions);
        Assert.Throws<ArgumentException>(() => launcher.SetReferenceVersion("手机实例", "1.21.1", "unknown-loader"));
    }

    [Fact]
    public void ResourceCompatibilityDoesNotAcceptWrongGameOrModLoader()
    {
        var release = new CatalogRelease("file", "1", "test.jar", ["1.21.1"], ["fabric"], "2026-11-11", 0, new PyDict());
        Assert.True(PortableCatalog.Compatible("mod", release, new CatalogTarget { GameVersion = "1.21.1", Loader = "fabric" }));
        Assert.False(PortableCatalog.Compatible("mod", release, new CatalogTarget { GameVersion = "1.21.1", Loader = "forge" }));
        Assert.False(PortableCatalog.Compatible("mod", release, new CatalogTarget { GameVersion = "1.21.1" }));
        Assert.False(PortableCatalog.Compatible("resourcepack", release, new CatalogTarget { GameVersion = "1.20.1" }));
        Assert.True(PortableCatalog.Compatible("resourcepack", release, new CatalogTarget { GameVersion = "1.21.1" }));
    }

    [Theory]
    [InlineData("../escape.jar")]
    [InlineData("..\\escape.jar")]
    [InlineData("stream:payload.jar")]
    [InlineData("/absolute.jar")]
    public void RemoteFilenamesCannotEscapeDownloadFolder(string filename)
    {
        Assert.Throws<InvalidDataException>(() => PortableCatalog.SafeDestination(root, filename));
        Assert.False(Directory.Exists(root));
    }

    public void Dispose()
    {
        Config.Reset();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
