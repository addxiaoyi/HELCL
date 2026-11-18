using System;
using System.IO;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class CustomResourceNamesTests
{
    [Fact]
    public void OverrideSurvivesCacheRemovalAndResetRestoresCommunityName()
    {
        using var home = new TestHome();
        var name = new CustomResourceName { Kind = "mod", Source = "modrinth", Slug = "sodium", Id = "AANobbMI", English = "Sodium", Chinese = "我的钠优化", Aliases = new[] { "帧率救星", "fpshelper" } };
        CustomResourceNames.Save(name);
        var project = PyDict.Of("kind", "mod", "source", "modrinth", "slug", "sodium", "name", "Sodium");
        CatalogPresentation.Localize(project, new PyDict());
        Assert.Equal("我的钠优化", project["chinese_name"]);
        Assert.Contains("Sodium", ResourceNames.Queries("mod", "帧率救星"));
        Assert.Contains("Sodium", ResourceNames.Queries("mod", "fpshelper"));
        Assert.Equal("Sodium", project["name"]);
        Assert.Null(CustomResourceNames.Find("curseforge", "sodium", "AANobbMI"));
        Utils.SetRoot(Path.Combine(home.Dir, "other"));
        Assert.Empty(CustomResourceNames.Entries);
        Utils.SetRoot(home.Dir);
        Assert.Equal("我的钠优化", CustomResourceNames.Find("modrinth", "sodium", "").Chinese);
        name.Chinese = ""; name.Aliases = Array.Empty<string>(); CustomResourceNames.Save(name);
        CatalogPresentation.Localize(project, new PyDict());
        Assert.Equal("钠", project["chinese_name"]);
    }

    [Fact]
    public void InvalidLabelsAndCorruptStoreNeverOverwritePreviousEdits()
    {
        using var home = new TestHome();
        var name = new CustomResourceName { Kind = "world", Source = "curseforge", Id = "123", English = "World", Chinese = "我的世界" };
        CustomResourceNames.Save(name);
        string path = Path.Combine(home.Dir, "resource-names.json");
        string saved = File.ReadAllText(path);
        name.Aliases = new[] { "bad\nname" };
        Assert.Throws<ArgumentException>(() => CustomResourceNames.Save(name));
        Assert.Equal(saved, File.ReadAllText(path));
        File.WriteAllText(path, "broken"); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        name.Aliases = Array.Empty<string>();
        Assert.Throws<InvalidDataException>(() => CustomResourceNames.Save(name));
        Assert.Equal("broken", File.ReadAllText(path));
    }
}
