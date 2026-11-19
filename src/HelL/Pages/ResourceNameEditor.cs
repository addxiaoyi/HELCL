using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

internal static class ResourceNameEditor
{
    private sealed class SavedName
    {
        public string Chinese { get; set; } = "";
        public string[] Aliases { get; set; } = Array.Empty<string>();
        public string Error { get; set; } = "";
    }

    internal static async Task<CatalogItem?> Show(CatalogItem project)
    {
        var saved = await AppServices.Client.CallAsync<SavedName>("catalog_custom_name", new { source = project.Source, slug = project.Slug ?? "", id = project.IdValue?.ToString() ?? "" })
            ?? throw new InvalidOperationException(I18n.L("没有收到自定义名称。"));
        if (saved.Error.Length > 0) throw new InvalidOperationException(saved.Error);
        var chinese = Ui.Input(I18n.L("留空使用社区名称"), saved.Chinese);
        chinese.MaxLength = 120;
        var aliases = Ui.Input(I18n.L("每行一个别名，也支持英文简称"), string.Join("\n", saved.Aliases));
        aliases.AcceptsReturn = true; aliases.TextWrapping = System.Windows.TextWrapping.Wrap; aliases.MinHeight = 100; aliases.MaxHeight = 180; aliases.MaxLength = 1700;
        aliases.VerticalContentAlignment = System.Windows.VerticalAlignment.Top;
        System.Windows.Automation.AutomationProperties.SetName(chinese, "自定义中文名");
        System.Windows.Automation.AutomationProperties.SetName(aliases, "自定义搜索别名");
        var reset = Ui.Btn(I18n.L("恢复默认名称与别名"), BtnKind.Ghost, (_, _) => { chinese.Text = ""; aliases.Text = ""; });
        var body = Ui.V(9, Ui.Muted(project.Name).Wrap(), Ui.Small(I18n.L("自定义中文名")), chinese,
            Ui.Small(I18n.L("自定义搜索别名")), aliases,
            Ui.Muted(I18n.L("仅保存到本机；不会改变项目 ID、安装文件名或社区词表。清空两项并保存可恢复默认。" )).Wrap(), reset.Left());
        CatalogItem? updated = null;
        await Dlg.EditAsync(I18n.L("修改中文名与别名"), body, async () =>
        {
            updated = await AppServices.Client.CallAsync<CatalogItem>("save_catalog_name", new { project, chinese = chinese.Text,
                aliases = aliases.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).ToArray() })
                ?? throw new InvalidOperationException(I18n.L("没有收到保存结果。"));
        }, width: 600);
        return updated;
    }
}
