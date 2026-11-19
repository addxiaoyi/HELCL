using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class TasksPage
{
    private readonly TextBox _search = Ui.Input(I18n.L("名称、状态或任务 ID"), width: 240);
    private readonly ComboBox _state = Ui.Combo(new[] { I18n.L("全部任务"), I18n.L("进行中"), I18n.L("失败"), I18n.L("已完成"), I18n.L("已取消") }, width: 150);
    private readonly SPanel _empty = Ui.Empty("\ue896", I18n.L("还没有任务"));
    private Button _reset;
    private Button _download;
    private TextBlock _emptyTitle;
    private TextBlock _emptyHint;

    private UIElement BuildFilters()
    {
        AutomationProperties.SetName(_search, I18n.L("搜索任务"));
        AutomationProperties.SetName(_state, I18n.L("任务状态"));
        _summary.TextWrapping = TextWrapping.Wrap;
        _search.TextChanged += (_, _) => ApplyFilters();
        _state.SelectionChanged += (_, _) => ApplyFilters();
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _search.Text.Length == 0) return;
            _search.Clear();
            e.Handled = true;
        };
        _reset = Ui.Btn(I18n.L("清除筛选"), BtnKind.Ghost, (_, _) => ResetFilters());
        _reset.Visibility = Visibility.Collapsed;
        _download = Ui.Btn(I18n.L("去下载版本"), BtnKind.Soft, (_, _) => Win?.Navigate("version"), "\ue7fc");
        _download.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyTitle = _empty.Children.OfType<TextBlock>().Last();
        _emptyTitle.TextWrapping = TextWrapping.Wrap;
        _emptyTitle.TextAlignment = TextAlignment.Center;
        _emptyHint = Ui.Muted("").Wrap();
        _emptyHint.TextAlignment = TextAlignment.Center;
        _empty.Children.Add(_emptyHint);
        _empty.Children.Add(_download);
        var resetEmpty = Ui.Btn(I18n.L("显示全部任务"), BtnKind.Soft, (_, _) => ResetFilters());
        resetEmpty.HorizontalAlignment = HorizontalAlignment.Center;
        resetEmpty.Tag = "reset-tasks";
        _empty.Children.Add(resetEmpty);
        _list.Children.Add(_empty);
        var controls = Ui.H(8, _state, _reset);
        controls.Wrap = true;
        return new PageHeader(Ui.V(5, Ui.Small(I18n.L("任务状态")), controls), Ui.V(5, Ui.Small(I18n.L("搜索任务")), _search)) { MinimumHeadingWidth = 260 };
    }

    private void ResetFilters()
    {
        _search.Clear();
        _state.SelectedIndex = 0;
        _search.Focus();
    }

    private bool Matches(TaskRow row)
    {
        bool matchesState = _state.SelectedIndex switch
        {
            1 => !row.Finished,
            2 => row.Finished && !row.Success && !row.Cancelled,
            3 => row.Finished && row.Success,
            4 => row.Cancelled,
            _ => true
        };
        string query = _search.Text.Trim();
        return matchesState && (query.Length == 0
            || row.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || DisplayLabels.Of(row.Status).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || row.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void ApplyFilters()
    {
        if (_reset == null) return;
        int visible = 0;
        foreach (TaskRow row in TaskStore.Rows)
        {
            if (!_cards.TryGetValue(row.Id, out Card card)) continue;
            bool show = Matches(row);
            card.Root.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) visible++;
        }
        bool hasTasks = TaskStore.Rows.Count > 0;
        bool filtered = _state.SelectedIndex > 0 || _search.Text.Trim().Length > 0;
        _summary.Text = I18n.L("显示 {0} / {1} 个任务 · 进行中 {2}", visible, TaskStore.Rows.Count, TaskStore.RunningCount);
        _reset.Visibility = filtered ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        _emptyTitle.Text = I18n.L(hasTasks ? "未找到匹配的任务" : "还没有任务");
        _emptyHint.Text = I18n.L(hasTasks ? "试试其他名称或状态，也可以清除筛选查看全部任务。" : "安装游戏或下载资源后，可在这里查看进度和日志。");
        _download.Visibility = !hasTasks && !filtered ? Visibility.Visible : Visibility.Collapsed;
        _empty.Children.OfType<Button>().Single(button => button.Tag as string == "reset-tasks").Visibility = filtered ? Visibility.Visible : Visibility.Collapsed;
        _clear.IsEnabled = TaskStore.Rows.Any(row => row.Finished);
        _cancelAll.IsEnabled = !_cancellingAll && TaskStore.RunningCount > 0;
        _clear.ToolTip = I18n.L("清除所有已结束的任务，包括当前筛选隐藏的任务。");
        _cancelAll.ToolTip = I18n.L("取消所有进行中的任务，包括当前筛选隐藏的任务。");
    }
}
