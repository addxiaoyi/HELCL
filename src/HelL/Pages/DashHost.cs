using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class DashHost : Panel
{
    private readonly Dictionary<string, UIElement> _contents = new();
    private readonly List<CardView> _views = new();
    private readonly List<DashLayout> _undo = new(), _redo = new();
    private DashLayout _layout = DashLayout.Default();
    private List<DashboardBox> _boxes = new();
    private DashLayout? _beforeDrag;
    private string _active = "";
    private bool _edit;
    private double? _guideX, _guideY;

    public Func<DashCard, UIElement?>? ContentFactory;
    public Func<string, string>? TitleFactory;
    public Func<string, bool>? Repeatable;
    public Dictionary<string, (int W, int H)> MinSizes { get; set; } = new();
    public DashLayout Layout => _layout;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;
    public event Action? HistoryChanged;
    public event Action? Rebuilding;
    public event Action? Rebuilt;
    public event Action<DashCard>? Configure;
    public ScrollViewer? ScrollHost { get; set; }

    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        nameof(ViewportHeight), typeof(double), typeof(DashHost),
        new FrameworkPropertyMetadata(600.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public bool EditMode
    {
        get => _edit;
        set
        {
            if (_edit == value) return;
            CancelDrag(); _edit = value;
            foreach (var view in _views) view.SetEdit(value);
            InvalidateVisual();
        }
    }

    public DashHost()
    {
        Background = Brushes.Transparent;
        Loaded += (_, _) => App.ThemeVisualsChanged += RefreshThemeVisuals;
        Unloaded += (_, _) => App.ThemeVisualsChanged -= RefreshThemeVisuals;
        PreviewKeyDown += OnHistoryKey;
    }

    public void Load(DashLayout layout)
    {
        _layout = layout; _layout.Normalize(); _undo.Clear(); _redo.Clear();
        RefreshModules(); HistoryChanged?.Invoke();
    }

    private string CacheKey(DashCard card) => Repeatable?.Invoke(card.Type) == true ? card.Id : card.Type;

    public void Rebuild()
    {
        Rebuilding?.Invoke();
        foreach (var view in _views) view.DetachContent();
        Children.Clear(); _views.Clear();
        var claimed = new HashSet<UIElement>();
        var currentKeys = new HashSet<string>(_layout.Items.Select(CacheKey));
        foreach (string key in _contents.Keys.Where(key => !currentKeys.Contains(key) && key.Contains("-")).ToList()) _contents.Remove(key);
        foreach (var card in _layout.VisibleItems())
        {
            string key = CacheKey(card);
            if (!_contents.TryGetValue(key, out var content))
            {
                try { content = ContentFactory?.Invoke(card); }
                catch (Exception ex)
                {
                    Smoke.Note("dashboard-module:" + card.Type, ex.ToString());
                    content = Ui.V(10, Ui.Muted("此模块暂时无法加载。请检查模块设置，或重新安装对应插件。").Wrap(),
                        Ui.Btn("检查模块设置", BtnKind.Ghost, (_, _) => Configure?.Invoke(card)));
                }
                if (content == null) continue;
                _contents[key] = content;
            }
            if (!claimed.Add(content)) content = Ui.Muted("此内置模块已在启动页中。可移除此重复模块。").Wrap();
            var view = new CardView(this, card, CardTitle(card), content);
            view.SetEdit(_edit); _views.Add(view); Children.Add(view);
        }
        if (_views.Count == 0) Children.Add(Ui.Muted("启动页还是空的。点击「编辑布局」→「模块库」添加你需要的模块。").Wrap().M(24));
        InvalidateMeasure();
        Rebuilt?.Invoke();
    }

    public void DetachContents() { foreach (var view in _views) view.DetachContent(); }
    public UIElement? BorrowContent(string type) => _contents.TryGetValue(type, out var content) ? content : null;

    private string CardTitle(DashCard card)
    {
        if (card.Settings is { } settings && settings.TryGetProperty("title", out var title) && title.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(title.GetString())) return title.GetString()!;
        return TitleFactory?.Invoke(card.Type) ?? card.Type;
    }

    public void RefreshModules()
    {
        foreach (var card in _layout.Items.Where(card => Repeatable?.Invoke(card.Type) == true)) _contents.Remove(CacheKey(card));
        Rebuild();
    }

    public void ChangeCard(DashCard card, Action<DashCard> change)
    {
        card = _layout.Items.FirstOrDefault(current => current.Id == card.Id) ?? throw new InvalidOperationException("此模块已被移除，请重新添加后再设置。");
        Remember(); change(card);
        if (Repeatable?.Invoke(card.Type) == true) _contents.Remove(CacheKey(card));
        Rebuild(); NotifyChanged();
    }

    public void AddCard(string type)
    {
        if (Repeatable?.Invoke(type) != true && Has(type)) return;
        Remember();
        var min = MinPx(type);
        double width = Math.Max(320, ActualWidth), top = _boxes.Count > 0 ? _boxes.Max(box => box.Bottom) + DashboardGeometry.Gap : 0;
        _layout.Items.Add(new DashCard { Id = type + "-" + Guid.NewGuid().ToString("N"), Type = type, X = 0,
            Y = top / DashboardGeometry.ReferenceHeight, W = Math.Min(1, Math.Max(0.32, min.W / width)),
            H = Math.Max(0.22, min.H / DashboardGeometry.ReferenceHeight), Z = _layout.NextZ() });
        Rebuild(); NotifyChanged();
        _views.LastOrDefault()?.BringIntoView();
    }

    public void RemoveCard(DashCard card) { Remember(); _layout.Items.Remove(card); Rebuild(); NotifyChanged(); }
    public void ApplyPreset(DashLayout preset)
    {
        Remember();
        var previous = _layout.Items.ToList(); var arranged = new List<DashCard>();
        foreach (var placement in preset.Items)
        {
            var card = previous.FirstOrDefault(item => item.Type == placement.Type);
            if (card != null) previous.Remove(card); else card = placement.Clone();
            card.X = placement.X; card.Y = placement.Y; card.W = placement.W; card.H = placement.H; card.Hidden = false; arranged.Add(card);
        }
        double bottom = arranged.Max(card => card.Y + card.H) + .02;
        foreach (var card in previous) { card.Y = bottom; bottom += card.H + .02; arranged.Add(card); }
        _layout.Items = arranged; for (int i = 0; i < arranged.Count; i++) arranged[i].Z = i;
        Rebuild(); NotifyChanged(); ScrollHost?.ScrollToTop();
    }
    public void MoveInOrder(DashCard card, int direction)
    {
        var ordered = _layout.VisibleItems().OrderBy(item => item.Y).ThenBy(item => item.X).ToList();
        int index = ordered.IndexOf(card), next = index + direction;
        if (index < 0 || next < 0 || next >= ordered.Count) return;
        Remember(); var neighbour = ordered[next];
        double x = card.X, y = card.Y;
        card.X = Math.Min(1 - card.W, neighbour.X); card.Y = neighbour.Y;
        neighbour.X = Math.Min(1 - neighbour.W, x); neighbour.Y = y;
        Rebuild(); NotifyChanged(); _views.First(view => view.Card.Id == card.Id).BringIntoView();
    }
    internal void ScrollWhileDragging(Point cursor)
    {
        var scroll = ScrollHost; if (scroll == null) return;
        Point location = TranslatePoint(cursor, scroll);
        const double edge = 48, speed = 24;
        if (location.Y < edge) scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - speed));
        else if (location.Y > scroll.ActualHeight - edge) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + speed);
    }
    public bool Has(string type) => _layout.Items.Any(card => card.Type == type && !card.Hidden);
    public void NotifyChanged() { Changed?.Invoke(); HistoryChanged?.Invoke(); }
    public (double W, double H) MinPx(string type)
    {
        var minimum = MinSizes.TryGetValue(type, out var size) ? size : (W: 240, H: 160);
        return (minimum.W, minimum.H);
    }

    private void Remember()
    {
        _undo.Add(_layout.Clone()); if (_undo.Count > 60) _undo.RemoveAt(0); _redo.Clear();
    }
    public void Undo() => RestoreHistory(_undo, _redo);
    public void Redo() => RestoreHistory(_redo, _undo);
    private void RestoreHistory(List<DashLayout> from, List<DashLayout> to)
    {
        if (from.Count == 0) return;
        CancelDrag(); to.Add(_layout.Clone()); _layout = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
        RefreshModules(); NotifyChanged();
    }
    private void OnHistoryKey(object sender, KeyEventArgs e)
    {
        if (!_edit || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.Z) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) Redo(); else Undo(); e.Handled = true; }
        if (e.Key == Key.Y) { Redo(); e.Handled = true; }
    }

    internal DashboardBox BeginDrag(DashCard card)
    {
        _beforeDrag = _layout.Clone(); _active = card.Id;
        return _boxes.Single(box => box.Id == card.Id).Copy();
    }

    internal void Drag(DashCard card, DashboardBox proposed, string handle, bool snap = true)
    {
        var box = DashboardGeometry.Snap(proposed, _boxes, ActualWidth, snap ? _layout.Grid : 0, handle, out _guideX, out _guideY);
        var min = MinPx(card.Type);
        box.Width = Math.Min(ActualWidth, Math.Max(min.W, box.Width)); box.Height = Math.Min(3200, Math.Max(min.H, box.Height));
        box.X = Math.Max(0, Math.Min(ActualWidth - box.Width, box.X));
        StoreBox(card, box, handle != "move"); InvalidateMeasure(); InvalidateVisual();
    }

    private void StoreBox(DashCard card, DashboardBox box, bool resize)
    {
        card.X = box.X / Math.Max(1, ActualWidth); card.Y = box.Y / DashboardGeometry.ReferenceHeight;
        if (resize) { card.W = box.Width / Math.Max(1, ActualWidth); card.H = box.Height / DashboardGeometry.ReferenceHeight; }
    }

    internal void EndDrag()
    {
        if (_beforeDrag == null) return;
        UpdateLayout();
        // Persist displaced neighbours so releasing a dragged module cannot change the arrangement.
        foreach (var card in _layout.VisibleItems())
        {
            var box = _boxes.FirstOrDefault(box => box.Id == card.Id);
            if (box != null) card.Y = box.Y / DashboardGeometry.ReferenceHeight;
        }
        if (_beforeDrag.ToJson() != _layout.ToJson()) { _undo.Add(_beforeDrag); if (_undo.Count > 60) _undo.RemoveAt(0); _redo.Clear(); }
        _beforeDrag = null; _active = ""; _guideX = _guideY = null;
        InvalidateMeasure(); InvalidateVisual(); NotifyChanged();
    }

    internal void CancelDrag()
    {
        if (_beforeDrag == null) return;
        _layout = _beforeDrag; _beforeDrag = null; _active = ""; _guideX = _guideY = null;
        Mouse.Capture(null); Rebuild();
    }

    private List<DashboardBox> ArrangeCards(double width) => DashboardGeometry.Arrange(
        _layout.VisibleItems().Select(card => new LayoutItem(card.Type, card.X, card.Y, card.W, card.H, card.Id, card.Z)), width, MinPx, _active);

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 900 : Math.Max(1, available.Width);
        _boxes = ArrangeCards(width);
        foreach (var view in _views)
        {
            var box = _boxes.Single(box => box.Id == view.Card.Id); view.Measure(new Size(box.Width, box.Height));
        }
        if (_views.Count == 0 && Children.Count > 0) Children[0].Measure(new Size(width, 240));
        double height = _boxes.Count > 0 ? _boxes.Max(box => box.Bottom) + (_edit ? 160 : 8) : 240;
        return new Size(width, Math.Max(ViewportHeight, height));
    }
    protected override Size ArrangeOverride(Size final)
    {
        _boxes = ArrangeCards(final.Width);
        foreach (var view in _views)
        {
            var box = _boxes.Single(box => box.Id == view.Card.Id); view.Arrange(new Rect(box.X, box.Y, box.Width, box.Height));
        }
        if (_views.Count == 0 && Children.Count > 0) Children[0].Arrange(new Rect(0, 0, final.Width, 240));
        return final;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!_edit) return;
        var accent = Application.Current.TryFindResource("B.Accent") as Brush ?? Brushes.SeaGreen;
        var pen = new Pen(accent, 1);
        if (_guideX.HasValue) dc.DrawLine(pen, new Point(_guideX.Value, 0), new Point(_guideX.Value, ActualHeight));
        if (_guideY.HasValue) dc.DrawLine(pen, new Point(0, _guideY.Value), new Point(ActualWidth, _guideY.Value));
    }
    public void RefreshThemeVisuals() { InvalidateVisual(); }
}
