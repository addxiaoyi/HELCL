using System;
using System.IO;
using HelL.Services;
using Xunit;

namespace HelL.Tests;

public sealed class FilePickerRulesTests
{
    [Theory]
    [InlineData("Mods|*.jar;*.zip", "Example.JAR", true)]
    [InlineData("Mods|*.jar;*.zip", "Example.jar.disabled", false)]
    [InlineData("Java|javaw.exe;java.exe", "JAVA.EXE", true)]
    [InlineData("Java|javaw.exe;java.exe", "other.exe", false)]
    [InlineData("All|*.*", "LICENSE", true)]
    [InlineData("Logs|log?.txt", "log1.txt", true)]
    [InlineData("Logs|log?.txt", "log12.txt", false)]
    public void FiltersRespectPatterns(string filter, string name, bool matches)
    {
        Assert.Equal(matches, FilePickerRules.ParseFilters(filter)[0].Matches(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../profile.json")]
    [InlineData("..\\profile.json")]
    [InlineData("CON.txt")]
    [InlineData("NUL")]
    [InlineData("COM1.log")]
    [InlineData("LPT².txt")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("C:\\file")]
    [InlineData("report:stream")]
    public void InvalidWindowsNamesAreRejected(string name)
    {
        Assert.Throws<ArgumentException>(() => FilePickerRules.ValidateName(name));
    }

    [Theory]
    [InlineData("backup", "Archives|*.zip", "backup.zip")]
    [InlineData("backup.zip", "Archives|*.zip", "backup.zip")]
    [InlineData("backup.tar.gz", "Archives|*.zip", "backup.tar.gz")]
    [InlineData("存档 备份", "Archives|*.zip", "存档 备份.zip")]
    [InlineData("LICENSE", "All|*.*", "LICENSE")]
    public void SaveAddsOnlyMissingExtensions(string name, string filter, string expected)
    {
        string directory = Path.GetFullPath("picker-test");
        Assert.Equal(Path.Combine(directory, expected), FilePickerRules.SavePath(directory, name, FilePickerRules.ParseFilters(filter)[0]));
    }

    [Theory]
    [InlineData("Missing pattern")]
    [InlineData("Empty|")]
    public void MalformedFiltersFailExplicitly(string filter)
    {
        Assert.Throws<ArgumentException>(() => FilePickerRules.ParseFilters(filter));
    }
}
