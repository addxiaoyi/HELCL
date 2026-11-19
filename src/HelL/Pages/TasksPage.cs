using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class TasksPage : PageBase
{
	private sealed class Card
	{
		public bool Expanded;

		public bool Cancelling;

		public DateTime LogPainted;

		public required Border Root { get; init; }

		public required TextBlock Title { get; init; }

		public required TextBlock Status { get; init; }

		public required TextBlock Speed { get; init; }

		public required ProgressBar Bar { get; init; }

		public required Button Cancel { get; init; }

		public required Button Toggle { get; init; }

		public required Button Crash { get; init; }
		public required Button Retry { get; init; }
		public bool Retrying;
		public double Progress = double.NaN;

		public required Border LogWrap { get; init; }

		public required TextBox Log { get; init; }
	}

	private readonly SPanel _list = Ui.V(10.0);

	private readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();

	private readonly TextBlock _summary = Ui.Muted("");

	private readonly Button _clear;
	private readonly Button _cancelAll;
	private bool _cancellingAll;
	private bool _refreshQueued;
	private bool _resolvingTitles;

	private static readonly TimeSpan LogInterval = TimeSpan.FromMilliseconds(100.0);

	public override string Title => I18n.L("下载任务");

	public TasksPage()
	{
		_clear = Ui.Btn(I18n.L("清除已完成"), BtnKind.Chip, delegate
		{
			TaskStore.ClearFinished();
		}, "\uea99");
		_cancelAll = Ui.Btn(I18n.L("全部取消"), BtnKind.Ghost, delegate
		{
			PageBase.Run(CancelAllAsync, I18n.L("部分任务未能取消，请重试"));
		}, "\ue711");
		_cancelAll.IsEnabled = false;
		Grid grid = Ui.Section(I18n.L("下载任务"), I18n.L("安装、下载、启动都在这里排队"), Ui.H(8.0, _clear, _cancelAll));
		base.Content = PageBase.ScrollBody(grid, BuildFilters(), _summary, _list);
		EnableDrop(() => FileDrop.DropContext.None);
		TaskStore.Added += _ => QueueRefresh();
		TaskStore.Updated += _ => QueueRefresh();
		TaskStore.Cleared += QueueRefresh;
	}

	private void QueueRefresh()
	{
		if (_refreshQueued) return;
		_refreshQueued = true;
		Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
		{
			_refreshQueued = false;
			Rebuild();
		}));
	}

	private async Task CancelAllAsync()
	{
		if (_cancellingAll || TaskStore.RunningCount == 0) return;
		_cancellingAll = true;
		_cancelAll.IsEnabled = false;
		try
		{
			var failures = new List<string>();
			foreach (var task in TaskStore.Rows.Where(row => !row.Finished).ToList())
			{
				try { await PageBase.Api.CallAsync<object>("cancel_task", new { task_id = task.Id }); }
				catch (Exception ex) { failures.Add(task.Title + ": " + ex.Message); }
			}
			if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
		}
		finally
		{
			_cancellingAll = false;
			_cancelAll.IsEnabled = TaskStore.RunningCount > 0;
		}
	}

	protected override async Task LoadAsync()
	{
		Rebuild();
		await ResolveTitlesAsync();
	}

	public override void OnShown()
	{
		Rebuild();
		PageBase.Run(ResolveTitlesAsync);
	}

	private void Rebuild()
	{
		List<TaskRow> list = TaskStore.Rows.Reverse().ToList();
		var activeIds = new HashSet<string>(list.Select(row => row.Id));
		foreach (string id in _cards.Keys.Where(id => !activeIds.Contains(id)).ToList())
		{
			_list.Children.Remove(_cards[id].Root);
			_cards.Remove(id);
		}
		bool resolveTitles = false;
		for (int index = 0; index < list.Count; index++)
		{
			TaskRow item = list[index];
			if (!_cards.TryGetValue(item.Id, out Card card))
			{
				card = Build(item);
				_cards[item.Id] = card;
				_list.Children.Insert(index, card.Root);
				resolveTitles |= item.Title == TaskStore.PlaceholderTitle;
			}
			Sync(item);
		}
		ApplyFilters();
		if (resolveTitles) PageBase.Run(ResolveTitlesAsync);
	}

	private async Task ResolveTitlesAsync()
	{
		if (_resolvingTitles) return;
		_resolvingTitles = true;
		try
		{
			foreach (TaskRow row in TaskStore.Rows.Where((TaskRow r) => r.Title == TaskStore.PlaceholderTitle).ToList())
			{
				string text = (await PageBase.Api.TryCallAsync("task_title", new { task_id = row.Id }, "")) ?? "";
				if (text.Length != 0 && text != row.Id && text != row.Title)
				{
					row.Title = text;
					if (_cards.TryGetValue(row.Id, out Card value))
					{
						value.Title.Text = text;
						value.Title.ToolTip = text;
					}
				}
			}
			ApplyFilters();
		}
		finally { _resolvingTitles = false; }
	}

	private Card Build(TaskRow row)
	{
		TextBlock textBlock = Ui.Txt(row.Title, 13.5, bold: true).Trim();
		textBlock.ToolTip = row.Title;
		TextBlock textBlock2 = Ui.Muted("").Wrap();
		TextBlock textBlock3 = Ui.Small("");
		ProgressBar progressBar = Ui.Prog();
		Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost);
		Button retry = Ui.Btn(I18n.L("重试下载"), BtnKind.Chip); retry.Visibility = Visibility.Collapsed;
		Button button = Ui.Btn(I18n.L("日志"), BtnKind.Chip, null, "\ue8fd");
		Button button2 = Ui.Btn(I18n.L("崩溃分析"), BtnKind.Chip, null, "\uebe8");
		button2.Visibility = Visibility.Collapsed;
		TextBox textBox = Ui.LogBox();
		System.Windows.Automation.AutomationProperties.SetName(textBox, I18n.L("任务日志"));
		textBox.Height = 160.0;
		textBox.BorderThickness = new Thickness(0.0);
		textBox.Background = Brushes.Transparent;
		Border logWrap = Ui.Pane(textBox, "B.Paper2", 9.0, 10.0);
		logWrap.Visibility = Visibility.Collapsed;
		var actions = Ui.H(6.0, retry, button2, button, cancel); actions.Wrap = true;
		var grid = new PageHeader(Ui.V(3.0, textBlock, textBlock2).VCenter(), actions) { MinimumHeadingWidth = 240 };
		Grid grid2 = Ui.G(null, "*,Auto");
		grid2.Add(progressBar.VCenter(), 0);
		grid2.Add(textBlock3.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Border border = Ui.Card(Ui.V(9.0, grid, grid2, logWrap), 14.0);
		Card card = new Card
		{
			Root = border,
			Title = textBlock,
			Status = textBlock2,
			Speed = textBlock3,
			Bar = progressBar,
			Cancel = cancel,
			Toggle = button,
			Crash = button2,
			Retry = retry,
			LogWrap = logWrap,
			Log = textBox
		};
		retry.Click += (_, _) => Run(async () =>
		{
			var request = DownloadRetries.Find(row.Id);
			if (request == null || !row.Finished || row.Success || card.Retrying) return;
			card.Retrying = true; retry.IsEnabled = false;
			try
			{
				using var arguments = System.Text.Json.JsonDocument.Parse(request.ArgumentsJson);
				await Api.StartTaskAsync(request.Method, arguments.RootElement);
				DownloadRetries.Remove(row.Id);
			}
			finally { card.Retrying = false; Sync(row); }
		});
		cancel.Click += delegate
		{
			PageBase.Run(async delegate
			{
				if (row.Finished)
				{
					TaskStore.Remove(row.Id);
					Rebuild();
				}
				else
				{
					if (card.Cancelling) return;
					card.Cancelling = true;
					cancel.IsEnabled = false;
					try { await PageBase.Api.CallAsync<object>("cancel_task", new { task_id = row.Id }); }
					finally
					{
						card.Cancelling = false;
						Sync(row);
					}
				}
			});
		};
		button2.Click += delegate
		{
			PageBase.Run(async delegate
			{
				CrashReport crashReport = await PageBase.Api.TryCallAsync<CrashReport>("get_crash", new
				{
					task_id = row.Id
				});
				if (crashReport == null || string.IsNullOrWhiteSpace(crashReport.Title + crashReport.Headline + crashReport.Summary + crashReport.Detail))
				{
					PageBase.Toast(I18n.L("没有崩溃分析"), I18n.L("这个任务没有留下崩溃报告"));
				}
				else
				{
					if (string.IsNullOrEmpty(crashReport.TaskId))
					{
						crashReport.TaskId = row.Id;
					}
					await CrashUi.ShowAsync(crashReport, gameError: true);
				}
			});
		};
		button.Click += delegate
		{
			card.Expanded = !card.Expanded;
			button.Content = I18n.L(card.Expanded ? "收起日志" : "日志");
			System.Windows.Automation.AutomationProperties.SetName(button, I18n.L(card.Expanded ? "收起日志" : "日志"));
			if (card.Expanded)
			{
				card.Log.Text = row.Log.ToString();
				card.Log.ScrollToEnd();
				logWrap.Visibility = Visibility.Visible;
				Motion.FadeIn(logWrap, 200.0, 8.0);
			}
			else
			{
				logWrap.Visibility = Visibility.Collapsed;
			}
		};
		return card;
	}

	private void Sync(TaskRow row)
	{
		if (!_cards.TryGetValue(row.Id, out Card value))
		{
			Rebuild();
			return;
		}
		value.Title.Text = row.Title;
		value.Title.ToolTip = row.Title;
		value.Status.Text = DisplayLabels.Of(row.Status);
		value.Speed.Text = row.Speed;
		value.Bar.IsIndeterminate = row.Indeterminate && !row.Finished;
		if (!value.Bar.IsIndeterminate && value.Progress != row.Progress)
		{
			value.Progress = row.Progress;
			Motion.Progress(value.Bar, row.Progress);
		}
		value.Cancel.IsEnabled = !value.Cancelling;
		value.Cancel.Content = (row.Finished ? I18n.L("清除") : I18n.L("取消"));
		System.Windows.Automation.AutomationProperties.SetName(value.Cancel, row.Finished ? I18n.L("清除") : I18n.L("取消"));
		value.Crash.Show(row.Finished && !row.Success && row.HasCrash);
		value.Retry.Show(row.Finished && !row.Success && DownloadRetries.Find(row.Id) != null);
		value.Retry.IsEnabled = !value.Retrying;
		value.Bar.SetResourceReference(Control.ForegroundProperty, row.Cancelled ? "B.InkFaint" : (row.Finished && !row.Success) ? "B.Danger" : "B.Accent");
		if (value.Expanded && (row.Finished || DateTime.UtcNow - value.LogPainted >= LogInterval))
		{
			value.LogPainted = DateTime.UtcNow;
			bool num = value.Log.VerticalOffset >= value.Log.ExtentHeight - value.Log.ViewportHeight - 20.0;
			string newLog = row.Log.ToString();
			if (value.Log.Text != newLog)
			{
				double offset = value.Log.VerticalOffset;
				int selectionStart = value.Log.SelectionStart;
				int selectionLength = value.Log.SelectionLength;
				value.Log.Text = newLog;
				value.Log.Select(Math.Min(selectionStart, newLog.Length), Math.Min(selectionLength, Math.Max(0, newLog.Length - selectionStart)));
				if (num)
				{
					value.Log.ScrollToEnd();
				}
				else value.Log.ScrollToVerticalOffset(offset);
			}
		}
	}
}
