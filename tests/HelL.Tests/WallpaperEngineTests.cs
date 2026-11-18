using System;
using System.IO;
using System.Text.Json;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class WallpaperEngineTests
{
    [Fact]
    public void CurrentSelectionWinsOverHistoryAndOtherUsers()
    {
        string json = """
        {"other":{"general":{"wallpaperconfig":{"selectedwallpapers":{"Monitor0":{"file":"other.mp4"}}}}},
         "player":{"general":{"wallpaperconfigrecent":[{"config":{"selectedwallpapers":{"Monitor0":{"file":"old.mp4"}}}}],
          "wallpaperconfig":{"selectedwallpapers":{"Monitor1":{"file":"video.mp4"},"Monitor0":{"file":"scene.pkg"}}}}}}
        """;
        var selected = WallpaperEngine.ParseSelections(json, "PLAYER");
        Assert.Equal(2, selected.Count); Assert.Equal("scene.pkg", selected[0].File); Assert.Equal("video.mp4", selected[1].File);
    }
    [Fact]
    public void MissingCurrentSelectionDoesNotReplayHistory()
    {
        var selected = WallpaperEngine.ParseSelections("""{"wallpaperconfigrecent":[{"config":{"selectedwallpapers":{"Monitor0":{"file":"old.mp4"}}}}]}""");
        Assert.Empty(selected);
    }
    [Fact]
    public void ReadsRealProjectPreviewAndDoesNotTreatScenesAsVideo()
    {
        string folder = Path.Combine(Path.GetTempPath(), "hell-wallpaper-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "scene.pkg"), "fixture"); File.WriteAllText(Path.Combine(folder, "preview.jpg"), "fixture");
            File.WriteAllText(Path.Combine(folder, "project.json"), """{"title":"测试场景","type":"Scene","preview":"preview.jpg"}""");
            string config = JsonSerializer.Serialize(new { player = new { wallpaperconfig = new { selectedwallpapers = new { Monitor0 = new { file = Path.Combine(folder, "scene.pkg") } } } } });
            File.WriteAllText(Path.Combine(folder, "config.json"), config);
            var paper = Assert.Single(WallpaperEngine.Read(Path.Combine(folder, "config.json")));
            Assert.False(paper.Direct); Assert.Equal("测试场景", paper.Title); Assert.Equal(Path.Combine(folder, "preview.jpg"), paper.DisplayFile);
            File.WriteAllText(Path.Combine(folder, "project.json"), """{"type":"Scene","preview":"../outside.jpg"}""");
            Assert.Empty(WallpaperEngine.Read(Path.Combine(folder, "config.json"))[0].Preview);
        }
        finally { Directory.Delete(folder, true); }
    }
}
