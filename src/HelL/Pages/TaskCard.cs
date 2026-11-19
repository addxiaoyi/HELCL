using System;
using System.Windows;
using System.Windows.Controls;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL.Pages;

/// <summary>AI 触发的下载/安装后台任务卡片：标题、进度条、百分比/速度，完成或失败后给出后续按钮。</summary>
internal sealed class TaskCard : Border
{
	private readonly TextBlock _title;

	private readonly TextBlock _status;

	private readonly TextBlock _percent;

	private readonly TextBlock _speed;

	private readonly ProgressBar _bar;

	private readonly SPanel _buttons = Ui.H(6.0);

	private readonly ContentControl _icon = new ContentControl
	{
		Width = 26.0,
		Height = 26.0,
		VerticalAlignment = VerticalAlignment.Top
	};

	/// <summary>完成后「启动游戏」；为 null 时不显示。</summary>
	public Action? Launch { get; set; }

	/// <summary>完成后「打开文件夹」；为 null 时不显示。</summary>
	public Action? OpenFolder { get; set; }

	/// <summary>失败后「重试」（失败原因作参数）；为 null 时不显示。</summary>
	public Action<string>? Retry { get; set; }

	public string TaskId { get; }

	public string Tool { get; }

	public string Instance { get; }

	public string FinishMessage { get; private set; } = "";

	public bool? Succeeded { get; private set; }

	internal string TitleText => _title.Text;

	public TaskCard(string taskId, string title, string tool, string instance)
	{
		TaskId = taskId ?? "";
		Tool = tool ?? "";
		Instance = instance ?? "";
		_title = Ui.Txt(title ?? "", 13.0, bold: true, "B.Ink");
		_title.TextTrimming = TextTrimming.CharacterEllipsis;
		_status = Ui.Txt(I18n.L("后台进行中"), 11.5, bold: false, "B.InkMuted");
		_status.TextTrimming = TextTrimming.CharacterEllipsis;
		_percent = Ui.Txt("", 11.5, bold: true, "B.AccentText");
		_speed = Ui.Txt("", 11.5, bold: false, "B.InkFaint");
		_bar = Ui.Prog();
		_bar.Height = 4.0;
		_bar.IsIndeterminate = true;
		_buttons.Visibility = Visibility.Collapsed;
		_buttons.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
		Grid head = Ui.G(null, "*,Auto,Auto");
		head.Add(_title.VCenter(), 0);
		head.Add(_speed.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		head.Add(_percent.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		Grid grid = Ui.G(null, "Auto,*");
		grid.Add(_icon, 0);
		grid.Add(Ui.V(5.0, head, _bar, _status, _buttons).M(10.0, 0.0, 0.0, 0.0), 0, 1);
		base.CornerRadius = new CornerRadius(10.0);
		base.BorderThickness = new Thickness(1.0);
		base.Padding = new Thickness(12.0, 10.0, 12.0, 10.0);
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		SetResourceReference(Border.BackgroundProperty, "B.Paper2");
		SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
		Child = grid;
		SetIcon(ToolState.Running);
	}

	public void SetTitle(string title)
	{
		_title.Text = title ?? "";
		_title.ToolTip = title;
	}

	private void SetIcon(ToolState state)
	{
		string brush = state switch
		{
			ToolState.Failed => "B.Danger",
			_ => "B.AccentText",
		};
		FrameworkElement icon = (state == ToolState.Running) ? Lucide.Spinner(14.0, brush) : Lucide.Icon((state == ToolState.Failed) ? "M18 6L6 18 M6 6l12 12" : "M20 6L9 17l-5-5", 14.0, brush);
		_icon.Content = AiIcons.Tile(icon, state);
	}

	public void SetProgress(int current, int total, string? message)
	{
		if (Succeeded.HasValue)
		{
			return;
		}
		_bar.IsIndeterminate = total <= 0;
		if (total > 0)
		{
			double num = Clamp.Of((double)current * 100.0 / (double)total, 0.0, 100.0);
			Motion.Progress(_bar, num);
			_percent.Text = $"{num:0}%";
		}
		Fmt.SplitMsg(message, out string status, out string speed);
		if (!string.IsNullOrEmpty(status))
		{
			_status.Text = status;
			_status.ToolTip = status;
		}
		_speed.Text = speed;
	}

	public void Finish(bool success, string? message)
	{
		Succeeded = success;
		FinishMessage = message ?? "";
		_bar.IsIndeterminate = false;
		_speed.Text = "";
		if (success)
		{
			Motion.Progress(_bar, 100.0);
			_percent.Text = "100%";
			_bar.Visibility = Visibility.Collapsed;
			_status.Text = string.IsNullOrWhiteSpace(message) ? I18n.L("完成") : message;
			_status.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		}
		else
		{
			_percent.Text = I18n.L("失败");
			_percent.SetResourceReference(TextBlock.ForegroundProperty, "B.Danger");
			_status.Text = I18n.L("原因：") + (string.IsNullOrWhiteSpace(message) ? I18n.L("未知错误") : message);
			_status.TextWrapping = TextWrapping.Wrap;
			_status.SetResourceReference(TextBlock.ForegroundProperty, "B.Danger");
			SetResourceReference(Border.BorderBrushProperty, "B.Danger");
		}
		_status.ToolTip = message;
		SetIcon(success ? ToolState.Done : ToolState.Failed);
		_buttons.Children.Clear();
		if (success)
		{
			Action launch = Launch;
			if (launch != null)
			{
				_buttons.Children.Add(Ui.Btn(I18n.L("启动游戏"), BtnKind.Primary, delegate
				{
					launch();
				}, "\ue768"));
			}
			Action open = OpenFolder;
			if (open != null)
			{
				_buttons.Children.Add(Ui.Btn(I18n.L("打开文件夹"), BtnKind.Chip, delegate
				{
					open();
				}, "\ue838"));
			}
		}
		else
		{
			Action<string> retry = Retry;
			if (retry != null && FinishMessage != "已取消")
			{
				Button btn = null;
				btn = Ui.Btn(I18n.L("重试"), BtnKind.Chip, delegate
				{
					btn.IsEnabled = false;
					retry(FinishMessage);
				}, "\ue72c");
				btn.ToolTip = I18n.L("让助手重新执行这一步");
				_buttons.Children.Add(btn);
			}
		}
		_buttons.Visibility = (_buttons.Children.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
	}
}
