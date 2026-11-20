using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HelL.Services;

/// <summary>
/// PCL 式页面左栏：一页一个分类，一行 36 高，选中行左边一条强调色竖条。
/// 只管选中态，右边换什么内容由 <see cref="Selected"/> 的订阅者决定。
/// 行样式跟着皮肤走（Win11 用 Rail.Item，mac 用 Rail.Item.Mac）。
/// </summary>
public sealed class Rail : StackPanel
{
	private readonly Dictionary<string, ToggleButton> _rows = new Dictionary<string, ToggleButton>();

	private readonly List<string> _order = new List<string>();

	private string _key = "";

	public event EventHandler? Selected;

	public string Key => _key;

	public Rail()
	{
		Orientation = Orientation.Vertical;
		Margin = new Thickness(0.0, 12.0, 0.0, 0.0);
		App.ThemeVisualsChanged += Restyle;
	}

	/// <summary>换皮肤或方向后把行换成对应样式（竖排跟皮肤走，窄窗横排用底部指示条）。</summary>
	public void Restyle()
	{
		bool horizontal = Orientation == Orientation.Horizontal;
		string key = horizontal ? "Rail.Item.Horizontal" : (Glass.IsMac ? "Rail.Item.Mac" : "Rail.Item");
		for (int i = 0; i < _order.Count; i++)
		{
			ToggleButton row = _rows[_order[i]];
			row.Style = Ui.S(key);
			row.Margin = horizontal ? new Thickness(0.0, 0.0, 4.0, 0.0) : (i > 0 ? new Thickness(0.0, 2.0, 0.0, 0.0) : new Thickness(0.0));
		}
	}

	public void Add(string key, string label, string glyph)
	{
		Grid grid = Ui.G(null, "18,*,Auto");
		grid.Add(Ui.Glyph(glyph, 16.0), 0);
		TextBlock text = Ui.Txt(label, 13.0).VCenter().Trim();
		grid.Add(text.M(10.0, 0.0, 0.0, 0.0), 0, 1);
		ToggleButton btn = new ToggleButton
		{
			Style = Ui.S(Glass.IsMac ? "Rail.Item.Mac" : "Rail.Item"),
			Content = grid,
			Tag = key,
			HorizontalContentAlignment = HorizontalAlignment.Stretch
		};
		btn.Tip(label);
		btn.Click += delegate
		{
			Select(key);
		};
		_rows[key] = btn;
		_order.Add(key);
		if (_order.Count > 1)
		{
			btn.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
		}
		Children.Add(btn);
	}

	public void Select(string key)
	{
		if (!_rows.ContainsKey(key))
		{
			return;
		}
		bool changed = _key != key;
		_key = key;
		foreach (KeyValuePair<string, ToggleButton> row in _rows)
		{
			row.Value.IsChecked = row.Key == key;
		}
		if (changed)
		{
			Selected?.Invoke(this, EventArgs.Empty);
		}
	}
}
