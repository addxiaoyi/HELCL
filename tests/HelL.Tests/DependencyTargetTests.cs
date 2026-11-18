using System;
using System.IO;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class DependencyTargetTests
{
    [Fact]
    public void DependencyUsesTheSelectedVersionsLoaderAndIsolation()
    {
        using var home = new TestHome();
        var instance = new Instance("dependencies", true); instance.Create();
        AddVersion(instance, "first-forge", "1.20.1", "net.minecraftforge:forge:47.0.0");
        AddVersion(instance, "custom-name", "1.21.1", "net.fabricmc:fabric-loader:0.16.10");
        VersionSettings.Save(instance, "custom-name", PyDict.Of("isolation", "all"));
        var target = DependencyTarget.Resolve(instance, "custom-name");
        Assert.Equal("1.21.1", target.GameVersion);
        Assert.Equal("fabric", target.Loader);
        Assert.Equal(Path.Combine(instance.VersionsDir(), "custom-name", "mods"), target.Folder);
        Assert.Throws<InvalidOperationException>(() => target.RequireCompatible(PyDict.Of("game_versions", Py.L("1.21.1"), "loaders", Py.L("forge"))));
        Assert.Throws<InvalidOperationException>(() => target.RequireCompatible(PyDict.Of("game_versions", Py.L("1.20.1"), "loaders", Py.L("fabric"))));
        Assert.Throws<ModError>(() => target.Destination("../escape.jar"));
        VersionSettings.Save(instance, "custom-name", PyDict.Of("isolation", "saves"));
        Assert.Equal(Path.Combine(instance.path, "mods"), DependencyTarget.Resolve(instance, "custom-name").Folder);
    }

    [Theory]
    [InlineData("embedded")]
    [InlineData("incompatible")]
    [InlineData("unknown")]
    public void NonInstallableDependenciesAreRejected(string relation)
        => Assert.Throws<ArgumentException>(() => DependencyTarget.RequireInstallable(relation));

    private static void AddVersion(Instance instance, string version, string mc, string library)
    {
        string folder = Path.Combine(instance.VersionsDir(), version); Directory.CreateDirectory(folder);
        Utils.WriteJson(Path.Combine(folder, version + ".json"), PyDict.Of("id", version, "inheritsFrom", mc, "libraries", Py.L(PyDict.Of("name", library))));
    }
}
