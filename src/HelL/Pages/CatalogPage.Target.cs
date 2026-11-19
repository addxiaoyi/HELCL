using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class CatalogPage
{
    private readonly CheckBox _followTarget = Ui.Check("跟随实例", true);
    private readonly ComboBox _targetVersion = Ui.Combo(placeholder: "选择匹配版本");
    private readonly ComboBox _loader = Ui.Combo(new[] { "全部加载器", "Fabric", "Forge", "NeoForge", "Quilt" }, "全部加载器", 116);
    private readonly TextBlock _targetStatus = Ui.Small("");
    private List<CatalogTarget> _targets = new();
    private int _targetGeneration;
    private bool _targetSync;
    private string _loadedTargetInstance = "";

    private UIElement MatchingControls()
    {
        System.Windows.Automation.AutomationProperties.SetName(_targetVersion, "匹配的游戏版本");
        System.Windows.Automation.AutomationProperties.SetName(_loader, "加载器筛选");
        _loader.Visibility = _kind.Key is "mod" or "modpack" ? Visibility.Visible : Visibility.Collapsed;
        _followTarget.IsChecked = Win?.Prefs.CatalogFollowTarget != false;
        _followTarget.Click += (_, _) => Run(async () =>
        {
            if (Win != null) { Win.Prefs.CatalogFollowTarget = _followTarget.IsChecked == true; Win.SaveUiPrefs(); }
            ApplyTarget(); await ReloadCurrentAsync();
        });
        _targetVersion.SelectionChanged += (_, _) =>
        {
            if (_targetSync) return;
            ApplyTarget(); if (_followTarget.IsChecked == true) Run(ReloadCurrentAsync);
        };
        _loader.SelectionChanged += (_, _) =>
        {
            if (_targetSync || _sync) return;
            ManualTarget(); Run(ReloadCurrentAsync);
        };
        _version.PreviewKeyDown += (_, e) => { if (!_targetSync && e.Key != Key.Tab && e.Key != Key.Escape) ManualTarget(); };
        _targetStatus.TextWrapping = TextWrapping.Wrap;
        return Ui.V(6, new PageHeader(_targetVersion, Ui.H(10, _followTarget.VCenter(), _loader)), _targetStatus);
    }

    private string LoaderFilter() => (_kind.Key is "mod" or "modpack") && _loader.SelectedIndex > 0 ? _loader.Str().ToLowerInvariant() : "";

    private void ManualTarget()
    {
        if (_targetSync) return;
        _followTarget.IsChecked = false; _targetStatus.Text = "手动筛选；勾选「跟随实例」可恢复自动匹配。";
        if (Win != null) { Win.Prefs.CatalogFollowTarget = false; Win.SaveUiPrefs(); }
    }

    private async Task ReloadTarget()
    {
        int generation = ++_targetGeneration;
        string instance = CurrentInstance();
        _targetStatus.Text = "正在读取实例版本…";
        try
        {
            var targets = await Api.CallAsync<List<CatalogTarget>>("catalog_targets", new { instance }) ?? new();
            if (generation != _targetGeneration || instance != CurrentInstance()) return;
            _targetSync = true;
            string selected = _loadedTargetInstance == instance ? _targetVersion.Str() : "";
            _targets = targets; _loadedTargetInstance = instance;
            _targetVersion.Fill(new[] { "自动匹配" }.Concat(targets.Select(target => target.Version)), selected);
            _targetVersion.IsEnabled = targets.Count > 0;
            _targetSync = false; ApplyTarget();
        }
        catch (Exception error)
        {
            if (generation != _targetGeneration) return;
            _targetSync = false; _targets.Clear();
            _targetStatus.Text = "版本匹配失败：" + error.Message + " 可关闭跟随实例后手动筛选。";
            if (_followTarget.IsChecked == true) SetTargetFilters("", "");
            HelL.Core.Launcher.Guard.WriteLog("catalog-target", error.ToString());
        }
    }

    private void SetTargetFilters(string version, string loader)
    {
        _targetSync = true; bool sync = _sync; _sync = true;
        try
        {
            _version.SelectedItem = null; _version.Text = version.Length > 0 ? version : I18n.L("全部");
            _loader.SelectedIndex = loader switch { "fabric" => 1, "forge" => 2, "neoforge" => 3, "quilt" => 4, _ => 0 };
        }
        finally { _sync = sync; _targetSync = false; }
    }

    private void ApplyTarget()
    {
        if (_followTarget.IsChecked != true) { _targetStatus.Text = "手动筛选；勾选「跟随实例」可恢复自动匹配。"; return; }
        var target = _targets.FirstOrDefault(row => row.Version == _targetVersion.Str()) ?? CatalogTarget.Common(_targets);
        SetTargetFilters(target.GameVersion, target.Loader);
        _targetStatus.Text = target.Error.Length > 0 ? target.Error : "匹配 Minecraft " + target.GameVersion + " · " + (target.Loader == "vanilla" ? "原版" : target.Loader);
    }
}
