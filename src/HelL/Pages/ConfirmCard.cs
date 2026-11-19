using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Core.Launcher.Ai;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL.Pages;

internal sealed class ConfirmCard : Border
{
	private readonly Action<bool, bool, string> _answer;

	private bool _done;

	internal AiRisk Risk { get; }

	internal bool Answered => _done;

	internal event Action<bool>? Resolved;

	public ConfirmCard(string label, string detail, bool allowAlways, Action<bool, bool, string> answer)
		: this("", label, detail, new List<string>(), allowAlways, answer)
	{
	}

	public ConfirmCard(string tool, string label, string reason, List<string> preview, bool allowAlways, Action<bool, bool, string> answer)
	{
		ConfirmCard confirmCard = this;
		_answer = answer;
		string sideEffect = "";
		try
		{
			if (!string.IsNullOrEmpty(tool) && ToolMeta.Map.TryGetValue(tool, out ToolMetaInfo value))
			{
				sideEffect = value.side_effect;
			}
		}
		catch
		{
		}
		Risk = AiUiText.RiskOf(tool, sideEffect);
		string text = Risk switch
		{
			AiRisk.Delete => "B.Danger", 
			AiRisk.Write => "B.Warn", 
			AiRisk.Network => "B.Info", 
			_ => "B.InkMuted", 
		};
		string bg = Risk switch
		{
			AiRisk.Delete => "B.DangerSoft", 
			AiRisk.Write => "B.WarnSoft", 
			AiRisk.Network => "B.InfoSoft", 
			_ => "B.Hover", 
		};
		string data = Risk switch
		{
			AiRisk.Delete => "M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6", 
			AiRisk.Network => "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M3 12h18 M12 3a14 14 0 0 1 0 18 M12 3a14 14 0 0 0 0 18", 
			_ => tool.StartsWith("write_", StringComparison.Ordinal) ? "M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z" : (tool.StartsWith("install_", StringComparison.Ordinal) ? "M21 8l-9-5-9 5v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8" : "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z M12 9v4 M12 17h.01"), 
		};
		Grid grid = Ui.G(null, "Auto,*");
		grid.Add(AiIcons.Icon("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", 16.0, "B.InkMuted").VCenter(), 0);
		grid.Add(Ui.H(6.0, Ui.Txt(I18n.L("助手要执行一个操作，需要你确认"), 13.0, bold: true, "B.Ink").VCenter()).M(9.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Border border = new Border
		{
			Padding = new Thickness(14.0, 10.0, 14.0, 10.0),
			Child = grid
		};
		border.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		Border el = Ui.Tag(RiskLabel(Risk), text, bg);
		TextBlock el2 = Ui.Txt(label, 13.0, bold: true, "B.Ink").Wrap();
		Grid grid2 = Ui.G(null, "Auto,*,Auto");
		grid2.Add(AiIcons.Icon(data, 15.0, text).M(0.0, 2.0), 0);
		grid2.Add(el2.M(9.0, 0.0, 10.0), 0, 1);
		grid2.Add(el.VCenter(), 0, 2);
		SPanel sPanel = Ui.V(6.0, grid2);
		if (Risk == AiRisk.Delete)
		{
			sPanel.Children.Add(Ui.Txt(I18n.L("删除类操作：如果这一轮没有检查点，删掉后无法一键撤回"), 12.0, bold: false, "B.Danger").Wrap().M(24.0, 0.0, 0.0, 0.0));
		}
		List<string> list = preview.Where((string l) => l != null).ToList();
		if (list.Count > 0)
		{
			sPanel.Children.Add(PreviewBox(list).M(24.0, 2.0));
		}
		if (!string.IsNullOrWhiteSpace(reason))
		{
			sPanel.Children.Add(Ui.Txt(reason.Trim(), 12.0, bold: false, "B.InkMuted").Wrap().M(24.0, 0.0, 0.0, 0.0));
		}
		Border border2 = new Border
		{
			Padding = new Thickness(14.0, 11.0, 14.0, 11.0),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = Ui.Scroll(sPanel)
		};
		border2.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		Button yes = Ui.Btn(I18n.L("允许"), BtnKind.Primary, null, "\ue73e");
		Button always = Ui.Btn(I18n.L("本对话都允许"));
		ComboBox scope = Ui.Combo(new string[2]
		{
			I18n.L("仅当前实例"),
			I18n.L("所有实例")
		}, null, 132.0);
		scope.SelectedIndex = 0;
		Button no = Ui.Btn(I18n.L("拒绝"), BtnKind.Ghost);
		always.ToolTip = I18n.L("同类操作以后不再询问");
		always.Visibility = ((!allowAlways) ? Visibility.Collapsed : Visibility.Visible);
		scope.Visibility = ((!allowAlways) ? Visibility.Collapsed : Visibility.Visible);
		TextBlock state = Ui.Txt("", 12.0, bold: false, "B.InkMuted");
		var actions = Ui.H(8, yes, always, scope.VCenter(), no);
		actions.Wrap = true;
		state.Visibility = Visibility.Collapsed;
		Border border3 = new Border
		{
			Padding = new Thickness(14.0, 9.0, 14.0, 10.0),
			BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
			Child = Ui.V(6, actions, state)
		};
		border3.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		border3.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		base.CornerRadius = new CornerRadius(10.0);
		base.BorderThickness = new Thickness(1.0);
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		base.MaxWidth = 760.0;
		base.ClipToBounds = true;
		SetResourceReference(Border.BackgroundProperty, "B.Paper");
		SetResourceReference(Border.BorderBrushProperty, "B.Line");
		Child = Ui.G("Auto,*,Auto").Add(border, 0).Add(border2, 1).Add(border3, 2);
		yes.Click += delegate
		{
			Done(ok: true, remember: false);
		};
		always.Click += delegate
		{
			Done(ok: true, remember: true);
		};
		no.Click += delegate
		{
			Done(ok: false, remember: false);
		};
		void Done(bool ok, bool remember)
		{
			if (!confirmCard._done)
			{
				confirmCard._done = true;
				yes.IsEnabled = false;
				always.IsEnabled = false;
				no.IsEnabled = false;
				scope.IsEnabled = false;
				state.Text = ((!ok) ? I18n.L("已拒绝") : (remember ? I18n.L("已记住并允许") : I18n.L("已允许")));
				state.Visibility = Visibility.Visible;
				confirmCard._answer(ok, remember, (scope.SelectedIndex == 1) ? "global" : "instance");
				confirmCard.Resolved?.Invoke(ok);
			}
		}
	}

	internal void Reject()
	{
		if (!_done)
		{
			_done = true;
			_answer(arg1: false, arg2: false, "instance");
			this.Resolved?.Invoke(obj: false);
		}
	}

	private static string RiskLabel(AiRisk r)
	{
		return r switch
		{
			AiRisk.Delete => I18n.L("删除"), 
			AiRisk.Write => I18n.L("写入"), 
			AiRisk.Network => I18n.L("联网"), 
			_ => I18n.L("只读"), 
		};
	}

	private static FrameworkElement PreviewBox(List<string> lines)
	{
		bool flag = AiUiText.LooksLikeDiff(lines);
		SPanel sPanel = Ui.V(0.0);
		foreach (string item in lines.Take(40))
		{
			char c = (flag ? AiUiText.DiffKind(item) : ' ');
			bool hasMarker = flag && (c == '+' || c == '-') && item.Length > 0 && item[0] == c;
			string bodyText = hasMarker ? item.Substring(1).TrimStart(' ') : item;
			TextBlock textBlock = new TextBlock
			{
				Text = bodyText,
				FontFamily = MdView.Mono,
				FontSize = 11.5,
				TextWrapping = TextWrapping.Wrap,
				Padding = new Thickness(hasMarker ? 0.0 : 10.0, 2.5, 10.0, 2.5)
			};
			UIElement lineChild = textBlock;
			if (hasMarker)
			{
				Grid diffRow = Ui.G(null, "22,*");
				TextBlock sign = new TextBlock
				{
					Text = c.ToString(),
					FontFamily = MdView.Mono,
					FontSize = 11.5,
					HorizontalAlignment = HorizontalAlignment.Center,
					Padding = new Thickness(0.0, 2.5, 0.0, 2.5)
				};
				sign.SetResourceReference(TextBlock.ForegroundProperty, c == '-' ? "B.Danger" : "B.Ok");
				diffRow.Add(sign, 0);
				diffRow.Add(textBlock, 0, 1);
				lineChild = diffRow;
			}
			Border border = new Border
			{
				Child = lineChild
			};
			switch (c)
			{
			case '-':
				border.Background = new SolidColorBrush(Color.FromArgb(34, 214, 69, 69));
				textBlock.SetResourceReference(TextBlock.ForegroundProperty, "B.Danger");
				break;
			case '+':
				border.Background = new SolidColorBrush(Color.FromArgb(32, 86, 197, 150));
				textBlock.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
				break;
			case '@':
				textBlock.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
				break;
			default:
				textBlock.SetResourceReference(TextBlock.ForegroundProperty, "B.InkSoft");
				break;
			}
			sPanel.Children.Add(border);
		}
		if (lines.Count > 40)
		{
			sPanel.Children.Add(Ui.Txt(I18n.L("…还有 {0} 行", lines.Count - 40), 11.5, bold: false, "B.InkMuted").M(10.0, 2.0, 0.0, 2.0));
		}
		Border border2 = new Border();
		border2.CornerRadius = new CornerRadius(8.0);
		border2.BorderThickness = new Thickness(1.0);
		border2.Padding = new Thickness(0.0, 4.0, 0.0, 4.0);
		border2.Child = sPanel;
		border2.ClipToBounds = true;
		border2.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		border2.SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		return border2;
	}

	public static bool AllowAlways(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("allow_always", out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		switch (ev.Name)
		{
		case "delete_instance":
		case "delete_mod":
		case "plan_approval":
			return false;
		default:
			return true;
		}
	}
}
