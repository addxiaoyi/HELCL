using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HelL.Services;

public static class Typography
{
    public const string DefaultStack = "Segoe UI Variable Text, Noto Sans SC, Microsoft YaHei UI, Segoe UI";
    public static string Family { get; private set; } = "";
    public static void Apply(string family)
    {
        Family = family;
        var selected = Fonts.SystemFontFamilies.FirstOrDefault(font => font.Source.Equals(family, StringComparison.OrdinalIgnoreCase));
        Application.Current.Resources["F.Ui"] = selected == null ? new FontFamily(DefaultStack) : new FontFamily(selected.Source + ", " + DefaultStack);
    }
    public static async Task Load()
    {
        string family = await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_font_family", @default = "" }) ?? "";
        Apply(family);
    }
    public static async Task Show()
    {
        var families = Fonts.SystemFontFamilies.Select(font => font.Source).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToList();
        var picker = Ui.Combo(new[] { "默认 · Segoe UI / 思源黑体" }.Concat(families));
        picker.SelectedIndex = Math.Max(0, families.FindIndex(name => name.Equals(Family, StringComparison.OrdinalIgnoreCase)) + 1);
        var sample = Ui.V(8, Ui.Txt("继续你的冒险", 24, true), Ui.Txt("Minecraft · 生存世界 · 1.21.1", 16, true), Ui.Txt("方块、光影与新的故事。 0123456789", 14));
        void Preview()
        {
            var font = picker.SelectedIndex > 0 ? new FontFamily(families[picker.SelectedIndex - 1] + ", " + DefaultStack) : new FontFamily(DefaultStack);
            foreach (TextBlock text in sample.Children) text.FontFamily = font;
        }
        picker.SelectionChanged += (_, _) => Preview();
        Preview();
        if (!await Dlg.Ask("界面字体", Ui.V(16, Ui.Muted("使用本机已安装字体。中文缺字时自动使用后备字体。"), picker, sample), "应用字体", width: 580)) return;
        string chosen = picker.SelectedIndex > 0 ? families[picker.SelectedIndex - 1] : "";
        await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_font_family"] = chosen } }); Apply(chosen);
    }
}
