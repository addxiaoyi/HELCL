using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HelL.Services;

public static class Ui
{
	private sealed class RowFactory<T> : IValueConverter
	{
		private readonly Func<T, UIElement> _row;

		public RowFactory(Func<T, UIElement> row)
		{
			_row = row;
		}

		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (!(value is T arg))
			{
				return null;
			}
			return _row(arg);
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return Binding.DoNothing;
		}
	}

	internal static readonly FontFamily GlyphFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

	public static Style S(string key)
	{
		return (Style)Application.Current.FindResource(key);
	}

	public static Brush Res(string key)
	{
		return (Brush)Application.Current.FindResource(key);
	}

	public static TextBlock Txt(string text = "", double size = UiMetrics.BodySize, bool bold = false, string? fg = null)
	{
		TextBlock textBlock = new TextBlock
		{
			Text = text,
			FontSize = size,
			LineHeight = Math.Ceiling(size * 1.5),
			TextWrapping = TextWrapping.NoWrap
		};
		if (bold)
		{
			textBlock.FontWeight = FontWeights.SemiBold;
		}
		if (fg != null)
		{
			textBlock.SetResourceReference(TextBlock.ForegroundProperty, fg);
		}
		return textBlock;
	}

	public static TextBlock H1(string text)
	{
		return Styled(text, "T.H1");
	}

	public static TextBlock H2(string text)
	{
		return Styled(text, "T.H2");
	}

	public static TextBlock H3(string text)
	{
		return Styled(text, "T.H3");
	}

	public static TextBlock Muted(string text)
	{
		return Styled(text, "T.Muted");
	}

	public static TextBlock Small(string text)
	{
		return Styled(text, "T.Small");
	}

	public static TextBlock Mono(string text)
	{
		return Styled(text, "T.Mono");
	}

	private static TextBlock Styled(string text, string key)
	{
		return new TextBlock
		{
			Text = text,
			Style = S(key)
		};
	}

	public static TextBlock Glyph(string glyph, double size = 14.0, string? fg = null)
	{
		TextBlock textBlock = new TextBlock
		{
			Style = S("T.Glyph"),
			Text = glyph,
			FontFamily = GlyphFont,
			FontSize = size,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (fg != null)
		{
			textBlock.SetResourceReference(TextBlock.ForegroundProperty, fg);
		}
		return textBlock;
	}

	public static SPanel V(double spacing, params UIElement?[] kids)
	{
		return Fill(new SPanel
		{
			Orientation = Orientation.Vertical,
			Spacing = spacing
		}, kids);
	}

	public static SPanel H(double spacing, params UIElement?[] kids)
	{
		return Fill(new SPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = spacing
		}, kids);
	}

	private static SPanel Fill(SPanel p, UIElement?[] kids)
	{
		foreach (UIElement uIElement in kids)
		{
			if (uIElement != null)
			{
				p.Children.Add(uIElement);
			}
		}
		return p;
	}

	public static Grid G(string? rows = null, string? cols = null)
	{
		Grid grid = new Grid();
		foreach (GridLength item in Parse(rows))
		{
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = item
			});
		}
		foreach (GridLength item2 in Parse(cols))
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = item2
			});
		}
		return grid;
	}

	private static IEnumerable<GridLength> Parse(string? spec)
	{
		if (string.IsNullOrWhiteSpace(spec))
		{
			yield break;
		}
		string[] array = spec.Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			double result;
			double result2;
			if (text == "Auto")
			{
				yield return GridLength.Auto;
			}
			else if (text == "*")
			{
				yield return new GridLength(1.0, GridUnitType.Star);
			}
			else if (text.EndsWith('*') && double.TryParse(text.Substring(0, text.Length - 1), out result))
			{
				yield return new GridLength(result, GridUnitType.Star);
			}
			else if (double.TryParse(text, out result2))
			{
				yield return new GridLength(result2);
			}
			else
			{
				yield return GridLength.Auto;
			}
		}
	}

	public static Border Card(UIElement? content = null, double padding = VisualPolishUtils.StandardCardPadding, string style = "Card")
	{
		Border border = new Border
		{
			Style = S(style),
			Padding = new Thickness(padding)
		};
		if (content != null)
		{
			border.Child = content;
		}
		return border;
	}

    public static Border Inset(UIElement content, double padding = 16)
    {
        var surface = new Border { Child = content, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(0), Padding = new Thickness(padding) };
        surface.SetResourceReference(Border.BackgroundProperty, "B.SurfaceInset");
        return surface;
    }

	public static Border Pane(UIElement? content, string bg = "B.Paper2", double radius = 10.0, double padding = 12.0)
	{
		Border border = new Border
		{
			CornerRadius = new CornerRadius(radius),
			Padding = new Thickness(padding)
		};
		border.SetResourceReference(Border.BackgroundProperty, bg);
		if (content != null)
		{
			border.Child = content;
		}
		return border;
	}

	/// <summary>PCL 式字段组：行与行之间只留行距，不画分隔线，也不再套卡片。</summary>
	public static SPanel Fields(params UIElement?[] rows)
	{
		SPanel panel = new SPanel
		{
			Orientation = Orientation.Vertical,
			Spacing = VisualPolishUtils.StandardFieldSpacing
		};
		foreach (UIElement? row in rows)
		{
			if (row != null)
			{
				panel.Children.Add(row);
			}
		}
		return panel;
	}

	/// <summary>
	/// PCL 式页面分栏：左边一条窄分类列表，右边内容铺满（对应它的 PageSelectLeft / PageSelectRight）。
	/// 传了 <paramref name="foot"/> 就在底部压一条分隔线加一行常驻操作，不随右边内容滚动。
	/// </summary>
	public static Grid Split(UIElement rail, UIElement content, UIElement? foot = null, double railWidth = 180.0)
	{
		Grid grid = G((foot == null) ? "*" : "*,Auto,Auto", $"{railWidth},*");
		rail.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
		grid.Add(rail, 0, 0);
		grid.Add(content, 0, 1);
		if (foot != null)
		{
			grid.Add(Sep(), 1, 0, 1, 2);
			grid.Add(foot, 2, 0, 1, 2);
		}
		return grid;
	}

	public static SmoothScroll Scroll(UIElement content, double padding = 0.0)
	{
		return new SmoothScroll
		{
			Content = content,
			Padding = new Thickness(padding)
		};
	}

	public static ListBox VirtualList<T>(Func<T, UIElement> row, double rowSpacing = 10.0)
	{
		ListBox obj = new ListBox
		{
			Style = S("PlainList"),
			Focusable = false,
			ItemTemplate = RowTemplate(row),
			ItemContainerStyle = BareContainer(rowSpacing)
		};
		ScrollViewer.SetCanContentScroll(obj, canContentScroll: true);
		ScrollViewer.SetHorizontalScrollBarVisibility(obj, ScrollBarVisibility.Disabled);
		return obj;
	}

	private static DataTemplate RowTemplate<T>(Func<T, UIElement> row)
	{
		FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(ContentPresenter));
		frameworkElementFactory.SetBinding(ContentPresenter.ContentProperty, new Binding
		{
			Converter = new RowFactory<T>(row)
		});
		DataTemplate dataTemplate = new DataTemplate();
		dataTemplate.VisualTree = frameworkElementFactory;
		dataTemplate.Seal();
		return dataTemplate;
	}

	private static Style BareContainer(double spacing)
	{
		ControlTemplate controlTemplate = new ControlTemplate(typeof(ListBoxItem));
		controlTemplate.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
		Style style = new Style(typeof(ListBoxItem));
		style.Setters.Add(new Setter(Control.TemplateProperty, controlTemplate));
		style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
		// Recycled rows must shrink with the viewport instead of retaining their widest measured size.
		style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, new Binding("ActualWidth")
		{
			RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ScrollContentPresenter), 1)
		}));
		style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0.0, 0.0, 0.0, spacing)));
		style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
		style.Seal();
		return style;
	}

	public static Border Sep(bool vertical = false)
	{
		Border border = new Border();
		border.SetResourceReference(Border.BackgroundProperty, "B.LineSoft");
		if (vertical)
		{
			border.Width = 1.0;
			border.VerticalAlignment = VerticalAlignment.Stretch;
		}
		else
		{
			border.Height = 1.0;
			border.HorizontalAlignment = HorizontalAlignment.Stretch;
		}
		return border;
	}

	public static FrameworkElement Spring()
	{
		return new Border
		{
			Width = 0.0,
			Height = 0.0
		};
	}

	public static Button Btn(string text, BtnKind kind = BtnKind.Normal, RoutedEventHandler? click = null, string? glyph = null)
	{
		Button button = new Button
		{
			Style = S(StyleOf(kind))
		};
		button.Content = glyph == null ? text : H(6, Glyph(glyph),
			string.IsNullOrEmpty(text) ? null : new TextBlock { Text = text, Style = S("T.Control") });
		System.Windows.Automation.AutomationProperties.SetName(button, text);
		if (click != null)
		{
			button.Click += click;
		}
		return button;
	}

	public static Button IconBtn(string glyph, string? tip = null, RoutedEventHandler? click = null, double size = 14.0)
	{
		Button button = new Button
		{
			Style = S("Btn.Icon"),
			Content = Glyph(glyph, size)
		};
		if (tip != null)
		{
			button.ToolTip = tip;
			System.Windows.Automation.AutomationProperties.SetName(button, tip);
		}
		if (click != null)
		{
			button.Click += click;
		}
		return button;
	}

	private static string StyleOf(BtnKind k)
	{
		return k switch
		{
			BtnKind.Primary => "Btn.Primary", 
			BtnKind.Soft => "Btn.Soft", 
			BtnKind.Ghost => "Btn.Ghost", 
			BtnKind.Danger => "Btn.Danger", 
			BtnKind.Chip => "Btn.Chip", 
			BtnKind.Icon => "Btn.Icon", 
			_ => "Btn.Normal", 
		};
	}

	public static TextBox Input(string placeholder = "", string text = "", double width = double.NaN)
	{
		TextBox textBox = new TextBox
		{
			Style = S("Input"),
			Tag = placeholder,
			Text = text
		};
		if (!double.IsNaN(width))
		{
			textBox.Width = width;
		}
		return textBox;
	}

	public static TextBox Multi(string placeholder = "", string text = "", double height = 90.0)
	{
		return new TextBox
		{
			Style = S("Input.Multi"),
			Tag = placeholder,
			Text = text,
			Height = height
		};
	}

	public static TextBox LogBox()
	{
		return new TextBox
		{
			Style = S("Input.Log")
		};
	}

	public static PasswordBox Pw(double width = double.NaN)
	{
		PasswordBox passwordBox = new PasswordBox
		{
			Style = S("Pw")
		};
		if (!double.IsNaN(width))
		{
			passwordBox.Width = width;
		}
		return passwordBox;
	}

	public static ComboBox Combo(IEnumerable<string>? items = null, string? selected = null, double width = double.NaN, string placeholder = "", bool editable = false)
	{
		ComboBox comboBox = new ComboBox
		{
			Style = S("Combo"),
			Tag = placeholder,
			IsEditable = editable
		};
		if (items != null)
		{
			foreach (string item in items)
			{
				comboBox.Items.Add(item);
			}
		}
		if (selected != null && comboBox.Items.Contains(selected))
		{
			comboBox.SelectedItem = selected;
		}
		else if (comboBox.Items.Count > 0)
		{
			comboBox.SelectedIndex = 0;
		}
		if (!double.IsNaN(width))
		{
			comboBox.Width = width;
		}
		return comboBox;
	}

	public static CheckBox Check(string label, bool value = false, RoutedEventHandler? changed = null)
	{
		CheckBox checkBox = new CheckBox
		{
			Style = S("Chk"),
			Content = label,
			IsChecked = value
		};
		if (changed != null)
		{
			checkBox.Checked += changed;
			checkBox.Unchecked += changed;
		}
		return checkBox;
	}

	public static CheckBox Switch(bool value = false, RoutedEventHandler? changed = null)
	{
		CheckBox checkBox = new CheckBox
		{
			Style = S("Switch"),
			IsChecked = value,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (changed != null)
		{
			checkBox.Checked += changed;
			checkBox.Unchecked += changed;
		}
		return checkBox;
	}

	public static Slider Sld(double min, double max, double value, double tick = 1.0)
	{
		return new Slider
		{
			Style = S("Sld"),
			Minimum = min,
			Maximum = max,
			Value = Clamp.Of(value, min, max),
			TickFrequency = tick,
			IsSnapToTickEnabled = (tick > 0.0)
		};
	}

	public static ProgressBar Prog(double value = 0.0)
	{
		return new ProgressBar
		{
			Style = S("Prog"),
			Value = value,
			Maximum = 100.0
		};
	}

	public static Border Tag(string text, string fg = "B.InkMuted", string bg = "B.LineSoft")
	{
		Border border = new Border();
		border.CornerRadius = new CornerRadius(5.0);
		border.Padding = new Thickness(6.0, 1.5, 6.0, 2.5);
		border.VerticalAlignment = VerticalAlignment.Center;
		border.Child = Txt(text, 11.0, bold: false, fg);
		border.SetResourceReference(Border.BackgroundProperty, bg);
		return border;
	}

	public static Border Badge(string text)
	{
		TextBlock textBlock = new TextBlock
		{
			Text = text,
			FontSize = 11.0,
			FontWeight = FontWeights.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		textBlock.SetResourceReference(TextBlock.ForegroundProperty, "B.OnAccentStrong");
		Border border = new Border();
		border.CornerRadius = new CornerRadius(9.0);
		border.Padding = new Thickness(6.0, 0.0, 6.0, 1.0);
		border.MinWidth = 18.0;
		border.Height = 18.0;
		border.VerticalAlignment = VerticalAlignment.Center;
		border.Child = textBlock;
		border.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
		return border;
	}

	public static Grid Field(string label, UIElement control, string? hint = null, double labelWidth = 170.0)
	{
		TextBlock textBlock = Txt(label);
		textBlock.TextWrapping = TextWrapping.Wrap;
		System.Windows.Automation.AutomationProperties.SetLabeledBy(control, textBlock);
		control.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
		if (control is CheckBox || control is Button || control is FrameworkElement { Width: > 0 } element && !double.IsNaN(element.Width))
			control.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
		TextBlock? description = string.IsNullOrWhiteSpace(hint) ? null : Small(hint).Wrap();
		var notes = V(4, description, FieldState.ErrorText(control));
		double labelSize = string.IsNullOrEmpty(label) ? 0 : double.IsNaN(labelWidth) ? 150 : labelWidth;
		Grid grid = new FieldLayout(textBlock, control, notes, labelSize);
		grid.Margin = new Thickness(0, 2, 0, 2);
		if (hint != null)
		{
			System.Windows.Automation.AutomationProperties.SetHelpText(control, hint);
		}
		return grid;
	}

	public static Grid Section(string title, string? sub = null, UIElement? right = null)
	{
		TextBlock textBlock = ((sub == null) ? null : Muted(sub));
		if (textBlock != null)
		{
			textBlock.TextWrapping = TextWrapping.Wrap;
		}
		SPanel el = V(4, H2(title), textBlock);
		el.Margin = new Thickness(0, 0, 16, 8);
		Grid grid;
		if (right != null)
		{
			if (right is SPanel actions) actions.Wrap = true;
			right.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			grid = new PageHeader(el, right);
		}
		else grid = G().Add(el, 0);
		return grid;
	}

	public static SPanel Empty(string glyph, string title, string? hint = null)
	{
		SPanel sPanel = V(8.0, Glyph(glyph, 34.0, "B.InkFaint"), new TextBlock
		{
			Text = title,
			FontSize = 14.0,
			HorizontalAlignment = HorizontalAlignment.Center
		}.Dyn(TextBlock.ForegroundProperty, "B.InkMuted"), (hint == null) ? null : new TextBlock
		{
			Text = hint,
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			MaxWidth = 420.0,
			TextAlignment = TextAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center
		}.Dyn(TextBlock.ForegroundProperty, "B.InkFaint"));
		sPanel.HorizontalAlignment = HorizontalAlignment.Center;
		sPanel.VerticalAlignment = VerticalAlignment.Center;
		sPanel.Margin = new Thickness(0.0, 46.0, 0.0, 46.0);
		foreach (UIElement child in sPanel.Children)
		{
			child.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
		}
		return sPanel;
	}

	public static SPanel EmptyError(string title, string detail, Action retry)
	{
		SPanel sPanel = Empty("\ue72c", title, detail);
		Button button = Btn(I18n.L("重试"), BtnKind.Soft, delegate
		{
			retry();
		}, "\ue72c");
		button.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		button.HorizontalAlignment = HorizontalAlignment.Center;
		sPanel.Children.Add(button);
		return sPanel;
	}

	public static bool Copy(string text, string? title = null, string? body = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			AppServices.Toast(I18n.L("没有可复制的内容"), "", ToastKind.Warning);
			return false;
		}
		try
		{
			Clipboard.SetText(text);
		}
		catch (Exception)
		{
			AppServices.Toast(I18n.L("复制失败"), I18n.L("剪贴板正被其他程序占用，稍后再试"), ToastKind.Warning);
			return false;
		}
		AppServices.Toast(title ?? I18n.L("已复制"), body ?? "", ToastKind.Success);
		return true;
	}

	public static bool FromButton(DependencyObject? src)
	{
		while (src != null)
		{
			if (src is ButtonBase)
			{
				return true;
			}
			src = ParentOf(src);
		}
		return false;
	}

	internal static DependencyObject? ParentOf(DependencyObject source)
	{
		if (source is Visual || source is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(source);
		if (source is ContentElement content) return ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent;
		return LogicalTreeHelper.GetParent(source);
	}

	public static Border Skeleton(double height = 16.0, double width = double.NaN)
	{
		Border b = new Border
		{
			Height = height,
			CornerRadius = new CornerRadius(6.0),
			ClipToBounds = true
		};
		if (!double.IsNaN(width))
		{
			b.Width = width;
		}
		b.SetResourceReference(Border.BackgroundProperty, "B.LineSoft");
		Border border = new Border
		{
			Width = 90.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			Background = new LinearGradientBrush(new GradientStopCollection
			{
				new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0),
				new GradientStop(Color.FromArgb(120, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.5),
				new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0)
			}, 0.0)
		};
		TranslateTransform tt = new TranslateTransform(-90.0, 0.0);
		border.RenderTransform = tt;
		b.Child = border;
		Motion.Loop(b, delegate
		{
			tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-90.0, Math.Max(520.0, b.ActualWidth + 90.0), TimeSpan.FromMilliseconds(1150.0))
			{
				RepeatBehavior = RepeatBehavior.Forever
			});
		}, delegate
		{
			tt.BeginAnimation(TranslateTransform.XProperty, null);
		});
		return b;
	}

	public static Border RowCard(UIElement content, MouseButtonEventHandler? click = null, double padding = 12.0)
	{
		Border border = new Border
		{
			Style = S("List.Row"),
			Padding = new Thickness(8, padding, 8, padding),
			Child = content,
			Cursor = ((click != null) ? Cursors.Hand : null)
		};
		if (click != null)
		{
			border.MouseLeftButtonUp += click;
		}
		return border;
	}

	public static void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			AppServices.Toast(I18n.L("打不开链接"), ex.Message, ToastKind.Warning);
		}
	}

	/// <summary>
	/// 现代化高级胶囊分段选择器（Segmented Control）
	/// </summary>
	public static Border Segmented(IReadOnlyList<string> options, Action<int> onSelect, int initial = 0)
	{
		Border container = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(3.0),
			BorderThickness = new Thickness(1.0),
			HorizontalAlignment = HorizontalAlignment.Left
		};
		container.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		container.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");

		Grid grid = new Grid();
		for (int i = 0; i < options.Count; i++)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		}

		List<Button> buttons = new List<Button>();
		int selected = Math.Max(0, Math.Min(initial, options.Count - 1));

		for (int i = 0; i < options.Count; i++)
		{
			int index = i;
			Button btn = new Button
			{
				Content = options[i],
				Cursor = Cursors.Hand,
				Padding = new Thickness(14.0, 5.0, 14.0, 5.0),
				FontSize = 12.5,
				FontWeight = (index == selected) ? FontWeights.SemiBold : FontWeights.Normal,
				BorderThickness = new Thickness(0.0)
			};
			btn.SetResourceReference(Control.BackgroundProperty, (index == selected) ? "B.Paper" : "B.Paper2");
			btn.SetResourceReference(Control.ForegroundProperty, (index == selected) ? "B.Ink" : "B.InkMuted");

			btn.Click += delegate
			{
				for (int j = 0; j < buttons.Count; j++)
				{
					bool isCurrent = (j == index);
					buttons[j].FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal;
					buttons[j].SetResourceReference(Control.BackgroundProperty, isCurrent ? "B.Paper" : "B.Paper2");
					buttons[j].SetResourceReference(Control.ForegroundProperty, isCurrent ? "B.Ink" : "B.InkMuted");
				}
				onSelect(index);
			};

			Grid.SetColumn(btn, i);
			grid.Children.Add(btn);
			buttons.Add(btn);
		}

		container.Child = grid;
		return container;
	}

	/// <summary>
	/// 现代化高级折叠面板
	/// </summary>
	public static Expander ModernExpander(string header, UIElement content, bool isExpanded = false)
	{
		return new Expander
		{
			Header = header,
			Content = content,
			IsExpanded = isExpanded,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
	}

	/// <summary>
	/// 现代化高级分组框
	/// </summary>
	public static GroupBox ModernGroupBox(string header, UIElement content)
	{
		return new GroupBox
		{
			Header = header,
			Content = content,
			Margin = new Thickness(0.0, 6.0, 0.0, 6.0)
		};
	}
}
