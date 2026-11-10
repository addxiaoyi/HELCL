using System;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftExtensions
{
    public static bool FeiyuEnabled => Py.Truthy(Config.CONFIG.Get("minecraft_feiyu_enabled", false));
    public static bool ModelAutoStart => Py.Truthy(Config.CONFIG.Get("minecraft_model_auto_start", false));

    public static PyDict Status() => PyDict.Of("feiyu_enabled", FeiyuEnabled, "model_auto_start", ModelAutoStart);

    public static void Set(string extension, bool enabled)
    {
        string key = extension switch
        {
            "feiyu" => "minecraft_feiyu_enabled",
            "model_auto_start" => "minecraft_model_auto_start",
            _ => throw new ArgumentException("未知的拓展设置。")
        };
        var config = Config.CONFIG;
        lock (config._lock)
        {
            object previous = config.Get(key, false);
            config.Set(key, enabled);
            try { config.Save(); }
            catch { config.Set(key, previous); throw; }
        }
        if (extension == "feiyu" && !enabled)
        {
            if (MinecraftSession.ControlledByFeiyu) MinecraftSession.Stop();
            else FeiyuLink.Close();
        }
    }
}
