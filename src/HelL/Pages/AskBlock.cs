using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HelL.Services;

namespace HelL.Pages;

internal sealed class AskBlock
{
	private readonly List<(ToggleButton Btn, string Id, string Label)> _opts = new List<(ToggleButton, string, string)>();

	private readonly TextBox _other = Ui.Input(I18n.L("选「其它」时在这里填"));

	private readonly bool _multi;

	private readonly ToggleButton? _otherBtn;

	public string Id { get; }

	public string Prompt { get; }

	public SPanel Root { get; }

	public AskBlock(JsonElement q)
	{
		string text = Str(q, "id");
		Id = ((text != null && text.Length > 0) ? text : "q1");
		string text2 = Str(q, "prompt");
		Prompt = ((text2 != null && text2.Length > 0) ? text2 : I18n.L("请选择"));
		_multi = q.TryGetProperty("allow_multiple", out var value) && value.ValueKind == JsonValueKind.True;
		Root = Ui.V(5.0, Ui.Txt(Prompt + (_multi ? I18n.L("（可多选）") : ""), 12.5).Wrap());
		string groupName = Guid.NewGuid().ToString("N");
		if (q.TryGetProperty("options", out var value2) && value2.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in value2.EnumerateArray())
			{
				string text3 = Str(item, "id");
				string text4 = Str(item, "label");
				string text5 = ((text4 != null && text4.Length > 0) ? text4 : text3);
				ToggleButton toggleButton = (_multi ? ((ToggleButton)new CheckBox
				{
					Content = text5
				}) : ((ToggleButton)new RadioButton
				{
					Content = text5,
					GroupName = groupName
				}));
				toggleButton.Margin = new Thickness(0.0, 1.0, 0.0, 1.0);
				toggleButton.Checked += delegate
				{
					SyncOther();
				};
				toggleButton.Unchecked += delegate
				{
					SyncOther();
				};
				if (text3 == "other")
				{
					_otherBtn = toggleButton;
				}
				_opts.Add((toggleButton, text3, text5));
				Root.Children.Add(toggleButton);
			}
		}
		_other.Visibility = Visibility.Collapsed;
		Root.Children.Add(_other);
	}

	private void SyncOther()
	{
		TextBox other = _other;
		ToggleButton otherBtn = _otherBtn;
		other.Visibility = ((otherBtn == null || otherBtn.IsChecked != true) ? Visibility.Collapsed : Visibility.Visible);
	}

	public object? Collect()
	{
		List<Dictionary<string, object>> list = (from o in _opts
			where o.Btn.IsChecked == true
			select new Dictionary<string, object>
			{
				["id"] = o.Id,
				["label"] = o.Label
			}).ToList();
		string text = _other.Text?.Trim() ?? "";
		if (list.Count == 0 && text.Length == 0)
		{
			return null;
		}
		return new Dictionary<string, object>
		{
			["picked"] = list,
			["other"] = text
		};
	}

	private static string Str(JsonElement el, string key)
	{
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(key, out var value))
		{
			return "";
		}
		if (value.ValueKind != JsonValueKind.String)
		{
			return value.ToString();
		}
		return value.GetString() ?? "";
	}
}
