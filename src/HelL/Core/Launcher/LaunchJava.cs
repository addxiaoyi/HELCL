using System;

namespace HelL.Core.Launcher;

public static class LaunchJava
{
    public static bool Automatic(object preference)
    {
        string value = Py.S(preference).Trim();
        return value.Length == 0 || value == "自动选择" || value.Equals("auto", StringComparison.OrdinalIgnoreCase) || value.Equals("default", StringComparison.OrdinalIgnoreCase);
    }

    public static string Preference(Instance instance, string version, object requested = null)
    {
        foreach (object preference in new[] { VersionSettings.Load(instance, version).Get("java"), requested, instance.JavaPref(), Config.CONFIG.Get("default_java") })
            if (!Automatic(preference)) return Py.S(preference).Trim();
        return "";
    }

    public static PyDict Inspect(PyDict version, string preference)
    {
        long required = Java.RequiredJavaMajor(version);
        bool automatic = Automatic(preference);
        string executable = automatic ? Java.ResolveLaunchJava(version) : Java.PreferencePath(preference);
        long? major = string.IsNullOrEmpty(executable) ? null : Java.GetJavaMajor(executable);
        string code = string.IsNullOrEmpty(executable) ? automatic ? "java_auto_download" : "java_missing"
            : !major.HasValue ? "java_invalid" : major.Value < required ? "java_too_old" : "java_ready";
        string message = code switch
        {
            "java_auto_download" => "未找到兼容的 Java，启动时将自动下载安装。",
            "java_missing" => "指定的 Java 不存在，请自动匹配或重新选择。",
            "java_invalid" => "指定的 Java 无法运行或版本无法识别，请修复后重新检测。",
            "java_too_old" => "所选 Java 版本过低，请自动匹配兼容版本。",
            _ => "Java 检测通过。"
        };
        return PyDict.Of("code", code, "level", code == "java_ready" ? "ok" : code == "java_auto_download" ? "warn" : "error", "title", message,
            "detail", "需要 Java " + required + "+" + (major.HasValue ? "；当前 Java " + major.Value : "") + "\n" + (executable ?? preference),
            "path", executable ?? "", "required", required, "major", major, "recovery", code == "java_ready" ? "" : "java");
    }
}
