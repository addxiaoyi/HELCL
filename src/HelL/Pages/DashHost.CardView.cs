using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class DashHost
{
    public sealed class CardView : Border
    {
        private readonly DashHost _host;
        private readonly ContentControl _body;
        private readonly Border _mask;
        private readonly Grid _tools;
        private readonly Canvas _handles = new();
        private DashboardBox? _grabBox;
        private Point _grabPoint;
        private string _mode = "move";
        private bool _editing;
        private readonly System.Windows.Threading.DispatcherTimer _dragTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
        public DashCard Card { get; }

        public CardView(DashHost host, DashCard card, string title, UIElement content)
        {
            _host = host; Card = card; Focusable = true;
            AutomationProperties.SetName(this, title + "模块");
            ToolTip = "编辑时：方向键移动，Shift + 方向键缩放，Alt 逐像素微调，Delete 移除，Enter 设置，Esc 取消拖动";
            CornerRadius = new CornerRadius(14); BorderThickness = new Thickness(1);
            SetResourceReference(BorderBrushProperty, "B.CardBorder"); SetResourceReference(BackgroundProperty, "B.Card");
            var root = new Grid();
            bool showTitle = card.Type != "banner" && card.Type != "news" && card.Type != "playtime";
            _body = new ContentControl { Content = content, Margin = new Thickness(16, 14, 16, 14), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            _body.ClipToBounds = true;
            if (showTitle)
            {
                _body.Margin = new Thickness(16, 42, 16, 14);
                var heading = Ui.Txt(title, 12, true, "B.InkMuted"); heading.TextTrimming = TextTrimming.CharacterEllipsis; heading.Margin = new Thickness(16, 12, 16, 0); heading.VerticalAlignment = VerticalAlignment.Top;
                root.Children.Add(heading);
            }
            if (card.Type == "banner" || card.Type == "quick") { Background = Brushes.Transparent; BorderThickness = new Thickness(0); }
            root.Children.Add(_body);
            _mask = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
            _mask.SetResourceReference(BorderBrushProperty, "B.Accent");
            BindDrag(_mask, "move"); root.Children.Add(_mask);
            _tools = Ui.G(null, "*,Auto,Auto,Auto,Auto"); _tools.VerticalAlignment = VerticalAlignment.Top;
            _tools.SetResourceReference(BackgroundProperty, "B.Paper"); _tools.Margin = new Thickness(4);
            var label = Ui.Txt(title, 12, true); label.TextTrimming = TextTrimming.CharacterEllipsis; label.Margin = new Thickness(8, 5, 8, 5); label.Cursor = Cursors.SizeAll; BindDrag(label, "move");
            _tools.Add(label, 0);
            _tools.Add(Ui.IconBtn("\ue70e", "模块上移", (_, _) => host.MoveInOrder(Card, -1), 11), 0, 1);
            _tools.Add(Ui.IconBtn("\ue70d", "模块下移", (_, _) => host.MoveInOrder(Card, 1), 11), 0, 2);
            _tools.Add(Ui.IconBtn("\ue713", "设置模块", (_, _) => host.Configure?.Invoke(Card), 12), 0, 3);
            _tools.Add(Ui.IconBtn("\ue711", "移除模块", (_, _) => host.RemoveCard(Card), 12), 0, 4);
            root.Children.Add(_tools);
            foreach (string edge in new[] { "nw", "n", "ne", "w", "e", "sw", "s", "se" })
            {
                var handle = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Tag = edge,
                    Cursor = edge == "n" || edge == "s" ? Cursors.SizeNS : edge == "e" || edge == "w" ? Cursors.SizeWE : edge == "nw" || edge == "se" ? Cursors.SizeNWSE : Cursors.SizeNESW };
                handle.SetResourceReference(BackgroundProperty, "B.Paper"); handle.SetResourceReference(BorderBrushProperty, "B.Accent"); BindDrag(handle, edge); _handles.Children.Add(handle);
            }
            root.Children.Add(_handles); Child = root;
            KeyDown += OnEditKey;
            _dragTimer.Tick += (_, _) => { if (_grabBox != null && Mouse.LeftButton == MouseButtonState.Pressed) MovePointer(Mouse.GetPosition(_host)); else _dragTimer.Stop(); };
            Unloaded += (_, _) => _dragTimer.Stop();
        }
        internal void DetachContent() => _body.Content = null;
        public void SetEdit(bool edit)
        {
            _editing = edit; _body.IsHitTestVisible = !edit;
            _mask.Visibility = _tools.Visibility = _handles.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            // Reserve the same content rectangle in both modes; edit tools are an overlay.
        }
        private void BindDrag(UIElement target, string mode)
        {
            target.MouseLeftButtonDown += (_, e) =>
            {
                if (!_editing) return;
                Focus(); _mode = mode; _grabPoint = e.GetPosition(_host); _grabBox = _host.BeginDrag(Card);
                target.CaptureMouse(); _dragTimer.Start(); e.Handled = true;
            };
            target.MouseMove += (_, e) =>
            {
                if (_grabBox == null || e.LeftButton != MouseButtonState.Pressed) return;
                MovePointer(e.GetPosition(_host)); e.Handled = true;
            };
            target.MouseLeftButtonUp += (_, e) =>
            {
                if (_grabBox == null) return;
                _dragTimer.Stop(); _grabBox = null; target.ReleaseMouseCapture(); _host.EndDrag(); e.Handled = true;
            };
            target.LostMouseCapture += (_, _) => { if (_grabBox != null) { _dragTimer.Stop(); _grabBox = null; _host.CancelDrag(); } };
        }
        private void MovePointer(Point cursor)
        {
            if (_grabBox == null) return;
            _host.ScrollWhileDragging(cursor);
            double dx = cursor.X - _grabPoint.X, dy = cursor.Y - _grabPoint.Y;
            var box = _grabBox.Copy(); var min = _host.MinPx(Card.Type);
            if (_mode == "move") { box.X += dx; box.Y += dy; }
            else
            {
                if (_mode.Contains("w")) { double left = Math.Max(0, Math.Min(box.Right - Math.Min(min.W, _host.ActualWidth), box.X + dx)); box.Width = box.Right - left; box.X = left; }
                if (_mode.Contains("e")) box.Width = Math.Max(min.W, Math.Min(_host.ActualWidth - box.X, box.Width + dx));
                if (_mode.Contains("n")) { double top = Math.Max(0, Math.Min(box.Bottom - min.H, box.Y + dy)); box.Height = box.Bottom - top; box.Y = top; }
                if (_mode.Contains("s")) box.Height = Math.Max(min.H, box.Height + dy);
            }
            _host.Drag(Card, box, _mode, (Keyboard.Modifiers & ModifierKeys.Alt) == 0);
        }
        private void OnEditKey(object sender, KeyEventArgs e)
        {
            if (!_editing) return;
            if (e.Key == Key.Escape) { _dragTimer.Stop(); _grabBox = null; _host.CancelDrag(); e.Handled = true; return; }
            if (e.Key == Key.Delete) { _host.RemoveCard(Card); e.Handled = true; return; }
            if (e.Key == Key.Enter) { _host.Configure?.Invoke(Card); e.Handled = true; return; }
            if (e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Up && e.Key != Key.Down) return;
            bool resize = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            double step = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? 1 : Math.Max(8, _host.Layout.Grid);
            double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            var box = _host.BeginDrag(Card);
            if (resize) { box.Width += dx; box.Height += dy; } else { box.X += dx; box.Y += dy; }
            _host.Drag(Card, box, resize ? "se" : "move", false); _host.EndDrag(); e.Handled = true;
        }
        protected override Size ArrangeOverride(Size final)
        {
            var size = base.ArrangeOverride(final);
            foreach (Border handle in _handles.Children)
            {
                string edge = (string)handle.Tag;
                Canvas.SetLeft(handle, (edge.Contains("w") ? 0 : edge.Contains("e") ? final.Width : final.Width / 2) - 4.5);
                Canvas.SetTop(handle, (edge.Contains("n") ? 0 : edge.Contains("s") ? final.Height : final.Height / 2) - 4.5);
            }
            return size;
        }
    }
}
