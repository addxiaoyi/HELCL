using HelL.Core;
using HelL.Core.Launcher;
using HelL.Tests;
using Xunit;

namespace HelL.Portable.Tests;

public sealed class SettingsCommitTests : IDisposable
{
    private readonly string previousLanguage = I18nCore.CurrentLanguage();
    public void Dispose() => I18nCore.CurrentLang = previousLanguage;
    [Fact]
    public void FailedSettingsPromotionPreservesMemoryAndOriginalFile()
    {
        using var fixture = new BackendFixture();
        LauncherConfig config = Config.CONFIG;
        string original = File.ReadAllText(Config.CONFIG_FILE);
        object width = config.Get("width");
        long revision = config.revision;
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(Config.CONFIG_FILE, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.ThrowsAny<Exception>(() => fixture.Api.save_settings(PyDict.Of("default_resolution", Py.L(1600L, 900L), "instances_dir", "alternate")));
        }
        else
        {
            string saved = Config.CONFIG_FILE + ".original";
            File.Move(Config.CONFIG_FILE, saved);
            Directory.CreateDirectory(Config.CONFIG_FILE);
            try { Assert.ThrowsAny<Exception>(() => fixture.Api.save_settings(PyDict.Of("default_resolution", Py.L(1600L, 900L), "instances_dir", "alternate"))); }
            finally { Directory.Delete(Config.CONFIG_FILE); File.Move(saved, Config.CONFIG_FILE); }
        }
        Assert.Equal(width, config.Get("width"));
        Assert.Equal(revision, config.revision);
        Assert.Equal(original, File.ReadAllText(Config.CONFIG_FILE));
    }

    [Fact]
    public void OneSettingsCommitIncludesDirectoryMultiInstanceLanguageAndAppearance()
    {
        using var fixture = new BackendFixture();
        fixture.Api.save_settings(PyDict.Of("instances_dir", "alternate", "allow_multi_instance", true,
            "language", "en", "default_resolution", Py.L(1600L, 900L),
            "ui_wpf_theme", PyDict.Of("Skin", "win11", "Tint", 70L)));
        var saved = Assert.IsType<PyDict>(Utils.ReadJson(Config.CONFIG_FILE));
        Assert.Equal("alternate", saved.Get("instances_dir"));
        Assert.True(Py.Truthy(saved.Get("allow_multi_instance")));
        Assert.Equal("en", saved.Get("language"));
        Assert.Equal(70L, Assert.IsType<PyDict>(saved.Get("ui_wpf_theme")).Get("Tint"));
        Assert.Equal("en", I18nCore.CurrentLanguage());
    }

    [Fact]
    public void UnwritableDirectoryRejectsTheWholeSettingsPatch()
    {
        using var fixture = new BackendFixture();
        string blocker = Path.Combine(Utils.ROOT, "directory-blocker");
        File.WriteAllText(blocker, "owned fixture");
        string before = File.ReadAllText(Config.CONFIG_FILE);
        Assert.ThrowsAny<Exception>(() => fixture.Api.save_settings(PyDict.Of("instances_dir", blocker,
            "default_resolution", Py.L(1600L, 900L), "allow_multi_instance", true)));
        Assert.Equal(before, File.ReadAllText(Config.CONFIG_FILE));
    }

    [UnixDirectoryFact]
    public void WritableLinkedGameDirectoryRemainsSupported()
    {
        using var fixture = new BackendFixture();
        string target = Path.Combine(Utils.ROOT, "games");
        string link = Path.Combine(Utils.ROOT, "linked-games");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        try
        {
            fixture.Api.save_settings(PyDict.Of("instances_dir", link, "allow_multi_instance", true));
            Assert.Equal(link, Config.CONFIG.Get("instances_dir"));
            Assert.True(Py.Truthy(Config.CONFIG.Get("allow_multi_instance")));
            Assert.Empty(Directory.EnumerateFiles(target, ".hell-write-probe-*"));
        }
        finally { Directory.Delete(link); }
    }

    public sealed class UnixDirectoryFactAttribute : FactAttribute
    {
        public UnixDirectoryFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Unix symbolic-link compatibility runs on Linux and macOS.";
        }
    }
}
