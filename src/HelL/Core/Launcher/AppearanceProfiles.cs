using System;
using System.Globalization;

namespace HelL.Core.Launcher;

public static class AppearanceProfiles
{
    public static readonly string[] Keys = {
        "ui_wallpaper_mode", "ui_wallpaper_last_mode", "ui_wallpaper_engine_config", "ui_wallpaper_monitor", "ui_wallpaper_engine_live",
        "ui_wallpaper_video_optimize", "ui_glass_transparency", "ui_glass_cards", "ui_glass_controls", "ui_glass_popups", "ui_glass_frost"
    };

    public static PyDict Preset(string key, string currentMode)
    {
        string wallpaper = currentMode is "local" or "windows" or "engine" ? currentMode : "local";
        return key switch
        {
            "wallpaper-clear" => Material(wallpaper, 100, 100, 100, 10, 0),
            "wallpaper-glass" => Material(wallpaper, 82, 68, 42, 10, 18),
            "desktop-clear" => Material("transparent", 100, 100, 100, 10, 0),
            "system-glass" => Material("material", 100, 68, 42, 10, 0),
            "solid" => Material("material", 0, 0, 0, 0, 0),
            _ => throw new ArgumentException("外观方案不存在，请重新选择。")
        };
    }

    private static PyDict Material(string mode, int window, int cards, int controls, int popups, int frost) =>
        PyDict.Of("ui_wallpaper_mode", mode, "ui_glass_transparency", (long)window, "ui_glass_cards", (long)cards,
            "ui_glass_controls", (long)controls, "ui_glass_popups", (long)popups, "ui_glass_frost", (long)frost);

    public static void RememberSource(PyDict patch, string previousMode)
    {
        string mode = Py.S(patch.Get("ui_wallpaper_mode"));
        if (mode is "local" or "windows" or "engine") patch["ui_wallpaper_last_mode"] = mode;
        else if (!patch.ContainsKey("ui_wallpaper_last_mode") && mode.Length > 0 && previousMode is "local" or "windows" or "engine") patch["ui_wallpaper_last_mode"] = previousMode;
    }

    public static void Validate(PyDict settings)
    {
        if (settings.ContainsKey("ui_wallpaper_mode") && settings["ui_wallpaper_mode"] is not ("local" or "windows" or "engine" or "material" or "transparent"))
            throw new ArgumentException("主题包中的背景模式无效。");
        if (settings.ContainsKey("ui_wallpaper_last_mode") && settings["ui_wallpaper_last_mode"] is not ("local" or "windows" or "engine"))
            throw new ArgumentException("主题包中的壁纸来源无效。");
        foreach (string key in new[] { "ui_glass_transparency", "ui_glass_cards", "ui_glass_controls", "ui_glass_popups", "ui_glass_frost" })
        {
            if (!settings.ContainsKey(key)) continue;
            int min = key is "ui_glass_cards" or "ui_glass_controls" or "ui_glass_popups" ? -1 : 0;
            int max = key == "ui_glass_frost" ? 60 : 100;
            if (!long.TryParse(Py.Str(settings[key]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < min || value > max)
                throw new ArgumentException("主题包中的透明度或磨砂数值无效：" + key);
            settings[key] = value;
        }
    }
}
