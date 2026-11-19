using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class CatalogPage
{
    private sealed class LanguageIndex
    {
        public int Count { get; set; }
        public int CommunityCount { get; set; }
        public string Updated { get; set; } = "";
        public string Error { get; set; } = "";
        public string Source { get; set; } = "";
    }

    private async Task ShowLanguageIndex()
    {
        var status = Ui.Muted(I18n.L("正在读取中文资源映射…")).Wrap();
        var update = Ui.Btn(I18n.L("更新中文映射"), BtnKind.Primary);
        async Task Refresh(bool force)
        {
            update.IsEnabled = false;
            try
            {
                var index = await Api.CallAsync<LanguageIndex>("catalog_language_status", new { refresh = force });
                if (index == null) throw new InvalidOperationException(I18n.L("没有收到中文映射状态。"));
                status.Text = I18n.L("已收录 {0} 条映射，其中社区记录 {1} 条。", index.Count, index.CommunityCount)
                    + "\n" + index.Source + (index.Updated.Length > 0 ? " · " + index.Updated : "")
                    + (index.Error.Length > 0 ? "\n" + index.Error : "");
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { update.IsEnabled = true; }
        }
        update.Click += async (_, _) => await Refresh(true);
        Dlg.Panel(I18n.L("中文资源映射"), Ui.V(12, status, Ui.Muted(I18n.L("支持模组、整合包、数据包、资源包、光影和世界的中文搜索。未收录名称保留原名；本地译名与社区名称会标明来源。" )).Wrap(), update.Left(), new ResourceNameTransferPanel()), 680);
        await Refresh(false);
    }

    private sealed class SummaryReply
    {
        public List<CatalogItem> Rows { get; set; } = new List<CatalogItem>();
        public string Error { get; set; } = "";
    }

    private readonly Dictionary<string, (CatalogItem Item, ResourceSummary View)> _summaries = new Dictionary<string, (CatalogItem, ResourceSummary)>();
    private readonly Dictionary<string, ResourceTitle> _resourceTitles = new();
    private readonly TextBlock _summaryProgress = Ui.Txt("", 11, false, "B.InkMuted");
    private bool _summaryBusy;
    private int _summaryGeneration;

    private static string SummaryText(CatalogItem item) => !string.IsNullOrWhiteSpace(item.Description) ? item.Description
        : I18n.L(item.OriginalDescription.Length > 0 ? "英文简介待翻译 · 详情中可查看原文" : "作者暂未提供简介");

    private async Task LocalizeResults(int generation)
    {
        if (I18n.Current != "zh_CN" || generation != _summaryGeneration) return;
        var targets = _results.Items.OfType<CatalogItem>().Where(item => item.Localization == "英文简介待翻译" || item.ChineseName.Length == 0 && !ResourceNames.ContainsChinese(item.Name)).ToList();
        if (targets.Count == 0) return;
        await TranslateSummaries(targets, generation);
    }

    private async Task TranslateSummaries(List<CatalogItem> targets, int generation)
    {
        if (_summaryBusy) { _summaryProgress.Text = I18n.L("正在翻译其他简介，请稍后重试。"); return; }
        _summaryBusy = true;
        _summaryProgress.Visibility = Visibility.Visible;
        _summaryProgress.ToolTip = null;
        _summaryProgress.Text = I18n.L("正在本地翻译 {0} 项资源…", targets.Count);
        try
        {
            int completed = 0;
            for (int offset = 0; offset < targets.Count; offset += 2)
            {
                if (generation != _summaryGeneration) return;
                var batch = targets.Skip(offset).Take(2).ToList();
                foreach (var item in batch) if (_summaries.TryGetValue(item.Key, out var row)) row.View.Begin();
                var response = await Api.CallAsync<SummaryReply>("catalog_summaries", new { projects = batch }) ?? throw new InvalidOperationException(I18n.L("中文摘要暂不可用。"));
                if (generation != _summaryGeneration) return;
                foreach (var item in batch)
                {
                    var translated = response.Rows.FirstOrDefault(row => row.Key == item.Key) ?? item;
                    item.Description = translated.Description; item.Localization = translated.Localization;
                    item.ChineseName = translated.ChineseName; item.NameLocalization = translated.NameLocalization;
                    if (_resourceTitles.TryGetValue(item.Key, out var title)) title.Apply(item);
                    if (_summaries.TryGetValue(item.Key, out var row)) row.View.Apply(item, response.Error);
                    if (translated.Localization != "英文简介待翻译" &&
                        (translated.ChineseName.Length > 0 || ResourceNames.ContainsChinese(translated.Name))) completed++;
                }
                if (response.Error.Length > 0)
                {
                    // A missing local service affects the whole queue; retry only when requested.
                    foreach (var item in targets.Skip(offset + batch.Count))
                        if (_summaries.TryGetValue(item.Key, out var row)) row.View.Apply(item, response.Error);
                    _summaryProgress.Text = I18n.L("本地翻译已暂停 · 可重试或查看原文");
                    _summaryProgress.ToolTip = response.Error;
                    return;
                }
                _summaryProgress.Text = I18n.L("资源汉化 {0} / {1} 已完成", completed, targets.Count);
            }
            _summaryProgress.Text = I18n.L("资源汉化 {0} / {1} 已完成", completed, targets.Count) + (completed < targets.Count ? I18n.L(" · 可逐项重试或查看原文") : "");
        }
        catch (Exception ex)
        {
            if (generation == _summaryGeneration)
            {
                _summaryProgress.Text = I18n.L("中文摘要暂不可用，请重试或查看原文。");
                foreach (var item in targets) if (_summaries.TryGetValue(item.Key, out var row)) row.View.Apply(item, ex.Message);
            }
            Smoke.Note("catalog-localize", ex.Message);
        }
        finally
        {
            _summaryBusy = false;
            if (generation != _summaryGeneration && IsVisible) _ = LocalizeResults(_summaryGeneration);
        }
    }

    private async Task ShowDetails(CatalogItem item)
    {
        var panel = new ProjectDetailsPanel(item, GameVersionFilter(), updated =>
        {
            item.ChineseName = updated.ChineseName; item.NameLocalization = updated.NameLocalization; item.SearchAliases = updated.SearchAliases;
            if (_resourceTitles.TryGetValue(item.Key, out var title)) title.Apply(item);
        });
        Dlg.Panel(item.DisplayName, panel, 760);
        await panel.EnsureLoadedAsync();
    }
}
