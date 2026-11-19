using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL.Pages;

internal sealed class PlanCard : Border
{
	internal int Done { get; }

	internal int Total { get; }

	public PlanCard(List<(string Title, string Status)> items)
	{
		(int, int) tuple = AiUiText.PlanProgress(items);
		Done = tuple.Item1;
		Total = tuple.Item2;
		ProgressBar progressBar = Ui.Prog((Total > 0) ? ((double)Done * 100.0 / (double)Total) : 0.0);
		progressBar.Height = 5.0;
		progressBar.Width = 160.0;
		Grid grid = Ui.G(null, "Auto,Auto,Auto,*,Auto");
		grid.Add(AiIcons.Icon("M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01", 14.0, "B.AccentText").VCenter(), 0);
		grid.Add(Ui.Txt(I18n.L("执行计划"), 12.5, bold: true, "B.Ink").M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid.Add(Ui.Txt($"· {Done} / {Total}", 12.0, bold: false, "B.InkMuted").M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		grid.Add(progressBar.VCenter(), 0, 4);
		SPanel sPanel = Ui.V(2.0, grid.M(0.0, 0.0, 0.0, 6.0));
		foreach (var (title, status) in items)
		{
			sPanel.Children.Add(Row(title, status));
		}
		Child = sPanel;
		base.Padding = new Thickness(13.0, 10.0, 13.0, 10.0);
		base.BorderThickness = new Thickness(1.0);
		base.CornerRadius = new CornerRadius(10.0);
		base.MaxWidth = 760.0;
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		SetResourceReference(Border.BorderBrushProperty, "B.AccentSoft2");
		SetResourceReference(Border.BackgroundProperty, "B.Paper");
	}

	private static FrameworkElement Row(string title, string status)
	{
		bool flag = status == "completed" || status == "done";
		bool flag2 = status == "in_progress";
		Grid grid = new Grid
		{
			Width = 16.0,
			Height = 16.0
		};
		Ellipse ellipse = new Ellipse
		{
			StrokeThickness = 1.5
		};
		if (flag)
		{
			ellipse.SetResourceReference(Shape.FillProperty, "B.Accent");
			ellipse.SetResourceReference(Shape.StrokeProperty, "B.Accent");
			grid.Children.Add(ellipse);
			Path path = AiIcons.Path("M20 6L9 17l-5-5", 10.0, Brushes.White);
			path.StrokeThickness = 3.5;
			grid.Children.Add(path.Mid());
		}
		else if (flag2)
		{
			ellipse.SetResourceReference(Shape.StrokeProperty, "B.Accent");
			grid.Children.Add(ellipse);
			Ellipse ellipse2 = new Ellipse
			{
				Width = 6.0,
				Height = 6.0
			};
			ellipse2.SetResourceReference(Shape.FillProperty, "B.Accent");
			grid.Children.Add(ellipse2.Mid());
		}
		else
		{
			ellipse.SetResourceReference(Shape.StrokeProperty, "B.InkFaint");
			grid.Children.Add(ellipse);
		}
		TextBlock textBlock = Ui.Txt(title, 12.5, flag2, flag ? "B.InkMuted" : "B.Ink").Wrap();
		if (flag)
		{
			textBlock.TextDecorations = TextDecorations.Strikethrough;
		}
		Grid grid2 = Ui.G(null, "Auto,*");
		grid2.Add(grid.M(0.0, 2.0), 0);
		grid2.Add(textBlock.M(9.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid2.Margin = new Thickness(0.0, 2.0, 0.0, 2.0);
		return grid2;
	}
}
