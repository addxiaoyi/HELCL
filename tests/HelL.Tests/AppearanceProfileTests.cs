using System;
using System.IO;
using HelL.Core;
using HelL.Core.Launcher;
using Xunit;

namespace HelL.Tests;

public sealed class AppearanceProfileTests
{
    [Fact]
    public void UndoRestoresThePreviousWallpaperSourceAsWellAsTheMode()
    {
        using var home = new TestHome();
        Themes.Apply(AppearanceProfiles.Preset("wallpaper-glass", "windows"));
        Themes.Apply(AppearanceProfiles.Preset("desktop-clear", "windows"));
        var before = Themes.Current();
        Themes.Apply(AppearanceProfiles.Preset("wallpaper-glass", "engine"));
        Themes.Apply(before);
        Assert.Equal("transparent", Config.CONFIG.Get("ui_wallpaper_mode"));
        Assert.Equal("windows", Config.CONFIG.Get("ui_wallpaper_last_mode"));
    }

    [Fact]
    public void ReturningFromDesktopRestoresWallpaperSource()
    {
        using var home = new TestHome();
        Config.CONFIG.Set("ui_wallpaper_mode", "engine");
        Themes.Apply(AppearanceProfiles.Preset("desktop-clear", "engine"));
        Assert.Equal("engine", Config.CONFIG.Get("ui_wallpaper_last_mode"));
    }

    [Fact]
    public void WallpaperSelectorAndPresetsShareSourceHistory()
    {
        using var home = new TestHome();
        var api = (BackendApi)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(BackendApi));
        api.save_settings(PyDict.Of("ui_wallpaper_mode", "windows"));
        api.save_settings(PyDict.Of("ui_wallpaper_mode", "transparent"));
        api.apply_appearance_preset("wallpaper-glass");
        Assert.Equal("windows", Config.CONFIG.Get("ui_wallpaper_mode"));
    }

    [Theory]
    [InlineData("desktop-clear")]
    [InlineData("solid")]
    [InlineData("wallpaper-clear")]
    public void PresetsAcceptZeroBlurAndZeroOpacity(string preset)
    {
        using var home = new TestHome();
        Themes.Apply(AppearanceProfiles.Preset(preset, "local"));
        Assert.Equal(0L, Config.CONFIG.Get("ui_glass_frost"));
    }

    [Fact]
    public void ThemeRoundTripKeepsIndependentMaterialsAndWallpaperMode()
    {
        using var home = new TestHome();
        var saved = PyDict.Of("ui_wallpaper_mode", "transparent", "ui_glass_transparency", 100L,
            "ui_glass_cards", 77L, "ui_glass_controls", 43L, "ui_glass_popups", 12L, "ui_glass_frost", 8L);
        Config.CONFIG.Update(saved);
        Themes.SaveTheme("桌面工作区");
        Config.CONFIG.Update(PyDict.Of("ui_wallpaper_mode", "local", "ui_glass_cards", 0L));
        Themes.LoadTheme("桌面工作区");
        foreach (var entry in saved) Assert.Equal(entry.Value, Config.CONFIG.Get(entry.Key));
    }

    [Fact]
    public void InvalidMaterialCannotPartiallyApplyATheme()
    {
        using var home = new TestHome();
        Config.CONFIG.Set("ui_dark", false);
        Themes.SaveTheme("broken");
        string path = Path.Combine(home.Dir, "themes", "broken.json");
        Utils.WriteJson(path, PyDict.Of("name", "broken", "ui_dark", true, "ui_glass_cards", "bad"));
        Assert.ThrowsAny<Exception>(() => Themes.LoadTheme("broken"));
        Assert.False(Py.Truthy(Config.CONFIG.Get("ui_dark")));
    }
}
