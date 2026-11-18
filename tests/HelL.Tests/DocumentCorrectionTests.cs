using System;
using System.IO;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class DocumentCorrectionTests
{
    [Fact]
    public void ManualTranslationSurvivesCacheRemovalAndResetRestoresMachineText()
    {
        using var home = new TestHome();
        const string original = "Original paragraph.";
        string key = Py.S(CatalogDocument.Create("modrinth", "fixture", "description", "", original)["key"]);
        string automatic = CatalogDocument.TranslatePart(key, 0, original, new PyDict(), _ => "自动译文。");
        Assert.Equal("手工校对。", DocumentCorrections.Save(key, 0, original, "手工校对。", automatic));
        Assert.Equal("手工校对。", CatalogDocument.TranslatePart(key, 0, original, new PyDict(), _ => throw new Exception("must not overwrite manual edit")));
        Assert.Equal(automatic, DocumentCorrections.Save(key, 0, original, "", "手工校对。", reset: true));
        DocumentCorrections.Save(key, 0, original, "永久修订。", automatic);
        Directory.Delete(Path.Combine(home.Dir, "cache"), true);
        Assert.Equal("永久修订。", CatalogDocument.CurrentTranslation(key, 0, original));
        Assert.Equal("", CatalogDocument.CurrentTranslation(key, 0, "Changed paragraph."));
    }

    [Fact]
    public void StaleEditorCannotOverwriteAnotherCorrection()
    {
        using var home = new TestHome();
        string key = new string('a', 64);
        DocumentCorrections.Save(key, 0, "original", "第一次修订", "");
        Assert.Throws<InvalidOperationException>(() => DocumentCorrections.Save(key, 0, "original", "过期修订", ""));
        Assert.Equal("第一次修订", DocumentCorrections.Read(key, 0, "original"));
        Assert.Throws<ArgumentException>(() => DocumentCorrections.Save("../bad", 0, "original", "无效路径", ""));
    }

    [Fact]
    public void ManualEditWinsWhenModelCompletesAfterTheUserSaves()
    {
        using var home = new TestHome();
        string key = new string('b', 64);
        string returned = CatalogDocument.TranslatePart(key, 0, "A paragraph", new PyDict(), _ =>
        {
            DocumentCorrections.Save(key, 0, "A paragraph", "用户最终修订", "");
            return "较晚返回的机器译文";
        });
        Assert.Equal("用户最终修订", returned);
    }
}
