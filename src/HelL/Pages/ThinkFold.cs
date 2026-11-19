using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HelL.Services;

namespace HelL.Pages;

internal sealed class ThinkFold : Border
{
	private readonly TextBlock _label;

	private readonly Path _chevron;

	private readonly TextBox _body;

	private bool _open;

	private bool _thinking;

	public bool IsOpen => _open;

	public string Text => _body.Text;

	internal string Header => _label.Text;

	internal bool BodyShown => _body.Visibility == Visibility.Visible;

	public ThinkFold()
	{
		_label = Ui.Txt("", 12.0, bold: true, "B.Ink");
		_chevron = Lucide.Icon("M9 18l6-6-6-6");
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(Lucide.Icon("M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77").VCenter(), 0);
		grid.Add(_label.M(8.0, 0.0, 8.0).VCenter(), 0, 1);
		grid.Add(_chevron.VCenter(), 0, 2);
		grid.Background = Brushes.Transparent;
		grid.Cursor = Cursors.Hand;
		grid.MinHeight = 22.0;
		grid.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			Toggle();
			e.Handled = true;
		};
		_body = new TextBox
		{
			IsReadOnly = true,
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			TextWrapping = TextWrapping.Wrap,
			FontSize = 12.0,
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			Visibility = Visibility.Collapsed
		};
		_body.SetResourceReference(Control.ForegroundProperty, "B.InkMuted");
		base.CornerRadius = new CornerRadius(8.0);
		base.Padding = new Thickness(10.0, 5.0, 10.0, 6.0);
		base.BorderThickness = new Thickness(1.0);
		SetResourceReference(Border.BackgroundProperty, "B.Hover");
		SetResourceReference(Border.BorderBrushProperty, "B.Line");
		Child = Ui.V(0.0, grid, _body);
		SyncHead();
	}

	public void Toggle()
	{
		SetOpen(!_open);
	}

	public void SetOpen(bool on)
	{
		if (_open == on)
		{
			SyncHead();
			return;
		}
		_open = on;
		_body.Visibility = ((!on) ? Visibility.Collapsed : Visibility.Visible);
		SyncHead();
	}

	public void SetThink(string text, bool live, bool open)
	{
		_thinking = live && open;
		_body.Text = text ?? "";
		SetOpen(open);
	}

	private void SyncHead()
	{
		int length = _body.Text.Length;
		_label.Text = (_thinking ? I18n.L("思考中…") : (I18n.L("思考过程") + ((length > 0) ? (I18n.L("（") + length + I18n.L("）")) : "")));
		Lucide.Shimmer(_label, _thinking, "B.Ink");
		Lucide.Rotate(_chevron, _open);
	}
}
