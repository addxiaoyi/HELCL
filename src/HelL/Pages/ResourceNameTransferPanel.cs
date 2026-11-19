using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

internal sealed class ResourceNameTransferPanel : ContentControl
{
    private sealed class ImportRow
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Incoming { get; set; } = "";
        public string Current { get; set; } = "";
        public List<string> Aliases { get; set; } = new();
        public List<string> CurrentAliases { get; set; } = new();
        public string Status { get; set; } = "";
    }
    private sealed class Preview { public string Token { get; set; } = ""; public List<ImportRow> Rows { get; set; } = new(); }
    private sealed class Saved { public int Count { get; set; } }
    private readonly TextBlock _status = Ui.Small("").Wrap();
    private static readonly string[] Actions = { "keep", "replace", "merge" };

    internal ResourceNameTransferPanel()
    {
        var import = Ui.Btn(I18n.L("导入自定义词表"), BtnKind.Soft, (_, _) => PageBase.Run(Import));
        var export = Ui.Btn(I18n.L("导出自定义词表"), BtnKind.Ghost, (_, _) => PageBase.Run(Export));
        var actions = Ui.H(8, import, export); actions.Wrap = true;
        Content = Ui.V(8, Ui.Txt(I18n.L("自定义词表"), 14, true),
            Ui.Muted(I18n.L("导出手动设置的名称和别名；导入前可逐项处理冲突。" )).Wrap(), actions, _status);
    }

    private async Task Import()
    {
        string? path = Dlg.PickFile("JSON (*.json)|*.json", I18n.L("导入自定义词表"));
        if (path != null) await ShowImport(path);
    }

    private async Task Export()
    {
        string? path = Dlg.SaveFile("JSON (*.json)|*.json", "HelL-resource-names.json", I18n.L("导出自定义词表"));
        if (path == null) return;
        var saved = await AppServices.Client.CallAsync<Saved>("export_resource_names", new { path });
        _status.Text = I18n.L("已导出 {0} 项自定义名称。", saved?.Count ?? 0);
    }

    private async Task ShowImport(string path)
    {
        var preview = await AppServices.Client.CallAsync<Preview>("preview_resource_names", new { path })
            ?? throw new InvalidOperationException(I18n.L("没有收到词表预览。"));
        var choices = preview.Rows.Where(row => row.Status == "conflict").ToDictionary(row => row.Key, _ => "keep");
        var list = Ui.VirtualList<ImportRow>(Row, rowSpacing: 8); list.ItemsSource = preview.Rows; list.MaxHeight = 340;
        var all = Ui.Combo(new[] { I18n.L("保留本机"), I18n.L("使用导入名称"), I18n.L("保留名称并合并别名") });
        all.SelectedIndex = 0;
        all.SelectionChanged += (_, _) =>
        {
            if (all.SelectedIndex < 0) return;
            foreach (string key in choices.Keys.ToArray()) choices[key] = Actions[all.SelectedIndex];
            list.Items.Refresh();
        };
        var counts = Ui.Muted(I18n.L("新增 {0} 项 · 冲突 {1} 项 · 相同 {2} 项", preview.Rows.Count(row => row.Status == "new"),
            choices.Count, preview.Rows.Count(row => row.Status == "same"))).Wrap();
        var body = Ui.V(12, counts, Ui.Small(I18n.L("所有冲突的默认处理")), all, list,
            Ui.Small(I18n.L("合并保留本机名称，将导入名称和别名加入搜索；确认后自动备份原词表。" )).Wrap());
        await Dlg.EditAsync(I18n.L("词表导入预览"), body, async () =>
        {
            var saved = await AppServices.Client.CallAsync<Saved>("import_resource_names", new { token = preview.Token, choices });
            _status.Text = I18n.L("已导入或更新 {0} 项，重新搜索即可生效。", saved?.Count ?? 0);
        }, I18n.L("确认导入"), 720);

        UIElement Row(ImportRow row)
        {
            var heading = Ui.Txt(row.Name, 13, true).Wrap();
            var incoming = Ui.Small(I18n.L("导入：{0}", row.Incoming) + "\n" + string.Join("、", row.Aliases)).Wrap();
            if (row.Status != "conflict") return Ui.V(4, heading, incoming, Ui.Small(I18n.L(row.Status == "new" ? "新增" : "相同，跳过")));
            var choice = Ui.Combo(new[] { I18n.L("保留本机"), I18n.L("使用导入名称"), I18n.L("保留名称并合并别名") });
            choice.SelectedIndex = Array.IndexOf(Actions, choices[row.Key]);
            choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) choices[row.Key] = Actions[choice.SelectedIndex]; };
            return Ui.V(4, heading, Ui.Small(I18n.L("本机：{0}", row.Current) + "\n" + string.Join("、", row.CurrentAliases)).Wrap(), incoming, choice);
        }
    }
}
