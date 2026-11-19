using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Services;

namespace HelL.Pages;

internal sealed class LaunchWorkspace : ContentControl
{
    private readonly List<ContentControl> _hosts = new();
    private readonly FrameworkElement _config;
    private readonly FrameworkElement _quick;
    private readonly FrameworkElement _playtime;
    private readonly FrameworkElement _log;
    private readonly ContentControl _banner;
    private readonly Border _start;
    private readonly AdaptiveColumns _details;
    private bool _hasGames;
    private readonly ContentControl _library;
    private readonly ContentControl _news;
    private readonly SPanel _left = Ui.V(22);
    private readonly SPanel _right = Ui.V(20);
    private bool? _wide;

    public LaunchWorkspace(Func<string, UIElement?> borrow)
    {
        Focusable = false; HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
        _config = Ui.V(12, Ui.Txt("启动选项", 13, true, "B.InkMuted"), Host("config"));
        _quick = Ui.V(10, Ui.Txt("常用入口", 12.5, true, "B.InkMuted"), Host("quick"));
        _playtime = Ui.Pane(Host("playtime"), "B.Card", 14, 16);
        _log = new Expander { Header = "运行日志", Style = Ui.S("Expander.Inline"), Content = new Border { Child = Host("log"), Height = 220 } };
        _banner = Host("banner"); _library = Host("library"); _news = Host("news");
        _start = Ui.Card(new AdaptiveColumns(_banner, _config, 304, 740, gap: 36), 22);
        _start.CornerRadius = new CornerRadius(16);
        if (_library.Content is UIElement library)
            _library.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = library });
        _details = new AdaptiveColumns(_left, _right, 280, 740, gap: 22);
        Content = Ui.V(18, _start, _details);

        ContentControl Host(string type)
        {
            var content = borrow(type);
            if (content is ScrollViewer scroll) scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var host = new ContentControl { Content = content, Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
            _hosts.Add(host); return host;
        }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        bool wide = constraint.Width >= 740;
        if (_wide != wide)
        {
            _wide = wide; _left.Children.Clear(); _right.Children.Clear();
            foreach (var section in new[] { _library, _quick, _playtime, _log }) _left.Children.Add(section);
            (_hasGames ? _right : _left).Children.Add(_news);
        }
        return base.MeasureOverride(constraint);
    }

    public void SetHasGames(bool hasGames)
    {
        _config.Visibility = _quick.Visibility = _playtime.Visibility = _log.Visibility = hasGames ? Visibility.Visible : Visibility.Collapsed;
        if (_hasGames != hasGames) { _hasGames = hasGames; _wide = null; InvalidateMeasure(); }
    }

    public void Release()
    {
        foreach (var host in _hosts)
        {
            if (host.Content is ScrollViewer scroll) scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            host.Content = null;
        }
    }

    // Edited coordinates and extra modules keep the canvas; the standard layout follows its content.
    internal static bool Fits(DashLayout layout, string profile)
    {
        var defaults = DashLayout.Default().Items;
        if (layout.Items.Count != defaults.Count) return false;
        return defaults.All(expected => layout.Items.Any(card =>
            card.Type == expected.Type && card.Id == expected.Id && !card.Hidden && card.Z == expected.Z &&
            Math.Abs(card.X - expected.X) < 0.00001 && Math.Abs(card.Y - expected.Y) < 0.00001 &&
            Math.Abs(card.W - expected.W) < 0.00001 && Math.Abs(card.H - expected.H) < 0.00001));
    }

}
