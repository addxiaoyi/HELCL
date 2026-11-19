using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class SettingsPage
{
    private readonly HashSet<string> _changedSettings = new HashSet<string>();
    private readonly TextBlock _saveState = Ui.Small("外观即时预览；其它设置保存后生效；语言需重启。未保存 0 项。");
    private bool _changesBound;
    private bool _refreshDeferred;

    private bool DeferSettingsRefresh()
    {
        if (_changedSettings.Count == 0) return false;
        _refreshDeferred = true;
        UpdateSaveState();
        return true;
    }

    private void HookSettingsChanges()
    {
        if (_changesBound) return;
        _changesBound = true;
        foreach (FieldInfo field in GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            object control = field.GetValue(this);
            if (field.Name == "_themePack" || field.Name == "_settingsSearch") continue;
            string name = field.Name.TrimStart('_');
            if (control is TextBox text) text.TextChanged += (_, _) => Mark(name);
            else if (control is SecretInput secret) secret.TextChanged += (_, _) => Mark("密钥");
            else if (control is ComboBox combo) combo.SelectionChanged += (_, _) => Mark(name);
            else if (control is CheckBox check) { check.Checked += (_, _) => Mark(name); check.Unchecked += (_, _) => Mark(name); }
            else if (control is Slider slider) slider.ValueChanged += (_, _) => Mark(name);
        }
    }

    private void Mark(string name)
    {
        if (_sync || _savingSettings) return;
        _changedSettings.Add(name);
        UpdateSaveState();
    }

    private void UpdateSaveState()
    {
        _saveState.Text = "外观即时预览；其它设置保存后生效；语言需重启。未保存 " + _changedSettings.Count + " 项。";
        if (_changedSettings.Count == 0) _refreshDeferred = false;
        if (_refreshDeferred) _saveState.Text += " 后台设置已变化，保留当前草稿。";
        _saveState.ToolTip = _changedSettings.Count == 0 ? "设置已同步" : "有未保存的更改，保存前保留本页输入。";
    }

    public override void OnHidden()
    {
        if (_changedSettings.Count > 0) Toast("仍有未保存设置", "更改保留在本页；返回设置并保存后才会生效。", ToastKind.Warning);
    }
}
