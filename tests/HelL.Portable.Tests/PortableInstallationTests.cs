using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class PortableInstallationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-install-" + Guid.NewGuid().ToString("N"));
    private readonly IProgress<InstallProgress> progress = new Progress<InstallProgress>();

    [Fact]
    public void InheritedProfileUsesParentClientAndResolvedJavaMetadata()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("实例");
        var instance = new Instance("实例");
        WriteProfile(instance, "1.21.1", PyDict.Of("javaVersion", PyDict.Of("majorVersion", 21L)), client: true);
        WriteProfile(instance, "fabric-loader-test", PyDict.Of("inheritsFrom", "1.21.1", "mainClass", "net.fabricmc.loader.Main"));
        var metadata = PortableLauncher.ResolveLaunchMetadata(instance, "fabric-loader-test");
        Assert.Equal(21L, Py.Int(Py.DictOr(metadata.Get("javaVersion")).Get("majorVersion")));
        Assert.Equal("net.fabricmc.loader.Main", metadata.Get("mainClass"));
        Assert.Contains("fabric-loader-test", launcher.InstancesList().Single().Versions);
    }

    [Fact]
    public void MissingOrPendingParentPreventsInheritedLaunch()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("实例");
        var instance = new Instance("实例");
        WriteProfile(instance, "fabric-test", PyDict.Of("inheritsFrom", "1.21.1"));
        Assert.Throws<InvalidDataException>(() => PortableLauncher.ResolveLaunchMetadata(instance, "fabric-test"));
        WriteProfile(instance, "1.21.1", new PyDict(), client: true);
        string pending = Path.Combine(instance.VersionsDir(), "1.21.1", ".portable-install-pending");
        File.WriteAllText(pending, "pending");
        Assert.Empty(launcher.InstancesList().Single().Versions);
    }

    [Fact]
    public void MetadataCannotEscapeVersionsDirectory()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("实例");
        var instance = new Instance("实例");
        WriteProfile(instance, "unsafe", PyDict.Of("inheritsFrom", "../../other"));
        Assert.Throws<ArgumentException>(() => PortableLauncher.ResolveLaunchMetadata(instance, "unsafe"));
        Assert.Empty(launcher.InstancesList().Single().Versions);
    }

    [Fact]
    public void LoaderInstallUsesChosenVersionAndPromotesProfileOnlyAfterSuccess()
    {
        bool hiddenWhileInstalling = false;
        PortableLauncher? launcher = null;
        launcher = new PortableLauncher(root, (installer, version, loader, loaderVersion, extras) =>
        {
            Assert.Equal("1.21.1", version);
            Assert.Equal("fabric", loader);
            Assert.Equal("0.16.5", loaderVersion);
            WriteProfile(installer.instance, version, new PyDict(), client: true);
            WriteProfile(installer.instance, "fabric-test", PyDict.Of("inheritsFrom", version));
            hiddenWhileInstalling = launcher!.InstancesList().Single().Versions.Length == 0;
            return "fabric-test";
        });
        launcher.CreateInstance("实例");
        Assert.Equal("fabric-test", launcher.Install("实例", "1.21.1", "fabric", "0.16.5", progress, default));
        Assert.True(hiddenWhileInstalling);
        Assert.Contains("fabric-test", launcher.InstancesList().Single().Versions);
        Assert.Equal("fabric-test", new Instance("实例").Meta().Get("mc_version"));
        Assert.False(File.Exists(Path.Combine(launcher.InstanceFolder("实例"), ".portable-install.json")));
    }

    [Fact]
    public void FailedInstallRetainsJournalAndHealthyProfilesThenCanRetry()
    {
        bool fail = true;
        var launcher = new PortableLauncher(root, (installer, version, loader, loaderVersion, extras) =>
        {
            WriteProfile(installer.instance, version, new PyDict(), client: true);
            WriteProfile(installer.instance, "forge-test", PyDict.Of("inheritsFrom", version));
            installer.instance.SetMeta("mc_version", "forge-test");
            if (fail) throw new IOException("network interrupted");
            return "forge-test";
        });
        launcher.CreateInstance("实例");
        var instance = new Instance("实例");
        WriteProfile(instance, "1.20.1", new PyDict(), client: true);
        instance.SetMeta("mc_version", "1.20.1");
        Assert.Throws<IOException>(() => launcher.Install("实例", "1.21.1", "forge", "52.0.1", progress, default));
        Assert.Equal("1.20.1", instance.Meta().Get("mc_version"));
        Assert.Equal("1.20.1", launcher.InstancesList().Single().Versions.Single());
        Assert.Throws<InvalidOperationException>(() => launcher.Install("实例", "1.20.1", progress, default));
        fail = false;
        Assert.Equal("forge-test", launcher.Install("实例", "1.21.1", "forge", "52.0.1", progress, default));
        Assert.Contains("forge-test", launcher.InstancesList().Single().Versions);
    }

    [Fact]
    public void CancelledInstallDoesNotPromotePartiallyWrittenProfiles()
    {
        using var cancellation = new CancellationTokenSource();
        var launcher = new PortableLauncher(root, (installer, version, loader, loaderVersion, extras) =>
        {
            WriteProfile(installer.instance, version, new PyDict(), client: true);
            cancellation.Cancel();
            return version;
        });
        launcher.CreateInstance("实例");
        Assert.Throws<OperationCanceledException>(() => launcher.Install("实例", "1.21.1", progress, cancellation.Token));
        Assert.Empty(launcher.InstancesList().Single().Versions);
        Assert.True(File.Exists(Path.Combine(launcher.InstanceFolder("实例"), ".portable-install.json")));
    }

    [Fact]
    public void OptifineTokenIsPassedAsAnExplicitBuild()
    {
        var launcher = new PortableLauncher(root, (installer, version, loader, loaderVersion, extras) =>
        {
            Assert.Equal("optifine", loader);
            Assert.Equal("HD_U_I5", extras.Get("optifine_version"));
            WriteProfile(installer.instance, version, new PyDict(), client: true);
            return version;
        });
        launcher.CreateInstance("实例");
        launcher.Install("实例", "1.20.1", "optifine", "HD_U_I5", progress, default);
    }

    [Fact]
    public void LoaderInputIsValidatedBeforeWritingAnInstallJournal()
    {
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("实例");
        Assert.Throws<ArgumentException>(() => launcher.Install("实例", "1.21.1", "fabric", null, progress, default));
        Assert.Throws<ArgumentException>(() => launcher.Install("实例", "1.21.1", "unknown", "1", progress, default));
        Assert.Throws<ArgumentException>(() => launcher.Install("实例", "1.21.1", "forge", "../../escape", progress, default));
        Assert.False(File.Exists(Path.Combine(launcher.InstanceFolder("实例"), ".portable-install.json")));
    }

    private static void WriteProfile(Instance instance, string id, PyDict metadata, bool client = false)
    {
        string folder = Path.Combine(instance.VersionsDir(), id);
        Directory.CreateDirectory(folder);
        metadata["id"] = id;
        Utils.WriteJson(Path.Combine(folder, id + ".json"), metadata);
        if (client) File.WriteAllText(Path.Combine(folder, id + ".jar"), "client fixture");
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
