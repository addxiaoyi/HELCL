using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Services;

namespace HelL.Pages;

public static class MaterialDialog
{
    public static Task Show()
    {
        int originalPage = Glass.Transparency, originalCards = Glass.CardTransparency, originalControls = Glass.ControlTransparency,
            originalPopups = Glass.PopupTransparency, originalFrost = Glass.Frost;
        var page = Slider("窗口透明度", originalPage, 100);
        var cards = Slider("卡片透明度", originalCards, 100);
        var controls = Slider("控件透明度", originalControls, 100);
        var popups = Slider("弹窗透明度", originalPopups, 100);
        var frost = Slider("壁纸磨砂强度", originalFrost, 60);
        var status = Ui.Muted("").Wrap(); status.Visibility = Visibility.Collapsed;
        var preview = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        bool closed = false, saved = false, saving = false;

        void Paint()
        {
            if (!closed) Glass.SetMaterial((int)page.Value, (int)frost.Value, (int)cards.Value, (int)controls.Value, (int)popups.Value);
        }
        void Set(int background, int panel, int field, int popup, int blur)
        {
            page.Value = background; cards.Value = panel; controls.Value = field; popups.Value = popup; frost.Value = blur;
            preview.Stop(); Paint();
        }
        preview.Tick += (_, _) => { preview.Stop(); Paint(); };
        foreach (var slider in new[] { page, cards, controls, popups, frost }) slider.ValueChanged += (_, _) => { if (!preview.IsEnabled) preview.Start(); };
        frost.IsEnabled = Wallpaper.Active && Backdrop.EffectsEnabled;
        var presets = Ui.H(6,
            Ui.Btn(I18n.L("完全透明"), BtnKind.Normal, (_, _) => Set(100, 100, 100, 100, 0)),
            Ui.Btn(I18n.L("清透玻璃"), BtnKind.Normal, (_, _) => Set(82, 68, 42, 10, 18)),
            Ui.Btn(I18n.L("柔和磨砂"), BtnKind.Normal, (_, _) => Set(38, 52, 24, 0, 30)),
            Ui.Btn(I18n.L("纯色外观"), BtnKind.Normal, (_, _) => Set(0, 0, 0, 0, 0)));
        presets.Wrap = true; presets.IsEnabled = Backdrop.EffectsEnabled;
        var fields = Ui.V(14, presets,
            Ui.Muted(I18n.L("100% 不叠加底色。文字、图标和主要操作保持清晰；各层可以分别调整。")).Wrap(),
            Field("窗口透明度", page), Field("卡片透明度", cards), Field("控件透明度", controls), Field("弹窗透明度", popups),
            Field("壁纸磨砂强度", frost, false),
            Ui.Small(I18n.L(Wallpaper.Active ? "完全透明预设同时关闭壁纸遮罩和模糊，保留原壁纸设置。" : "当前透出系统背景；桌面直透模式不使用 Mica 或 Acrylic 模糊。")).Wrap(), status);
        fields.IsEnabled = Backdrop.EffectsEnabled;
        var reset = Ui.Btn(I18n.L("恢复默认"), BtnKind.Ghost, (_, _) => Set(38, 52, 24, 0, 30));
        var save = Ui.Btn(I18n.L("保存材质"), BtnKind.Primary); var cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost);
        var actions = new PageHeader(reset, Ui.H(8, cancel, save));
        Dlg.Layer? layer = null;
        layer = Dlg.Panel(I18n.L("玻璃实时预览"), fields, 610, onClose: () =>
        {
            closed = true; preview.Stop();
            if (!saved) Glass.SetMaterial(originalPage, originalFrost, originalCards, originalControls, originalPopups);
        }, footer: actions, dimBackground: false, canDismiss: () => !saving);
        cancel.Click += (_, _) => layer.Dismiss();
        save.Click += async (_, _) =>
        {
            if (saving || closed) return;
            saving = true; fields.IsEnabled = actions.IsEnabled = false; preview.Stop(); Paint();
            try
            {
                await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> {
                    ["ui_glass_transparency"] = (int)page.Value, ["ui_glass_frost"] = (int)frost.Value,
                    ["ui_glass_cards"] = (int)cards.Value, ["ui_glass_controls"] = (int)controls.Value, ["ui_glass_popups"] = (int)popups.Value } });
                saved = true; closed = true; preview.Stop(); layer.Close();
            }
            catch (Exception ex)
            {
                Smoke.Note("material-save", ex.ToString());
                status.Text = I18n.L("材质未能保存：") + ex.Message; status.Visibility = Visibility.Visible;
            }
            finally { saving = false; fields.IsEnabled = Backdrop.EffectsEnabled; actions.IsEnabled = true; }
        };
        if (!Backdrop.EffectsEnabled) { status.Text = I18n.L("系统透明效果已关闭，当前使用实色外观。"); status.Visibility = Visibility.Visible; }
        return Task.CompletedTask;
    }

    private static Slider Slider(string label, int value, int maximum)
    {
        var slider = Ui.Sld(0, maximum, value, 1);
        AutomationProperties.SetName(slider, I18n.L(label));
        return slider;
    }

    private static UIElement Field(string label, Slider slider, bool percentage = true)
    {
        var value = Ui.Small(""); value.MinWidth = 42; value.TextAlignment = TextAlignment.Right;
        void Update() => value.Text = slider.Value.ToString("0") + (percentage ? "%" : "");
        slider.ValueChanged += (_, _) => Update(); Update();
        var row = Ui.G(null, "*,Auto"); row.Add(slider.VCenter(), 0); row.Add(value.VCenter().M(12, 0, 0, 0), 0, 1);
        return Ui.Field(I18n.L(label), row, labelWidth: 112);
    }
}
