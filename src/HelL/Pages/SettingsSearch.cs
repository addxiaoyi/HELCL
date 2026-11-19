using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

internal static class SettingsSearch
{
    internal sealed class Section
    {
        public string Key = "";
        public string Label = "";
        public FrameworkElement[] Controls = Array.Empty<FrameworkElement>();
    }

    public static FrameworkElement Wrap(FrameworkElement workspace, Rail rail, IEnumerable<Section> sections)
    {
        var search = Ui.Input("搜索设置：JVM、代理、密钥、目录…");
        var matches = Ui.V(4);
        var box = Ui.V(6, search, matches);
        box.Margin = new Thickness(UiMetrics.PageInset, 12, UiMetrics.PageInset, 0);
        var root = Ui.G("Auto,*").Add(box, 0).Add(workspace, 1);
        search.TextChanged += (_, _) =>
        {
            matches.Children.Clear();
            string query = search.Text.Trim();
            if (query.Length == 0) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (Section section in sections)
            foreach (FrameworkElement control in section.Controls)
            foreach (TextBlock label in Descendants(control).OfType<TextBlock>())
            {
                if (string.IsNullOrWhiteSpace(label.Text) || label.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string caption = section.Label + " · " + label.Text;
                if (!seen.Add(caption)) continue;
                if (++count > 6) continue;
                var button = Ui.Btn(caption.Length > 100 ? caption.Substring(0, 100) + "…" : caption, BtnKind.Ghost);
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                button.Click += (_, _) =>
                {
                    rail.Select(section.Key);
                    root.Dispatcher.BeginInvoke(new Action(() => label.BringIntoView()));
                };
                matches.Children.Add(button);
            }
            if (count == 0) matches.Children.Add(Ui.Small("没有匹配设置。可尝试目录、下载、模型或权限。"));
            else if (count > 6) matches.Children.Add(Ui.Small("找到 " + count + " 项，请细化关键词以缩小范围。"));
        };
        System.Windows.Automation.AutomationProperties.SetName(search, "搜索并定位设置");
        return root;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject item)
                foreach (DependencyObject descendant in Descendants(item)) yield return descendant;
    }
}
