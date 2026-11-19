using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Plugins;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class LaunchPage
{
    private int _pluginRevision = -1;
    private static bool IsRepeatable(string type) => type == "notes" || type == "clock" || type == "links" || type == "image" || type == "system" || PluginManifest.IsModuleType(type);
    private IEnumerable<(string Key, string Title)> AvailableModules()
    {
        foreach (var type in CardTypes) yield return type;
        foreach (var type in new[] { ("clock", "时钟与日期"), ("links", "自定义链接"), ("image", "照片与截图"), ("system", "存储与内存") }) yield return type;
        foreach (var type in new[] { ("screenshots", "实例截图"), ("servers", "服务器状态") }) yield return type;
        foreach (var plugin in LauncherPlugins.Installed().Where(plugin => plugin.Enabled && plugin.Error.Length == 0))
            foreach (var module in plugin.Manifest.Modules) yield return ("plugin:" + plugin.Manifest.Id + ":" + module.Id, module.Title);
    }
    private string ModuleTitle(string type) => AvailableModules().FirstOrDefault(module => module.Key == type).Title ?? "扩展模块";
    private static Dictionary<string, string> ModuleSettings(DashCard card)
    {
        var values = new Dictionary<string, string>();
        if (card.Settings is { } settings)
            foreach (var property in settings.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.String) values[property.Name] = property.Value.GetString() ?? "";
        return values;
    }
    private static void SetModuleSettings(DashCard card, Dictionary<string, string> values)
    {
        var merged = new Dictionary<string, object>();
        if (card.Settings is { } previous) foreach (var property in previous.EnumerateObject()) merged[property.Name] = property.Value.Clone();
        foreach (var pair in values) merged[pair.Key] = pair.Value;
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(merged)); card.Settings = json.RootElement.Clone();
    }
    private UIElement? BuildModule(DashCard card)
    {
        string kind = card.Type; var values = ModuleSettings(card);
        if (PluginManifest.IsModuleType(kind))
        {
            var installed = LauncherPlugins.Find(kind);
            if (!installed.HasValue) return Ui.V(12, Ui.Muted("此扩展未安装或已停用。布局和内容已保留。").Wrap(), Ui.Btn("管理插件", BtnKind.Ghost, (_, _) => Win?.Navigate("plugins")));
            kind = installed.Value.Module.Kind;
            foreach (var pair in installed.Value.Module.Settings) if (!values.ContainsKey(pair.Key)) values[pair.Key] = pair.Value;
            if (values.TryGetValue("image", out string image) && PluginManifest.SafeAsset(image)) values["image"] = Path.Combine(installed.Value.Plugin.Directory, image.Replace('/', Path.DirectorySeparatorChar));
        }
        string Value(string key, string fallback = "") => values.TryGetValue(key, out var value) ? value : fallback;
        switch (kind)
        {
            case "notes":
                var notes = Ui.Multi("记录坐标、计划或待办…", height: double.NaN); notes.VerticalAlignment = VerticalAlignment.Stretch;
                notes.Text = Value("text", card.Type == "notes" ? Win?.Prefs.Notes ?? "" : "");
                notes.TextChanged += (_, _) =>
                {
                    var current = _dash.Layout.Items.FirstOrDefault(item => item.Id == card.Id); if (current == null) return;
                    var settings = ModuleSettings(current); settings["text"] = notes.Text; SetModuleSettings(current, settings); PersistLayout();
                };
                return notes;
            case "clock": return LiveModule(TimeSpan.FromSeconds(1), () => DateTime.Now.ToString(Value("format") == "12" ? "hh:mm tt" : "HH:mm"), () => DateTime.Now.ToString("yyyy 年 M 月 d 日 · dddd"));
            case "system": return LiveModule(TimeSpan.FromSeconds(10), () => (GC.GetTotalMemory(false) / 1048576d).ToString("0") + " MB", () =>
            {
                var drive = new DriveInfo(Path.GetPathRoot(Utils.ROOT)!);
                return "启动器托管内存 · 磁盘可用 " + (drive.AvailableFreeSpace / 1073741824d).ToString("0.0") + " GB";
            });
            case "links":
                string url = Value("url"), label = Value("label", "设置一个常用链接");
                return Ui.V(10, Ui.Txt(Value("title", "常用链接"), 14, true).Wrap(), Ui.Muted(PluginManifest.ValidUrl(url) ? new Uri(url).Host : "编辑布局 → 模块设置，添加自己的链接。").Wrap(),
                    Ui.Btn(label, BtnKind.Ghost, (_, _) => Run(async () =>
                    {
                        if (url.Length == 0) { ConfigureModule(card); return; }
                        if (!PluginManifest.ValidUrl(url)) throw new InvalidDataException("链接需要使用 HTTPS。");
                        await Task.CompletedTask; Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    })));
            case "image":
                string path = Value("image");
                var picture = new LocalPicture(double.NaN, double.NaN, path.Length > 0 ? "图片暂时无法读取，请在模块设置中重新选择。" : "在模块设置中选择照片或游戏截图");
                if (path.Length > 0) _ = picture.SetPath(path, Path.GetDirectoryName(Path.GetFullPath(path))!, Value("title", "自定义图片"));
                return picture;
            default: return BuildCard(kind);
        }
    }
    private static UIElement LiveModule(TimeSpan interval, Func<string> primary, Func<string> secondary)
    {
        var headline = Ui.Txt("", 32, true); var detail = Ui.Muted("").Wrap(); var body = Ui.V(4, headline, detail);
        var timer = new DispatcherTimer { Interval = interval };
        void Refresh()
        {
            try { headline.Text = primary(); detail.Text = secondary(); }
            catch (IOException ex) { detail.Text = "暂时无法读取磁盘信息。"; Smoke.Note("dashboard-system", ex.Message); }
        }
        timer.Tick += (_, _) => Refresh(); body.Loaded += (_, _) => { Refresh(); timer.Start(); }; body.Unloaded += (_, _) => timer.Stop();
        return body;
    }
    private void ShowModuleLibrary()
    {
        var rows = Ui.V(0); var search = Ui.Input("搜索模块");
        var status = Ui.Small("内置模块与已启用的插件模块");
        void Render()
        {
            rows.Children.Clear();
            foreach (var module in AvailableModules().Where(module => module.Title.IndexOf(search.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
            {
                bool exists = _dash.Has(module.Key) && !IsRepeatable(module.Key);
                var add = Ui.Btn(exists ? "已添加" : "添加", BtnKind.Ghost, (_, _) => { _dash.AddCard(module.Key); Run(FillCardData); Render(); }); add.IsEnabled = !exists;
                rows.Children.Add(new PageHeader(Ui.V(2, Ui.Txt(module.Title, 13, true), Ui.Small(IsRepeatable(module.Key) ? "支持多个独立模块" : "启动器内置模块")), add).M(0, 10, 0, 10));
            }
            if (rows.Children.Count == 0) rows.Children.Add(Ui.Muted("没有匹配的模块，试试其他关键词。"));
        }
        search.TextChanged += (_, _) => Render(); Render();
        var scroll = Ui.Scroll(rows); scroll.MaxHeight = 360;
        Dlg.Layer? layer = null;
        var market = Ui.Btn("打开插件市场", BtnKind.Ghost, (_, _) => { layer?.Close(); Win?.Navigate("plugins"); });
        layer = Dlg.Panel("模块库", Ui.V(12, search, status, scroll, market), 600);
    }
    private void ConfigureModule(DashCard card) => Run(() => EditModule(card), "模块设置未保存");
    private async Task EditModule(DashCard card)
    {
        int generation = _layoutGen;
        var values = ModuleSettings(card); var installed = LauncherPlugins.Find(card.Type); string kind = installed?.Module.Kind ?? card.Type;
        if (installed.HasValue) foreach (var pair in installed.Value.Module.Settings) if (!values.ContainsKey(pair.Key)) values[pair.Key] = pair.Value;
        string Value(string key, string fallback = "") => values.TryGetValue(key, out var value) ? value : fallback;
        var title = Ui.Input("模块名称", Value("title", ModuleTitle(card.Type)));
        title.MaxLength = 80;
        System.Windows.Automation.AutomationProperties.SetName(title, "模块名称");
        var width = Ui.Sld(15, 100, Math.Round(card.W * 100), 1); var height = Ui.Sld(130, 1000, Math.Round(card.H * DashboardGeometry.ReferenceHeight), 10);
        var size = Ui.Small(""); void SizeLabel() => size.Text = $"宽度 {width.Value:0}% · 高度 {height.Value:0} px（窄窗口会自动避让）";
        width.ValueChanged += (_, _) => SizeLabel(); height.ValueChanged += (_, _) => SizeLabel(); SizeLabel();
        var body = Ui.V(10, Ui.Muted("名称"), title, size, width, height);
        if (kind == "quick") body.Children.Add(Ui.Btn("选择快捷入口", BtnKind.Ghost, (_, _) => Run(PickQuickAsync)));
        TextBox? url = null, label = null; CheckBox? clock = null; TextBox? image = null;
        if (kind == "links")
        {
            label = Ui.Input("链接文字", Value("label")); url = Ui.Input("https://…", Value("url")); body.Children.Add(Ui.V(6, Ui.Muted("链接文字与地址"), label, url));
        }
        if (kind == "image")
        {
            image = Ui.Input("本地图片路径", Value("image")); image.IsReadOnly = true;
            body.Children.Add(Ui.V(6, image, Ui.Btn("选择图片", BtnKind.Ghost, (_, _) => { string? path = Dlg.PickFile("图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif", "选择模块图片"); if (path != null) image.Text = path; })));
        }
        if (kind == "clock") { clock = Ui.Check("使用 12 小时制", Value("format") == "12"); body.Children.Add(clock); }
        if (!await Dlg.Ask("模块设置", body, "保存", width: 560)) return;
        if (generation != _layoutGen) { Toast("布局已切换", "请在当前布局中重新打开模块设置。", ToastKind.Warning); return; }
        if (url != null && url.Text.Length > 0 && !PluginManifest.ValidUrl(url.Text.Trim())) { Toast("链接未保存", "请使用完整的 HTTPS 地址。", ToastKind.Warning); return; }
        values["title"] = title.Text.Trim(); if (url != null) { values["url"] = url.Text.Trim(); values["label"] = label!.Text.Trim(); }
        if (image != null) values["image"] = image.Text; if (clock != null) values["format"] = clock.IsChecked == true ? "12" : "24";
        _dash.ChangeCard(card, current => { SetModuleSettings(current, values); current.W = width.Value / 100; current.X = Math.Min(current.X, 1 - current.W); current.H = height.Value / DashboardGeometry.ReferenceHeight; });
    }
}
