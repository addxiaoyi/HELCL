using HelL.Core;
using HelL.Core.Launcher;
using HelL.Portable;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class PortableTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hell-portable-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DataLivesNextToExecutable()
    {
        Assert.Equal(Path.Combine(root, "data"), PortableHome.Resolve(root, "", false));
        Assert.Equal(root, PortableHome.Resolve("/irrelevant", root, false));
        Assert.Throws<ArgumentException>(() => PortableHome.Resolve(root, "relative", false));
    }

    [Fact]
    public void MacDataStaysOutsideSignedBundle()
    {
        string app = Path.Combine(root, "HelL.app");
        string binaries = Path.Combine(app, "Contents", "MacOS");
        Assert.Equal(Path.Combine(root, "data"), PortableHome.Resolve(binaries, "", true));
        Assert.Throws<ArgumentException>(() => PortableHome.Resolve(binaries, Path.Combine(app, "data"), true));
        Assert.Throws<IOException>(() => PortableHome.Resolve("/private/AppTranslocation/uuid/d/HelL.app/Contents/MacOS", "", true));
    }

    [Fact]
    public void HomeCannotBeSharedUntilOwnerExits()
    {
        using (PortableHome.Open(root))
            Assert.Throws<IOException>(() => PortableHome.Open(root));
        using var reopened = PortableHome.Open(root);
        Assert.Equal(root, reopened.Root);
    }

    [Fact]
    public void PortableProcessPreservesArgumentsWithoutShellQuoting()
    {
        string[] arguments = ["java", "中文路径/a b", "", "a\"b", "tail\\", "$HOME", "$(echo unsafe)"];
        var start = Utils.MakeStartInfo(arguments, root);
        Assert.False(start.UseShellExecute);
        Assert.Equal(arguments.Skip(1), start.ArgumentList);
    }

    [Fact]
    public void InstanceCreationSelectionAndCancellationPreserveState()
    {
        using var lease = PortableHome.Open(root);
        var launcher = new PortableLauncher(root);
        Assert.Empty(launcher.InstancesList());
        launcher.CreateInstance("中文生存实例");
        launcher.SelectInstance("中文生存实例");
        Assert.Equal("中文生存实例", new LauncherConfig().Get("default_instance"));
        Assert.Single(launcher.InstancesList());
        Assert.ThrowsAny<Exception>(() => launcher.CreateInstance("中文生存实例"));
        foreach (string invalid in new[] { "", "..", "../escape", "CON", "a/b", ".minecraft" })
            Assert.Throws<ArgumentException>(() => launcher.CreateInstance(invalid));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => launcher.RemoteVersions(cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => launcher.Install("中文生存实例", "1.21.1", new Progress<InstallProgress>(), cancelled.Token));
        Assert.Empty(launcher.InstancesList().Single().Versions);
        Assert.False(File.Exists(Path.Combine(root, "accounts.json")));
        Assert.Throws<ArgumentException>(() => launcher.PrepareDemo("中文生存实例", "../escape", "", new Progress<InstallProgress>(), default));
    }

    [Fact]
    public void IncompleteInstallationIsNotOfferedAsPlayable()
    {
        using var lease = PortableHome.Open(root);
        var launcher = new PortableLauncher(root);
        launcher.CreateInstance("安装中断");
        var instance = new Instance("安装中断");
        string version = Path.Combine(instance.VersionsDir(), "1.21.1");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{\"id\":\"1.21.1\"}");
        Assert.Empty(launcher.InstancesList().Single().Versions);
        File.WriteAllText(Path.Combine(version, "1.21.1.jar"), "incomplete test fixture");
        File.WriteAllText(Path.Combine(version, ".portable-install-pending"), "pending");
        Assert.Empty(launcher.InstancesList().Single().Versions);
        Assert.Throws<InvalidOperationException>(() => launcher.PrepareDemo("安装中断", "1.21.1", "", new Progress<InstallProgress>(), default));
        File.Delete(Path.Combine(version, ".portable-install-pending"));
        Assert.Equal("1.21.1", launcher.InstancesList().Single().Versions.Single());
    }

    [Fact]
    public void DownloadedRuntimeCanActuallyExecuteOnUnix()
    {
        Directory.CreateDirectory(root);
        Utils.SetRoot(root);
        string java = Path.Combine(root, "java");
        File.WriteAllText(java, "#!/bin/sh\nprintf 'openjdk version \"21.0.1\"\\n' >&2\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(java, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Java.Chmod755(java);
        if (!OperatingSystem.IsWindows()) Assert.Equal(21L, Java.GetJavaMajor(java));
        else Assert.True(File.Exists(java));
    }

    [Fact]
    public void JavaTarExtractionPreservesExecuteBitsWithoutSpecialPermissions()
    {
        Directory.CreateDirectory(root);
        Utils.SetRoot(root);
        string archive = Path.Combine(root, "java.tar.gz");
        byte[] script = Encoding.UTF8.GetBytes("#!/bin/sh\nprintf 'openjdk version \"21.0.1\"\\n' >&2\n");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionMode.Compress))
        using (var tar = new TarWriter(gzip))
        using (var content = new MemoryStream(script))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "bin/java")
            {
                DataStream = content, Mode = (UnixFileMode)0x9ED
            });
        }
        string extracted = Path.Combine(root, "runtime");
        Utils.SafeExtractTargz(archive, extracted);
        string java = Path.Combine(extracted, "bin", "java");
        Assert.Equal(script, File.ReadAllBytes(java));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal((UnixFileMode)0x1ED, File.GetUnixFileMode(java));
            Assert.Equal(21L, Java.GetJavaMajor(java));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
