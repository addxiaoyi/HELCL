using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class NewsFeed
{
    public List<NewsRow> Rows { get; set; } = new List<NewsRow>();
    public bool Cached { get; set; }
    public string Error { get; set; } = "";
}

public sealed class NewsTranslation
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public List<NewsRow> Rows { get; set; } = new List<NewsRow>();
}

public sealed class NewsPanel : UserControl
{
    private readonly SPanel _items = Ui.V(16);
    private readonly TextBlock _state = Ui.Txt("正在读取新闻…", UiMetrics.CaptionSize, false, "B.InkMuted").Wrap();
    private readonly Button _refresh;
    private readonly Button _translate;
    private readonly Button _more;
    private readonly Button _all;
    private readonly Button _release;
    private readonly Button _snapshot;
    private List<NewsRow> _rows = new List<NewsRow>();
    private bool _expanded;
    private bool _compact;
    public bool Compact { get => _compact; set { if (_compact == value) return; _compact = value; if (_rows.Count > 0) Render(); } }
    private string _filter = "all";
    public event Action? RefreshRequested;
    public event Action? TranslationRequested;
    public int LoadedImages => Descendants(this).OfType<Image>().Count(image => image.Source != null);

    public NewsPanel()
    {
        _refresh = Ui.Btn("", BtnKind.Icon, (_, _) => RefreshRequested?.Invoke(), "\ue72c");
        _refresh.ToolTip = I18n.L("刷新新闻");
        _state.LineHeight = UiMetrics.CaptionLine;
        AutomationProperties.SetName(_refresh, I18n.L("刷新新闻"));
        _translate = Ui.Btn(I18n.L("翻译新内容"), BtnKind.Ghost, (_, _) => TranslationRequested?.Invoke());
        _translate.ToolTip = I18n.L("使用本机 Ollama 翻译英文简介，保留官方原文");
        _translate.Visibility = Visibility.Collapsed;
        var heading = Ui.G(null, "*,Auto");
        heading.Add(Ui.V(3, Ui.Txt(I18n.L("Minecraft 动态"), 17, true), _state), 0);
        heading.Add(_refresh.VCenter(), 0, 1);
        _all = Ui.Btn(I18n.L("全部"), BtnKind.Ghost, (_, _) => Filter("all"));
        _release = Ui.Btn(I18n.L("正式版"), BtnKind.Ghost, (_, _) => Filter("release"));
        _snapshot = Ui.Btn(I18n.L("快照"), BtnKind.Ghost, (_, _) => Filter("snapshot"));
        var filters = Ui.H(4, _all, _release, _snapshot, _translate);
        filters.Wrap = true;
        _more = Ui.Btn(I18n.L("更多动态"), BtnKind.Ghost, (_, _) => { _expanded = !_expanded; Render(); });
        _more.HorizontalAlignment = HorizontalAlignment.Left;
        _more.Visibility = Visibility.Collapsed;
        Content = Ui.V(12, heading, filters, _items, _more);
        AutomationProperties.SetName(this, I18n.L("Minecraft 图文新闻"));
    }

    public void Busy(bool busy)
    {
        _refresh.IsEnabled = !busy;
        if (busy) _state.Text = I18n.L("正在更新官方新闻…");
        if (busy && _rows.Count == 0)
        {
            _items.Children.Clear();
            _items.Children.Add(Ui.V(12, Ui.Skeleton(160), Ui.Skeleton(20), Ui.Skeleton(16)));
        }
    }

    public void Show(NewsFeed feed)
    {
        _rows = feed.Rows;
        _state.Text = feed.Error.Length > 0 ? I18n.L(_rows.Count > 0 ? "连接失败，显示上次内容" : "新闻暂时不可用，点击刷新重试。") : feed.Cached ? I18n.L("Minecraft 官方 · 本地缓存") : I18n.L("Minecraft 官方 · 最新动态");
        _state.ToolTip = feed.Error;
        _translate.Visibility = I18n.Current == "zh_CN" && _rows.Any(row => row.Localization == "版本导读") ? Visibility.Visible : Visibility.Collapsed;
        Render();
    }

    public void TranslationState(bool busy, string text)
    {
        _translate.IsEnabled = !busy;
        _state.Text = text;
    }

    public void TranslationFinished() => _translate.IsEnabled = true;

    public void Error(string message)
    {
        _state.Text = message;
        if (_rows.Count == 0)
        {
            _items.Children.Clear();
            _items.Children.Add(Ui.Muted(I18n.L("暂时没有可显示的新闻。点击右上角刷新重试。")));
        }
    }

    private void Filter(string filter) { _filter = filter; _expanded = false; Render(); }

    private void Render()
    {
        _all.Style = Ui.S(_filter == "all" ? "Btn.Chip" : "Btn.Ghost");
        _release.Style = Ui.S(_filter == "release" ? "Btn.Chip" : "Btn.Ghost");
        _snapshot.Style = Ui.S(_filter == "snapshot" ? "Btn.Chip" : "Btn.Ghost");
        var rows = _rows.Where(row => _filter == "all" || (_filter == "snapshot") == Preview(row)).ToList();
        _items.Children.Clear();
        if (rows.Count == 0) _items.Children.Add(Ui.Muted(I18n.L("这个分类暂时没有新闻。")));
        int visible = _expanded ? 12 : 3;
        for (int i = 0; i < Math.Min(rows.Count, visible); i++) _items.Children.Add(Article(rows[i], !_compact && i == 0));
        _more.Visibility = _expanded || rows.Count > visible ? Visibility.Visible : Visibility.Collapsed;
        _more.Content = I18n.L(_expanded ? "收起动态" : "更多动态");
    }

    private static bool Preview(NewsRow row) => row.Type == "snapshot" || System.Text.RegularExpressions.Regex.IsMatch(row.Version, "snapshot|pre|rc|[0-9]{2}w", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private FrameworkElement Article(NewsRow row, bool featured)
    {
        var picture = new NewsImage(row.Image, row.Title, featured ? 190 : 80);
        if (featured) picture.SizeChanged += (_, _) => picture.Height = picture.ActualWidth < 310 ? 140 : 190;
        var meta = Ui.Txt((Preview(row) ? I18n.L("开发快照") : I18n.L("正式更新")) + (row.Date.Length > 0 ? "  ·  " + row.Date : ""), UiMetrics.CaptionSize, false, "B.InkMuted");
        meta.LineHeight = UiMetrics.CaptionLine;
        var title = Ui.Txt(row.Title, featured ? 19 : UiMetrics.BodySize, true).Wrap();
        title.LineHeight = featured ? UiMetrics.HeadingLine : UiMetrics.BodyLine;
        var body = Ui.Txt(row.Body, 13, false, "B.InkMuted").Wrap();
        body.LineHeight = 20;
        body.MaxHeight = featured ? 60 : 40;
        body.TextTrimming = TextTrimming.CharacterEllipsis;
        var words = Ui.V(featured ? 7 : 4, meta, title, body);
        FrameworkElement composition;
        if (featured) composition = Ui.V(12, picture, words);
        else
        {
            picture.Width = 112;
            var line = Ui.G(null, "Auto,*");
            line.Add(picture, 0); line.Add(words.M(12, 0, 0, 0).VCenter(), 0, 1);
            composition = line;
        }
        var button = Ui.Btn("", BtnKind.Ghost, (_, _) => Open(row));
        button.Content = composition;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(0);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.ToolTip = I18n.L("阅读更新摘要与官方原文");
        AutomationProperties.SetName(button, row.Title);
        return button;
    }

    private static void Open(NewsRow row)
    {
        var content = Ui.V(14, new NewsImage(row.Image, row.Title, 210), Ui.Txt(row.Title, 22, true).Wrap(),
            Ui.Txt(row.Date + " · " + I18n.L("Minecraft 官方") + (row.Localization.Length > 0 ? " · " + I18n.L(row.Localization) : ""), 12, false, "B.InkMuted"),
            Ui.Txt(row.Body, 14).Wrap());
        if (row.OriginalBody.Length > 0 && row.OriginalBody != row.Body)
            content.Children.Add(new Expander { Header = I18n.L("查看英文原文"), Content = Ui.Txt(row.OriginalBody, 13, false, "B.InkMuted").Wrap().M(0, 10, 0, 0) });
        string url = News.OfficialUrl(row.Url, false);
        if (url.Length > 0) content.Children.Add(Ui.Btn(I18n.L("打开官方文章"), BtnKind.Chip, (_, _) => Ui.OpenUrl(url), "\ue8a7").Left());
        var scroll = Ui.Scroll(content); scroll.MaxHeight = Math.Max(260, (AppServices.Window?.ActualHeight ?? 740) - 190);
        Dlg.Panel(I18n.L("更新动态"), scroll, 680);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var child = VisualTreeHelper.GetChild(node, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}

public sealed class NewsImage : Grid
{
    private readonly Image _image = new Image { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _placeholder = Ui.Txt(I18n.L("正在加载封面…"), 12, false, "B.InkMuted").Wrap();
    public Task Loading { get; }
    public bool Ready => _image.Source != null;

    public NewsImage(string url, string title, double height)
    {
        Height = height;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "B.Paper2");
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.TextAlignment = TextAlignment.Center;
        _placeholder.MaxWidth = 104;
        var fallback = Ui.V(6, Ui.Glyph("\ue8a5", 28, "B.InkFaint"), _placeholder);
        fallback.HorizontalAlignment = HorizontalAlignment.Center;
        fallback.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(fallback); Children.Add(_image);
        AutomationProperties.SetName(this, title);
        SizeChanged += (_, _) => Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 8, 8);
        Loading = Load(url);
    }

    private async Task Load(string url)
    {
        url = News.OfficialUrl(url, true);
        if (url.Length == 0) { _placeholder.Text = I18n.L("暂无封面"); return; }
        try
        {
            BitmapSource? cover = await Thumbs.LoadAsync(url, 600);
            if (cover == null) { _placeholder.Text = I18n.L("封面暂不可用"); return; }
            _image.Source = cover;
            _placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { _placeholder.Text = I18n.L("封面暂不可用"); Smoke.Note("news-cover", ex.Message); }
    }
}
