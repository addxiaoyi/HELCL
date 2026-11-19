using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HelL.Services;

namespace HelL.Pages.Ai;

internal sealed class MdView : RichTextBox
{
	internal static readonly FontFamily Mono = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI");

	private string _text = "";

	private string _rendered;

	private readonly DispatcherTimer _throttle;

	private bool _pendingLive;

	public string Markdown => _text;

	public MdView()
	{
		base.IsReadOnly = true;
		base.IsDocumentEnabled = true;
		base.BorderThickness = new Thickness(0.0);
		base.Background = Brushes.Transparent;
		base.Padding = new Thickness(0.0);
		base.Focusable = true;
		base.IsTabStop = false;
		base.Cursor = Cursors.IBeam;
		base.FontSize = 13.5;
		base.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
		base.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		SetResourceReference(Control.ForegroundProperty, "B.Ink");
		base.Template = BuildTemplate();
		base.Document = new FlowDocument
		{
			PagePadding = new Thickness(0.0),
			FontSize = 13.5,
			LineHeight = 22.0
		};
		_throttle = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromMilliseconds(80.0)
		};
		_throttle.Tick += delegate
		{
			_throttle.Stop();
			Render(_pendingLive);
		};
		base.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
		{
			if (!e.Handled)
			{
				e.Handled = true;
				MouseWheelEventArgs e2 = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
				{
					RoutedEvent = UIElement.MouseWheelEvent,
					Source = this
				};
				(base.Parent as UIElement)?.RaiseEvent(e2);
			}
		};
	}

	private static ControlTemplate BuildTemplate()
	{
		ControlTemplate controlTemplate = new ControlTemplate(typeof(RichTextBox));
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
		frameworkElementFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
		frameworkElementFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
		frameworkElementFactory.SetValue(UIElement.FocusableProperty, false);
		controlTemplate.VisualTree = frameworkElementFactory;
		return controlTemplate;
	}

	public void SetMarkdown(string? text, bool live = false)
	{
		_text = text ?? "";
		if (live)
		{
			_pendingLive = true;
			if (!_throttle.IsEnabled)
			{
				_throttle.Start();
			}
		}
		else
		{
			_throttle.Stop();
			Render(live: false);
		}
	}

	private void Render(bool live)
	{
		if (_rendered == _text)
		{
			return;
		}
		_rendered = _text;
		FlowDocument document = base.Document;
		document.Blocks.Clear();
		List<MdBlock> list;
		try
		{
			list = MdParser.Parse(_text);
		}
		catch
		{
			list = new List<MdBlock>
			{
				new MdBlock
				{
					Kind = MdBlockKind.Paragraph,
					Inlines = new List<MdInline>
					{
						new MdInline
						{
							Text = _text
						}
					}
				}
			};
		}
		for (int i = 0; i < list.Count; i++)
		{
			Block block = BuildBlock(list[i], 0);
			if (block != null)
			{
				if (i == list.Count - 1)
				{
					block.Margin = new Thickness(block.Margin.Left, block.Margin.Top, block.Margin.Right, 0.0);
				}
				document.Blocks.Add(block);
			}
		}
	}

	private Block? BuildBlock(MdBlock b, int depth)
	{
		switch (b.Kind)
		{
		case MdBlockKind.Heading:
		{
			Paragraph paragraph4 = new Paragraph();
			paragraph4.Margin = new Thickness(0.0, (depth == 0) ? 6 : 2, 0.0, 6.0);
			paragraph4.FontWeight = FontWeights.SemiBold;
			Paragraph paragraph5 = paragraph4;
			paragraph5.FontSize = b.Level switch
			{
				1 => 18.0, 
				2 => 16.0, 
				3 => 14.5, 
				_ => 13.5, 
			};
			Paragraph paragraph6 = paragraph4;
			AddInlines(paragraph6.Inlines, b.Inlines);
			return paragraph6;
		}
		case MdBlockKind.Paragraph:
		{
			Paragraph paragraph3 = new Paragraph
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			AddInlines(paragraph3.Inlines, b.Inlines);
			return paragraph3;
		}
		case MdBlockKind.Bullet:
		case MdBlockKind.Ordered:
		{
			Section section2 = new Section
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			};
			int start = b.Start;
			for (int i = 0; i < b.Items.Count; i++)
			{
				int num = ((i < b.ItemDepth.Count) ? b.ItemDepth[i] : 0);
				Paragraph paragraph2 = new Paragraph
				{
					Margin = new Thickness(4 + num * 18, 1.0, 0.0, 1.0),
					TextIndent = -16.0,
					Padding = new Thickness(16.0, 0.0, 0.0, 0.0)
				};
				Run run = new Run(((b.Kind == MdBlockKind.Ordered && num == 0) ? (start++ + ".") : ((num == 0) ? "•" : "◦")) + " ");
				run.SetResourceReference(TextElement.ForegroundProperty, "B.InkMuted");
				paragraph2.Inlines.Add(run);
				AddInlines(paragraph2.Inlines, b.Items[i]);
				section2.Blocks.Add(paragraph2);
			}
			return section2;
		}
		case MdBlockKind.Quote:
		{
			Section section = new Section
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
				Padding = new Thickness(10.0, 2.0, 0.0, 2.0),
				BorderThickness = new Thickness(3.0, 0.0, 0.0, 0.0)
			};
			section.SetResourceReference(Block.BorderBrushProperty, "B.Line");
			section.SetResourceReference(TextElement.ForegroundProperty, "B.InkSoft");
			{
				foreach (MdBlock child in b.Children)
				{
					Block block = BuildBlock(child, depth + 1);
					if (block != null)
					{
						section.Blocks.Add(block);
					}
				}
				return section;
			}
		}
		case MdBlockKind.Rule:
		{
			Paragraph paragraph = new Paragraph();
			paragraph.Margin = new Thickness(0.0, 6.0, 0.0, 10.0);
			paragraph.BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0);
			paragraph.FontSize = 2.0;
			paragraph.SetResourceReference(Block.BorderBrushProperty, "B.Line");
			return paragraph;
		}
		case MdBlockKind.Code:
			return new BlockUIContainer(CodeBlock(b.Raw, b.Lang))
			{
				Margin = new Thickness(0.0, 2.0, 0.0, 10.0)
			};
		case MdBlockKind.Table:
			return BuildTable(b);
		default:
			return null;
		}
	}

	private Table BuildTable(MdBlock b)
	{
		Table table = new Table
		{
			CellSpacing = 0.0,
			Margin = new Thickness(0.0, 2.0, 0.0, 10.0),
			FontSize = 12.5
		};
		int num = Math.Max(1, b.Header.Count);
		for (int i = 0; i < num; i++)
		{
			table.Columns.Add(new TableColumn
			{
				Width = new GridLength(1.0, GridUnitType.Auto)
			});
		}
		TableRowGroup tableRowGroup = new TableRowGroup();
		TableRow tableRow = new TableRow();
		tableRow.SetResourceReference(TextElement.BackgroundProperty, "B.Paper2");
		for (int j = 0; j < num; j++)
		{
			tableRow.Cells.Add(Cell((j < b.Header.Count) ? b.Header[j] : new List<MdInline>(), header: true, Align(b, j)));
		}
		tableRowGroup.Rows.Add(tableRow);
		foreach (List<List<MdInline>> row in b.Rows)
		{
			TableRow tableRow2 = new TableRow();
			for (int k = 0; k < num; k++)
			{
				tableRow2.Cells.Add(Cell((k < row.Count) ? row[k] : new List<MdInline>(), header: false, Align(b, k)));
			}
			tableRowGroup.Rows.Add(tableRow2);
		}
		table.RowGroups.Add(tableRowGroup);
		return table;
	}

	private static TextAlignment Align(MdBlock b, int i)
	{
		string text = ((i < b.Align.Count) ? b.Align[i] : "left");
		if (text == "center")
		{
			return TextAlignment.Center;
		}
		if (!(text == "right"))
		{
			return TextAlignment.Left;
		}
		return TextAlignment.Right;
	}

	private TableCell Cell(List<MdInline> inl, bool header, TextAlignment align)
	{
		Paragraph paragraph = new Paragraph
		{
			Margin = new Thickness(0.0),
			TextAlignment = align
		};
		if (header)
		{
			paragraph.FontWeight = FontWeights.SemiBold;
			paragraph.SetResourceReference(TextElement.ForegroundProperty, "B.InkMuted");
		}
		AddInlines(paragraph.Inlines, inl);
		TableCell tableCell = new TableCell(paragraph);
		tableCell.Padding = new Thickness(10.0, 6.0, 14.0, 6.0);
		tableCell.BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0);
		tableCell.SetResourceReference(TableCell.BorderBrushProperty, "B.LineSoft");
		return tableCell;
	}

	private void AddInlines(InlineCollection target, List<MdInline> inlines)
	{
		foreach (MdInline inline2 in inlines)
		{
			Inline inline = BuildInline(inline2);
			if (inline != null)
			{
				target.Add(inline);
			}
		}
	}

	private Inline? BuildInline(MdInline x)
	{
		switch (x.Kind)
		{
		case MdInlineKind.Text:
			return new Run(x.Text);
		case MdInlineKind.Break:
			return new LineBreak();
		case MdInlineKind.Code:
		{
			Run run = new Run(" " + x.Text + " ");
			run.FontFamily = Mono;
			run.FontSize = 12.0;
			run.SetResourceReference(TextElement.BackgroundProperty, "B.Hover");
			run.SetResourceReference(TextElement.ForegroundProperty, "B.AccentText");
			return run;
		}
		case MdInlineKind.Bold:
		{
			Bold bold = new Bold();
			AddInlines(bold.Inlines, x.Children);
			return bold;
		}
		case MdInlineKind.Italic:
		{
			Italic italic = new Italic();
			AddInlines(italic.Inlines, x.Children);
			return italic;
		}
		case MdInlineKind.Strike:
		{
			Span span = new Span
			{
				TextDecorations = TextDecorations.Strikethrough
			};
			AddInlines(span.Inlines, x.Children);
			return span;
		}
		case MdInlineKind.Link:
		{
			Hyperlink hyperlink = new Hyperlink
			{
				Cursor = Cursors.Hand
			};
			hyperlink.SetResourceReference(TextElement.ForegroundProperty, "B.AccentText");
			AddInlines(hyperlink.Inlines, x.Children);
			string url = x.Url;
			hyperlink.ToolTip = url;
			hyperlink.Click += delegate
			{
				if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				{
					Ui.OpenUrl(url);
				}
			};
			return hyperlink;
		}
		default:
			return null;
		}
	}

	internal static FrameworkElement CodeBlock(string code, string lang)
	{
		TextBox textBox = new TextBox
		{
			Style = Ui.S("Input.Composer"),
			Text = code,
			IsReadOnly = true,
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			FontFamily = Mono,
			FontSize = 12.0,
			TextWrapping = TextWrapping.NoWrap,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Padding = new Thickness(0.0),
		};
		textBox.SetResourceReference(Control.ForegroundProperty, "B.Ink");
		textBox.SetResourceReference(TextBox.CaretBrushProperty, "B.Ink");
		TextBlock textBlock = Ui.Small(string.IsNullOrWhiteSpace(lang) ? I18n.L("代码") : lang);
		Button button = Ui.Btn(I18n.L("复制代码"), BtnKind.Ghost, glyph: "\ue8c8");
		button.FontSize = 12;
		button.Padding = new Thickness(6, 2, 6, 2);
		Button button2 = button;
		button2.Click += delegate
		{
			Ui.Copy(code);
		};
		Grid grid = Ui.G(null, "*,Auto").Add(textBlock.VCenter(), 0).Add(button2, 0, 1);
		Border border = new Border();
		border.SetResourceReference(Border.BackgroundProperty, "B.SurfaceInset");
		border.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
		border.BorderThickness = new Thickness(1);
		border.CornerRadius = new CornerRadius(6);
		border.Padding = new Thickness(12.0, 7.0, 12.0, 10.0);
		border.Child = Ui.V(4.0, grid, textBox);
		return border;
	}
}
