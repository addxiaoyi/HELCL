using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HelL.Services;

namespace HelL.Pages.Ai;

internal sealed class StepGroup : Border
{
	private readonly SPanel _body = Ui.V(0.0);

	private readonly Border _bodyHost;

	private readonly TextBlock _title;

	private readonly TextBlock _count;

	private readonly Path _chev;

	private readonly Stopwatch _sw = Stopwatch.StartNew();

	private bool _open = true;

	private bool _running = true;

	private bool _userToggled;

	internal int Count => _body.Children.OfType<ToolLine>().Count();

	internal bool IsOpen => _open;

	internal string TitleText => _title.Text;

	public StepGroup()
	{
		_title = Ui.Txt(I18n.L("正在执行"), 12.5, bold: true, "B.Ink");
		_title.TextTrimming = TextTrimming.CharacterEllipsis;
		_count = Ui.Txt("", 12.0, bold: false, "B.InkFaint");
		_chev = AiIcons.Icon("M6 9l6 6 6-6", 14.0, "B.InkFaint");
		_chev.RenderTransformOrigin = new Point(0.5, 0.5);
		_chev.RenderTransform = new RotateTransform(0.0);
		Grid grid = Ui.G(null, "Auto,Auto,Auto,*,Auto");
		grid.Add(AiIcons.Icon("M12 2l10 6-10 6L2 8z M2 14l10 6 10-6", 14.0, "B.InkSoft").VCenter(), 0);
		grid.Add(_title.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid.Add(_count.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		grid.Add(_chev.VCenter(), 0, 4);
		grid.Background = Brushes.Transparent;
		grid.Cursor = Cursors.Hand;
		grid.Margin = new Thickness(12.0, 8.0, 12.0, 8.0);
		grid.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			_userToggled = true;
			SetOpen(!_open);
			e.Handled = true;
		};
		_bodyHost = new Border
		{
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Padding = new Thickness(6.0, 4.0, 6.0, 6.0),
			Child = _body
		};
		_bodyHost.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		base.CornerRadius = new CornerRadius(10.0);
		base.BorderThickness = new Thickness(1.0);
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		SetResourceReference(Border.BorderBrushProperty, "B.Line");
		SetResourceReference(Border.BackgroundProperty, "B.Paper");
		Child = Ui.V(0.0, grid, _bodyHost);
		Refresh();
	}

	public void AddLine(ToolLine line)
	{
		if (line.Parent is Panel panel)
		{
			if (panel == _body)
			{
				Refresh();
				return;
			}
			panel.Children.Remove(line);
		}
		_body.Children.Add(line);
		Refresh();
	}

	public SPanel AddSubagent(string title)
	{
		SPanel sPanel = Ui.V(0.0);
		Border border = new Border();
		border.BorderThickness = new Thickness(1.0);
		border.CornerRadius = new CornerRadius(9.0);
		border.Padding = new Thickness(4.0);
		border.Margin = new Thickness(35.0, 2.0, 0.0, 4.0);
		border.Child = Ui.V(2.0, Ui.H(6.0, AiIcons.Icon("M6 3a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 15a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M18 5a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M6 9v6 M18 11a6 6 0 0 1-6 6H9", 13.0).VCenter(), Ui.Txt(title, 11.5, bold: false, "B.InkMuted").VCenter()).M(6.0, 3.0, 0.0, 3.0), sPanel);
		Border border2 = border;
		border2.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		_body.Children.Add(border2);
		return sPanel;
	}

	public void Refresh()
	{
		ToolLine[] array = _body.Children.OfType<ToolLine>().ToArray();
		ToolLine toolLine = array.LastOrDefault((ToolLine l) => l.State == ToolState.Running || l.State == ToolState.Prepare);
		bool flag = _running && toolLine != null;
		_title.Text = AiUiText.StepSummary(array.Length, _sw.Elapsed.TotalSeconds, flag, toolLine?.LabelText ?? "");
		int num = array.Count((ToolLine l) => l.State == ToolState.Failed);
		_count.Text = (flag ? $"· {array.Length} 步" : ((num > 0) ? $"· {num} 步失败" : ""));
		_count.SetResourceReference(TextBlock.ForegroundProperty, (num > 0 && !flag) ? "B.Danger" : "B.InkFaint");
	}

	public void Complete()
	{
		_running = false;
		_sw.Stop();
		Refresh();
		if (!_userToggled && Count > 3)
		{
			SetOpen(open: false);
		}
	}

	public void SetOpen(bool open)
	{
		_open = open;
		_bodyHost.Visibility = ((!open) ? Visibility.Collapsed : Visibility.Visible);
		((RotateTransform)_chev.RenderTransform).Angle = ((!open) ? (-90) : 0);
	}
}
