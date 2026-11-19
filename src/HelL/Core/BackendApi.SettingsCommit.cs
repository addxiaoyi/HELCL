using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    private static void ValidateSettingsCommit(PyDict patch)
    {
        if (patch.ContainsKey("instances_dir"))
        {
            string entered = Py.S(patch["instances_dir"]).Trim();
            if (entered.Length == 0 || entered.Length > 4096 || entered.Any(char.IsControl))
                throw new ArgumentException("游戏目录无效，请重新选择。");
            string expanded = ExpandUser(entered);
            string absolute = Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(Utils.ROOT, expanded));
            string probe = Path.Combine(absolute, ".hell-write-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(absolute);
                using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Flush(true);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { throw new IOException("游戏目录不可写，全部设置尚未提交。", error); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
            patch["instances_dir"] = Path.IsPathRooted(expanded) ? absolute : entered;
        }
        if (patch.ContainsKey("language"))
        {
            string language = Py.S(patch["language"]).Replace('-', '_');
            if (!I18nCore.AvailableLanguages().ContainsKey(language)) throw new ArgumentException("界面语言无效，设置未保存。");
            patch["language"] = language;
        }
        if (patch.Get("ui_wpf_theme") is PyDict theme)
        {
            string[] allowed = { "ThemeColor", "Background", "Skin", "Tint", "AutoAccent", "FlyAnimation", "FlyDurationMs" };
            if (theme.Keys.Any(key => !allowed.Contains(key))) throw new ArgumentException("外观偏好包含无法识别的字段。");
            if (theme.ContainsKey("ThemeColor") && !Regex.IsMatch(Py.S(theme["ThemeColor"]), @"\A#[0-9a-fA-F]{6}\z"))
                throw new ArgumentException("外观颜色无效。");
            foreach (string key in new[] { "Background", "Skin" })
                if (theme.ContainsKey(key) && !Regex.IsMatch(Py.S(theme[key]), @"\A[a-zA-Z0-9_-]{1,48}\z")) throw new ArgumentException("外观类型无效。");
            if (theme.ContainsKey("Tint") && (Py.Int(theme["Tint"]) < 0 || Py.Int(theme["Tint"]) > 100)) throw new ArgumentException("外观浓度超出范围。");
            if (theme.ContainsKey("FlyDurationMs") && (Py.Int(theme["FlyDurationMs"]) < 1 || Py.Int(theme["FlyDurationMs"]) > 10000))
                throw new ArgumentException("动画时长超出范围。");
        }
        else if (patch.ContainsKey("ui_wpf_theme")) throw new ArgumentException("外观偏好格式无效。");
    }
}
