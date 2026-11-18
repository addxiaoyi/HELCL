using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class ResourceNameTransferTests
{
    private static CustomResourceName Entry(string slug, string chinese, params string[] aliases) => new() {
        Kind = "mod", Source = "modrinth", Slug = slug, English = slug, Chinese = chinese, Aliases = aliases };

    [Fact]
    public void PreviewDoesNotWriteAndMergeKeepsNamesAndBothAliasSets()
    {
        using var home = new TestHome();
        CustomResourceNames.Save(Entry("sodium", "本机钠", "帧率"));
        string path = Path.Combine(home.Dir, "import.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Format = "hell-resource-names", Version = 1, Entries = new[] {
            Entry("sodium", "外部钠", "优化"), Entry("iris", "鸢尾") } }));
        var preview = CustomResourceNames.PreviewImport(path);
        Assert.Equal(new[] { "conflict", "new" }, preview.Rows.Select(row => row.Status));
        Assert.Single(CustomResourceNames.Entries);
        Assert.Equal(2, CustomResourceNames.ApplyImport(preview, new Dictionary<string, string> { ["modrinth:sodium"] = "merge" }));
        var sodium = CustomResourceNames.Find("modrinth", "sodium", "");
        Assert.Equal("本机钠", sodium.Chinese); Assert.Equal(new[] { "帧率", "优化", "外部钠" }, sodium.Aliases);
        Assert.True(File.Exists(Path.Combine(home.Dir, "resource-names.json.import-backup")));
        string export = Path.Combine(home.Dir, "export.json");
        Assert.Equal(2, CustomResourceNames.Export(export));
        Assert.All(CustomResourceNames.PreviewImport(export).Rows, row => Assert.Equal("same", row.Status));
    }

    [Theory]
    [InlineData("keep", "本机钠", 0)]
    [InlineData("replace", "外部钠", 1)]
    public void ConflictsUseTheSelectedPolicy(string action, string expected, int changed)
    {
        using var home = new TestHome();
        CustomResourceNames.Save(Entry("sodium", "外部钠"));
        string path = Path.Combine(home.Dir, "names.json"); CustomResourceNames.Export(path);
        CustomResourceNames.Save(Entry("sodium", "本机钠"));
        var preview = CustomResourceNames.PreviewImport(path);
        Assert.Equal(changed, CustomResourceNames.ApplyImport(preview, new Dictionary<string, string> { ["modrinth:sodium"] = action }));
        Assert.Equal(expected, CustomResourceNames.Find("modrinth", "sodium", "").Chinese);
    }

    [Fact]
    public void StalePreviewAndInvalidFileNeverApplyPartialChanges()
    {
        using var home = new TestHome();
        CustomResourceNames.Save(Entry("sodium", "钠"));
        string path = Path.Combine(home.Dir, "names.json"); CustomResourceNames.Export(path);
        var preview = CustomResourceNames.PreviewImport(path);
        CustomResourceNames.Save(Entry("iris", "鸢尾"));
        Assert.Throws<InvalidOperationException>(() => CustomResourceNames.ApplyImport(preview, new Dictionary<string, string>()));
        File.WriteAllText(path, "{\"Format\":\"other\",\"Version\":1}");
        Assert.Throws<InvalidDataException>(() => CustomResourceNames.PreviewImport(path));
        Assert.Equal(2, CustomResourceNames.Entries.Count);
        Assert.Throws<ArgumentException>(() => CustomResourceNames.Export(Path.Combine(home.Dir, "resource-names.json")));
    }
}
