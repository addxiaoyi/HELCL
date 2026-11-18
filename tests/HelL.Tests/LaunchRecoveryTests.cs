using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class LaunchRecoveryTests
{
    [Fact]
    public void JavaVersionCacheExpiresWhenExecutableIsReplaced()
    {
        using var home = new TestHome(); string exe = Path.Combine(home.Dir, "java.exe");
        var hook = Java.VersionOutputHook;
        try
        {
            Java.ResetCaches(); Java.VersionOutputHook = File.ReadAllText;
            File.WriteAllText(exe, "openjdk version \"17.0.1\""); Assert.Equal(17, Java.GetJavaMajor(exe));
            File.WriteAllText(exe, "openjdk version \"21.0.10\" repaired"); Assert.Equal(21, Java.GetJavaMajor(exe));
            File.Delete(exe); Assert.Null(Java.GetJavaMajor(exe));
        }
        finally { Java.VersionOutputHook = hook; Java.ResetCaches(); }
    }

    [Theory]
    [InlineData("", "java_invalid")]
    [InlineData("openjdk version \"17.0.1\"", "java_too_old")]
    public void PreflightChecksTheVersionOverrideUsedByLaunch(string output, string code)
    {
        using var home = new TestHome();
        var instance = CreateInstance(); string exe = Path.Combine(home.Dir, "java.exe"); File.WriteAllText(exe, "fixture");
        var hook = Java.VersionOutputHook;
        try
        {
            Java.ResetCaches(); Java.VersionOutputHook = _ => output;
            VersionSettings.Save(instance, "1.21.1", PyDict.Of("java", exe));
            var check = Preflight.CheckLaunch(instance, "1.21.1");
            Assert.Contains(Py.ListOr(check.Get("items")).OfType<PyDict>(), item => Py.S(item.Get("code")) == code);
            Assert.False(Py.Truthy(check.Get("ok")));
        }
        finally { Java.VersionOutputHook = hook; Java.ResetCaches(); }
    }

    private static Instance CreateInstance()
    {
        var instance = new Instance("recovery-test", true); instance.Create();
        string folder = Path.Combine(instance.VersionsDir(), "1.21.1"); Directory.CreateDirectory(folder);
        Utils.WriteJson(Path.Combine(folder, "1.21.1.json"), PyDict.Of("id", "1.21.1", "mainClass", "test.Main", "javaVersion", PyDict.Of("majorVersion", 21L)));
        File.WriteAllText(Path.Combine(folder, "1.21.1.jar"), "fixture"); return instance;
    }

    [Fact]
    public void RepairChecksTheSharedParentJarAndDoesNotCreateAChildCopy()
    {
        using var home = new TestHome(); Config.CONFIG.Set("download_segments", 1L);
        var instance = CreateInstance(); byte[] client = Encoding.UTF8.GetBytes("verified fixture client jar");
        using var http = new DownloadHttpFixture(_ => new DownloadHttpFixture.Reply(200, client));
        var parent = (PyDict)instance.VersionJson("1.21.1");
        parent["downloads"] = PyDict.Of("client", PyDict.Of("url", http.Url, "sha1", Convert.ToHexString(SHA1.HashData(client)).ToLowerInvariant(), "size", client.Length));
        string parentFolder = Path.Combine(instance.VersionsDir(), "1.21.1"); Utils.WriteJson(Path.Combine(parentFolder, "1.21.1.json"), parent);
        string child = Path.Combine(instance.VersionsDir(), "child"); Directory.CreateDirectory(child);
        Utils.WriteJson(Path.Combine(child, "child.json"), PyDict.Of("id", "child", "inheritsFrom", "1.21.1"));
        var before = Preflight.CheckLaunch(instance, "child");
        Assert.Contains(Py.ListOr(before.Get("items")).OfType<PyDict>(), item => Py.S(item.Get("code")) == "client_corrupt");
        Repair.RepairVersion(new Installer(instance, new DownloadManager()), "child");
        Assert.Equal(client, File.ReadAllBytes(Path.Combine(parentFolder, "1.21.1.jar")));
        Assert.False(File.Exists(Path.Combine(child, "child.jar")));
        var after = Preflight.CheckLaunch(instance, "child");
        Assert.DoesNotContain(Py.ListOr(after.Get("items")).OfType<PyDict>(), item => Py.S(item.Get("code")) is "client_corrupt" or "no_client_jar");
        File.WriteAllText(Path.Combine(child, "child.jar"), "loader generated jar has its own format");
        Assert.DoesNotContain(Py.ListOr(Preflight.CheckLaunch(instance, "child").Get("items")).OfType<PyDict>(), item => Py.S(item.Get("code")) == "client_corrupt");
    }
}
