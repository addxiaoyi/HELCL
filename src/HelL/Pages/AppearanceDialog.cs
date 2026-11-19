using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL.Pages;

public static class AppearanceDialog
{
    public static async Task<bool> NeedsRestart() => AppServices.Window?.AllowsTransparency !=
        (await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_mode", @default = "local" }) == "transparent");

    public static async Task Show()
    {
        var api = AppServices.Client;
        bool busy = false;
        Dictionary<string, JsonElement>? undo = null;
        var status = Ui.Muted("点选即应用，可撤销上一次切换。").Wrap();
        var custom = Ui.Combo(placeholder: "选择已保存的方案");
        var choices = Ui.V(2);
        var restore = Ui.Btn("撤销上次切换", BtnKind.Ghost); restore.IsEnabled = false;
        var save = Ui.Btn("保存当前方案…", BtnKind.Ghost);
        var restart = Ui.Btn("重启应用外观", BtnKind.Primary, (_, _) => AppServices.Window?.RestartForAppearance());
        restart.Visibility = await NeedsRestart() ? Visibility.Visible : Visibility.Collapsed;
        Dlg.Layer? layer = null;
        var close = Ui.Btn("关闭", BtnKind.Ghost, (_, _) => layer!.Dismiss());
        var actions = Ui.H(8, restore, save, restart, close); actions.Wrap = true;

        async Task Refresh()
        {
            if (AppServices.Window != null) await AppServices.Window.ReloadAppearanceAsync();
            bool pending = await NeedsRestart();
            restart.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            status.Text = pending ? "方案已保存，重启后切换窗口背景。原壁纸选择已保留。" : "方案已应用。";
        }
        async Task Change(Func<Task> change, bool remember = true)
        {
            if (busy) return;
            busy = true; choices.IsEnabled = custom.IsEnabled = actions.IsEnabled = false;
            try
            {
                var before = await api.CallAsync<Dictionary<string, JsonElement>>("appearance_snapshot");
                await change();
                if (remember) undo = before;
                await Refresh();
            }
            catch (Exception error) { Guard.WriteLog("appearance-profile", error.ToString()); status.Text = "外观方案未能应用：" + error.Message; }
            finally { busy = false; choices.IsEnabled = custom.IsEnabled = actions.IsEnabled = true; restore.IsEnabled = undo != null; }
        }
        foreach (var preset in new[] {
            ("wallpaper-clear", "通透壁纸", "完整展示壁纸，菜单保留阅读底色"),
            ("wallpaper-glass", "柔光玻璃", "轻磨砂，适合日常使用"),
            ("desktop-clear", "桌面直透", "看到窗口后方的桌面与其他应用"),
            ("system-glass", "系统玻璃", "使用 Windows Mica / Acrylic 材质"),
            ("solid", "专注阅读", "实色背景，关闭磨砂") })
        {
            string key = preset.Item1;
            var button = Ui.Btn(preset.Item2, BtnKind.Ghost, (_, _) => PageBase.Run(() => Change(() => api.CallAsync("apply_appearance_preset", new { preset = key }))));
            button.Content = Ui.V(3, Ui.Txt(preset.Item2, 14, true), Ui.Small(preset.Item3));
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Padding = new Thickness(12, 10, 12, 10);
            choices.Children.Add(button);
        }
        async Task ReloadSaved(string? selected = null)
        {
            var themes = await api.CallAsync<List<Dictionary<string, JsonElement>>>("list_themes") ?? new();
            custom.Fill(themes.Select(theme => theme["name"].GetString() ?? ""), selected);
        }
        var load = Ui.Btn("应用方案", BtnKind.Normal, (_, _) => PageBase.Run(() => Change(async () =>
        {
            if (custom.Str().Length == 0) throw new InvalidOperationException("请先选择一个已保存的方案。");
            await api.CallAsync("load_theme", new { name = custom.Str() });
        })));
        restore.Click += (_, _) => PageBase.Run(() => Change(async () => { if (undo == null) return; await api.CallAsync("apply_appearance", new { settings = undo }); undo = null; }, false));
        save.Click += (_, _) => PageBase.Run(async () =>
        {
            string? name = await Dlg.Prompt("保存外观方案", "方案名称", "我的外观");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (custom.Items.Cast<object>().Any(item => Themes._sanitize(item.ToString()) == Themes._sanitize(name)) &&
                !await Dlg.Confirm("覆盖外观方案", "已有同名方案，替换它的外观设置？", "覆盖", "取消")) return;
            await api.CallAsync("save_theme", new { name }); await ReloadSaved(name); status.Text = "已保存「" + name + "」。";
        }, "方案未能保存");
        await ReloadSaved();
        layer = Dlg.Panel("外观方案", Ui.V(12, status, choices, Ui.Sep(), Ui.Field("我的方案", new PageHeader(custom, load)),
            Ui.H(8, Ui.Btn("调整材质…", BtnKind.Ghost, (_, _) => PageBase.Run(MaterialDialog.Show)),
                Ui.Btn("壁纸来源…", BtnKind.Ghost, (_, _) => PageBase.Run(WallpaperDialog.Show)))), 620,
            footer: actions, canDismiss: () => !busy);
    }
}
