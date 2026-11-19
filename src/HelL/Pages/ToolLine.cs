using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL.Pages;

internal sealed class ToolLine : Border
{
	private readonly TextBlock _label;

	private readonly TextBlock _arg;

	private readonly TextBlock _result;

	private readonly Border _mcp;

	private readonly ProgressBar _bar;

	private readonly ContentControl _icon = new ContentControl
	{
		Width = 26.0,
		Height = 26.0,
		VerticalAlignment = VerticalAlignment.Top
	};

	private readonly string? _typeIcon;

	private readonly string? _tool;

	private ToolState _state = (ToolState)(-1);

	internal string LabelText => _label.Text;

	internal string ArgText => _arg.Text;

	internal ToolState State => _state;

	public ToolLine(string text, string? tool = null)
	{
		_tool = tool;
		_typeIcon = Lucide.ToolIcon(tool);
		_label = Ui.Txt("", 12.5, bold: false, "B.Ink");
		_label.TextTrimming = TextTrimming.CharacterEllipsis;
		_arg = Ui.Txt("", 11.5, bold: false, "B.InkMuted");
		_arg.FontFamily = MdView.Mono;
		_arg.TextTrimming = TextTrimming.CharacterEllipsis;
		_arg.Margin = new Thickness(8.0, 0.0, 8.0, 0.0);
		_result = Ui.Txt("", 11.5, bold: false, "B.InkFaint");
		_mcp = Ui.Tag(I18n.L("MCP · 外部"), "B.Info", "B.InfoSoft");
		_mcp.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		_mcp.Visibility = ((!AiUiText.IsMcp(tool)) ? Visibility.Collapsed : Visibility.Visible);
		_bar = Ui.Prog();
		_bar.Height = 3.0;
		_bar.Visibility = Visibility.Collapsed;
		_bar.Margin = new Thickness(35.0, 2.0, 0.0, 0.0);
		Grid grid = Ui.G(null, "Auto,Auto,Auto,*,Auto");
		grid.Add(_icon, 0);
		grid.Add(_label.M(9.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid.Add(_mcp.VCenter(), 0, 2);
		grid.Add(_arg.VCenter(), 0, 3);
		grid.Add(_result.VCenter(), 0, 4);
		base.Padding = new Thickness(6.0, 4.0, 6.0, 4.0);
		base.CornerRadius = new CornerRadius(7.0);
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		base.Background = Brushes.Transparent;
		Child = Ui.V(0.0, grid, _bar);
		SetText(text);
		SetState(ToolState.Prepare);
	}

	public void SetText(string text)
	{
		(string, string) tuple = AiUiText.SplitLabel(text);
		_label.Text = tuple.Item1;
		_arg.Text = tuple.Item2;
		base.ToolTip = AiUiText.StripState(text);
	}

	public void SetResult(string text)
	{
		_result.Text = text ?? "";
	}

	public void SetState(ToolState state)
	{
		if (_state != state)
		{
			_state = state;
			SyncIcon();
		}
	}

	private void SyncIcon()
	{
		bool flag = _state == ToolState.Prepare || _state == ToolState.Running;
		string brushKey = _state switch
		{
			ToolState.Failed => "B.Danger", 
			ToolState.Skipped => "B.InkMuted", 
			_ => "B.AccentText", 
		};
		Lucide.Shimmer(_label, flag, (_state == ToolState.Failed) ? "B.Danger" : ((_state == ToolState.Skipped) ? "B.InkMuted" : "B.Ink"));
		FrameworkElement icon = ((!string.IsNullOrEmpty(_tool) && LucideAssets.Icons.ContainsKey(_tool)) ? Lucide.Asset(_tool, 14.0, brushKey) : ((_typeIcon != null) ? Lucide.Icon(_typeIcon, 14.0, brushKey) : ((!AiUiText.IsMcp(_tool)) ? (flag ? Lucide.Spinner(14.0, brushKey) : Lucide.Icon("M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1", 14.0, brushKey)) : Lucide.Icon("M12 22v-5 M9 8V2 M15 8V2 M18 8v5a6 6 0 0 1-12 0V8z", 14.0, brushKey))));
		_icon.Content = AiIcons.Tile(icon, _state);
		if (_state == ToolState.Skipped && _result.Text.Length == 0)
		{
			_result.Text = I18n.L("已跳过");
		}
	}

	public void BindTask()
	{
		_bar.Visibility = Visibility.Visible;
		_bar.IsIndeterminate = true;
		_result.Text = I18n.L("后台进行中");
	}

	public void SetProgress(int current, int total, string? message)
	{
		_bar.Visibility = Visibility.Visible;
		_bar.IsIndeterminate = total <= 0;
		if (total > 0)
		{
			double num = Clamp.Of((double)current * 100.0 / (double)total, 0.0, 100.0);
			Motion.Progress(_bar, num);
			_result.Text = $"{num:0}%";
		}
		Fmt.SplitMsg(message, out string status, out string _);
		if (!string.IsNullOrEmpty(status))
		{
			_arg.Text = status;
		}
	}

	public void Finish(bool success, string? message)
	{
		_bar.IsIndeterminate = false;
		if (success)
		{
			Motion.Progress(_bar, 100.0);
			_bar.Visibility = Visibility.Collapsed;
		}
		_result.Text = (success ? I18n.L("完成") : I18n.L("失败"));
		if (!string.IsNullOrWhiteSpace(message))
		{
			_arg.Text = message;
			base.ToolTip = message;
		}
		SetState(success ? ToolState.Done : ToolState.Failed);
		_result.SetResourceReference(TextBlock.ForegroundProperty, success ? "B.InkFaint" : "B.Danger");
	}
}
