using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher.Plugins;
using HelL.Services;

namespace HelL.Pages;

public sealed class PluginsPage : PageBase
{
    private readonly SPanel _rows = Ui.V(0);
    private readonly TextBox _search = Ui.Input("搜索插件");
    private readonly TextBlock _status = Ui.Muted("").Wrap();
    private readonly Button _refresh = Ui.Btn("刷新", BtnKind.Ghost);
    private readonly Button _import = Ui.Btn("导入插件包", BtnKind.Ghost);
    private readonly Button _source = Ui.Btn("市场源", BtnKind.Ghost);
    private List<PluginListing> _catalog = new();
    private bool _installedOnly;
    private bool _updatesOnly;
    private bool _busy;
    public override string Title => "插件市场";
    public PluginsPage()
    {
        _search.Width = 220;
        var all = Filter("发现", false, false);
        var installed = Filter("已安装", true, false);
        var updates = Filter("可更新", true, true);
        var actions = Ui.H(8, _import, _source, _refresh); actions.Wrap = true;
        var filters = new PageHeader(Ui.H(4, all, installed, updates), _search);
        var grid = Ui.G("Auto,Auto,Auto,*");
        grid.Add(new PageHeader(Ui.V(3, Ui.H1("插件市场"), Ui.Muted("把常用工具放进你的启动页")), actions), 0);
        grid.Add(Ui.Inset(filters, 8).M(0, 18, 0, 10), 1); grid.Add(_status.M(0, 0, 0, 12), 2); grid.Add(Ui.Scroll(_rows), 3);
        Content = grid.M(UiMetrics.PageInset, 20, UiMetrics.PageInset, 20);
        _search.TextChanged += (_, _) => Render(); _refresh.Click += (_, _) => RunBusy(LoadCatalog);
        _import.Click += (_, _) => RunBusy(Import); _source.Click += (_, _) => RunBusy(EditSource);
    }
    private RadioButton Filter(string label, bool installed, bool updates)
    {
        var filter = new RadioButton { Content = label, Style = Ui.S("TabItemBtn"), GroupName = "plugin-catalog-filter", IsChecked = !installed };
        filter.Checked += (_, _) => { _installedOnly = installed; _updatesOnly = updates; Render(); };
        return filter;
    }
    protected override Task LoadAsync() => LoadCatalog();
    public override void OnShown() { if (_catalog.Count > 0) Render(); }
    private async void RunBusy(Func<Task> action)
    {
        if (_busy) return; _busy = true; _status.Text = "正在处理…"; _refresh.IsEnabled = _source.IsEnabled = _import.IsEnabled = _rows.IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { Smoke.Note("plugins", ex.ToString()); _status.Text = "未完成：" + ex.Message + " 可修正后重试。"; }
        finally { _busy = false; if (_status.Text == "正在处理…") _status.Text = "操作已完成。"; _refresh.IsEnabled = _source.IsEnabled = _import.IsEnabled = _rows.IsEnabled = true; }
    }
    private async Task LoadCatalog()
    {
        _catalog = PluginCatalog.Builtins(); _status.Text = "正在读取插件目录…"; Render();
        string source = await Api.CallAsync<string>("get_setting", new { key = "ui_plugin_catalog", @default = "" }) ?? "";
        bool localOnly = await Api.CallAsync<bool>("get_setting", new { key = "ui_plugin_local_only", @default = false });
        if (localOnly) source = ""; else if (source.Length == 0) source = PluginCatalog.DefaultUrl;
        if (source.Length == 0) _status.Text = "仅使用内置目录 · 3 款可安装扩展。可在市场源中启用公共目录。";
        else
        {
            try
            {
                var remote = await PluginCatalog.Fetch(source);
                var remoteIds = new HashSet<string>(remote.Select(row => row.Manifest.Id));
                _catalog.RemoveAll(row => remoteIds.Contains(row.Manifest.Id));
                _catalog.AddRange(remote);
                _status.Text = "已连接 " + new Uri(source).Host + " · " + remote.Count + " 款社区插件";
            }
            catch (Exception ex) { Smoke.Note("plugin-catalog", ex.ToString()); _status.Text = "市场源暂时无法读取：" + ex.Message + "。内置目录仍可使用，点击刷新重试。"; }
        }
        Render();
    }
    private void Render()
    {
        _rows.Children.Clear(); var installed = LauncherPlugins.Installed();
        var entries = _installedOnly ? installed.Select(plugin => new PluginListing { Manifest = plugin.Manifest }).ToList() : _catalog;
        foreach (var listing in entries.Where(entry => (entry.Manifest.Name + " " + entry.Manifest.Description).IndexOf(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
        {
            var local = installed.FirstOrDefault(plugin => plugin.Manifest.Id == listing.Manifest.Id);
            var available = _catalog.FirstOrDefault(entry => entry.Manifest.Id == listing.Manifest.Id);
            var manifest = available?.Manifest ?? listing.Manifest;
            bool upgrade = local != null && local.Error.Length == 0 && new Version(manifest.Version) > new Version(local.Manifest.Version);
            if (_updatesOnly && !upgrade) continue;
            string state = local == null ? "未安装" : local.Error.Length > 0 ? "需要修复" : local.Enabled ? "已启用" : "已停用";
            var info = Ui.V(4, Ui.Txt(manifest.Name, 16, true).Wrap(), Ui.Small(manifest.Author + " · " + (upgrade ? local!.Manifest.Version + " → " + manifest.Version : manifest.Version) + " · " + state).Wrap(), Ui.Muted(manifest.Description).Wrap());
            if (local?.Error.Length > 0) info.Children.Add(Ui.Muted(local.Error).Wrap());
            var actions = Ui.H(6); actions.Wrap = true;
            var detailListing = available ?? listing;
            actions.Children.Add(Ui.Btn("详情", BtnKind.Ghost, (_, _) => Dlg.Panel(manifest.Name, new PluginDetailsPanel(detailListing, () => { Render(); return Task.CompletedTask; }), 700)));
            if (local == null || upgrade || local.Error.Length > 0)
            {
                var package = available ?? listing;
                actions.Children.Add(Ui.Btn(local == null ? "安装" : upgrade ? "更新" : "重新安装", BtnKind.Primary, (_, _) => RunBusy(async () =>
                {
                    if (available == null && local != null) { await Import(); return; }
                    await PluginCatalog.Install(package, LauncherPlugins.Store); LauncherPlugins.NotifyChanged(); _status.Text = manifest.Name + " 已安装。进入启动页 → 编辑布局 → 模块库即可添加。"; Render();
                })));
            }
            if (local != null)
            {
                if (local.Error.Length == 0) actions.Children.Add(Ui.Btn(local.Enabled ? "停用" : "启用", BtnKind.Ghost, (_, _) => RunBusy(() =>
                { LauncherPlugins.Store.SetEnabled(manifest.Id, !local.Enabled); LauncherPlugins.NotifyChanged(); Render(); return Task.CompletedTask; })));
                actions.Children.Add(Ui.Btn("卸载", BtnKind.Ghost, (_, _) => RunBusy(async () =>
                {
                    if (!await Dlg.Confirm("卸载「" + manifest.Name + "」", "启动页布局与模块内容会保留，重新安装后可以恢复。", "卸载", danger: true)) return;
                    LauncherPlugins.Store.Uninstall(manifest.Id); LauncherPlugins.NotifyChanged(); Render();
                })));
            }
            var row = new Border { Child = new PageHeader(info, actions), Padding = new Thickness(0, 18, 0, 18), BorderThickness = new Thickness(0, 0, 0, 1) };
            row.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge"); _rows.Children.Add(row);
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(Ui.Muted(_updatesOnly ? "当前目录没有可用更新。点击刷新检查市场源。" : _installedOnly ? "还没有安装匹配的插件。切换到发现，或导入本地插件包。" : "没有匹配的插件。试试其他关键词。").Wrap());
    }
    private async Task Import()
    {
        string? file = Dlg.PickFile("HelL 插件包 (*.hellplugin;*.zip)|*.hellplugin;*.zip", "导入启动器插件"); if (file == null) return;
        var manifest = await Task.Run(() => LauncherPlugins.Store.InspectPackage(file));
        if (!await Dlg.Confirm("安装「" + manifest.Name + "」", manifest.Author + " · " + manifest.Version + "\n" + manifest.Description + "\n包含 " + manifest.Modules.Count + " 个启动页模块。", "安装")) return;
        await Task.Run(() => LauncherPlugins.Store.InstallPackage(file, expectedId: manifest.Id, expectedVersion: manifest.Version)); LauncherPlugins.NotifyChanged(); _status.Text = manifest.Name + " 已安装。"; Render();
    }
    private async Task EditSource()
    {
        string current = await Api.CallAsync<string>("get_setting", new { key = "ui_plugin_catalog", @default = "" }) ?? "";
        bool builtinOnly = await Api.CallAsync<bool>("get_setting", new { key = "ui_plugin_local_only", @default = false });
        if (current.Length == 0) current = PluginCatalog.DefaultUrl;
        var input = Ui.Input("https://…/catalog.json", current);
        var local = Ui.Check("仅使用内置目录（不连接网络）", builtinOnly);
        var official = Ui.Btn("使用 HelL 官方目录", BtnKind.Ghost, (_, _) => { input.Text = PluginCatalog.DefaultUrl; local.IsChecked = false; });
        if (!await Dlg.Ask("市场源", Ui.V(10, Ui.Muted("HTTPS 插件目录地址。留空恢复 HelL 官方目录。"), input, local, official.Left()), "保存")) return;
        string source = input.Text.Trim(); if (source.Length == 0) source = PluginCatalog.DefaultUrl;
        if (local.IsChecked != true) await PluginCatalog.Fetch(source);
        await Api.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_plugin_catalog"] = source, ["ui_plugin_local_only"] = local.IsChecked == true } }); await LoadCatalog();
    }
}
