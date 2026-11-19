using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public static class DependencyInstallDialog
{
    public sealed class Target
    {
        public string Instance { get; set; } = "";
        public string Version { get; set; } = "";
        public string Label { get; set; } = "";
        public override string ToString() => Label;
    }

    public static async Task<string?> Show(CatalogDependency dependency)
    {
        var targets = await AppServices.Client.CallAsync<List<Target>>("dependency_targets") ?? new List<Target>();
        if (targets.Count == 0) { await Dlg.Confirm(I18n.L("没有可安装的目标"), I18n.L("请先安装游戏版本和模组加载器。"), I18n.L("知道了")); return null; }
        ComboBox select = Ui.Combo();
        foreach (var target in targets) select.Items.Add(target);
        select.SelectedIndex = 0;
        if (!await Dlg.Ask(I18n.L("安装依赖 · {0}", dependency.DisplayName), Ui.V(10,
            Ui.Muted(I18n.L("选择目标实例与游戏版本，将自动检查加载器兼容性。")).Wrap(), select), I18n.L("检查兼容性"))) return null;
        var chosen = (Target)select.SelectedItem;
        DependencyPlan plan;
        using (Dlg.Busy(I18n.L("正在查询适配文件与依赖…")))
            plan = await AppServices.Client.CallAsync<DependencyPlan>("prepare_dependency", new
            {
                source = dependency.Source, project = dependency.ProjectId, pinned_version = dependency.VersionId,
                relation = dependency.Kind, instance = chosen.Instance, version = chosen.Version
            }) ?? throw new InvalidOperationException(I18n.L("没有收到安装计划。"));
        var body = Ui.V(8, Ui.Txt(plan.Name, 16, true).Wrap(), Ui.Muted(plan.Version).Wrap(),
            Ui.Txt(chosen.Label).Wrap(), Ui.Small(plan.GameVersion + " · " + plan.Loader),
            Ui.Muted(plan.Filename).Wrap(), Ui.Small(plan.Destination).Wrap(),
            Ui.Muted(I18n.L("仅安装此依赖文件，校验通过后写入；已有同名文件不会覆盖。其他依赖可在详情中继续查看。")).Wrap());
        if (!await Dlg.Ask(I18n.L("确认安装依赖"), body, I18n.L("确认安装"), width: 620)) return null;
        string task = await AppServices.Client.StartTaskAsync("install_dependency", new { token = plan.Token });
        AppServices.Toast(I18n.L("已加入下载任务"), plan.Name);
        return task;
    }
}
