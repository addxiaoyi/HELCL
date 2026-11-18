using System;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object appearance_snapshot() => Themes.Current();

    public object apply_appearance(object settings)
    {
        if (settings is not PyDict patch) throw new ArgumentException("外观设置格式无效。");
        Themes.Apply(patch);
        return Themes.Current();
    }

    public object apply_appearance_preset(string preset)
    {
        string mode = Py.S(Config.CONFIG.Get("ui_wallpaper_mode", "local"));
        if (mode is not ("local" or "windows" or "engine")) mode = Py.S(Config.CONFIG.Get("ui_wallpaper_last_mode", "local"));
        Themes.Apply(AppearanceProfiles.Preset(preset, mode));
        return Themes.Current();
    }
}
