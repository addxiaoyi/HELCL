using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class CatalogPage
{
    private static readonly string[] ResultOrders = { "relevance", "downloads", "updated", "name" };
    private readonly ComboBox _resultOrder = Ui.Combo(new[] { "相关度", "下载量", "最近更新", "名称 A–Z" }, "相关度", 124);
    private readonly TextBlock _resultCount = Ui.Small("");
    private List<object> _shownRows = new();

    private FrameworkElement ResultsToolbar()
    {
        _resultOrder.ToolTip = "对本次搜索结果排序";
        System.Windows.Automation.AutomationProperties.SetName(_resultOrder, "结果排序");
        _resultOrder.SelectedIndex = System.Math.Max(0, System.Array.IndexOf(ResultOrders, Win?.Prefs.CatalogSort));
        _resultOrder.SelectionChanged += (_, _) =>
        {
            if (Win != null) { Win.Prefs.CatalogSort = ResultOrders[System.Math.Max(0, _resultOrder.SelectedIndex)]; Win.SaveUiPrefs(); }
            if (_results.Visibility == Visibility.Visible) ShowRows(_shownRows);
        };
        return Ui.H(10, _resultCount.VCenter(), _resultOrder).M(10, 2, 0, 8);
    }

    private IEnumerable<object> SortedRows()
    {
        bool projects = _shownRows.Count > 0 && _shownRows.All(row => row is CatalogItem);
        _resultOrder.IsEnabled = projects;
        _resultCount.Text = _shownRows.Count + " 项";
        return projects ? CatalogOrdering.Sort(_shownRows.Cast<CatalogItem>(), ResultOrders[System.Math.Max(0, _resultOrder.SelectedIndex)]).Cast<object>() : _shownRows;
    }
}
