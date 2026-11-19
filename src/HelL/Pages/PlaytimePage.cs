using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class PlaytimePage : PageBase
{
	private readonly SPanel _list = Ui.V(10.0);

	private readonly TextBlock _total = new TextBlock
	{
		FontSize = 34.0,
		FontWeight = FontWeights.Bold
	};

	private readonly TextBlock _sub = Ui.Muted("");

	private readonly Border _milestoneBadge = new Border
	{
		CornerRadius = new CornerRadius(4.0),
		Padding = new Thickness(8.0, 3.0, 8.0, 3.0),
		VerticalAlignment = VerticalAlignment.Center,
		Margin = new Thickness(10.0, 0.0, 0.0, 0.0)
	};

	private readonly TextBlock _milestoneText = Ui.Txt("萌新起航 (Lv.1)", 12.0, bold: true);

	private readonly ComboBox _inst = Ui.Combo(new string[1] { AllInstances }, null, 190.0);

	private bool _sync;
	private double _lastTotalHours;
	private int _lastInstanceCount;
	private string _lastTopInstanceName = "无";
	private double _lastTopInstanceHours;

	private static string AllInstances => I18n.L("全部实例");

	public override string Title => I18n.L("游戏时长");

	public PlaytimePage()
	{
		_milestoneBadge.Child = _milestoneText;
		_milestoneBadge.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		_milestoneText.SetResourceReference(TextBlock.ForegroundProperty, "B.AccentDeep");

		Button button = Ui.Btn(I18n.L("清空统计"), BtnKind.Ghost, delegate
		{
			PageBase.Run(ClearAllAsync);
		}, "\uea99");
		Button button2 = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(LoadAsync);
		}, "\ue72c");
		Button shareBtn = Ui.Btn(I18n.L("分享成就"), BtnKind.Chip, delegate
		{
			string report = ModernUiVisuals.FormatShareablePlaytimeSummary(
				_lastTotalHours,
				_lastInstanceCount,
				_lastTopInstanceName,
				_lastTopInstanceHours
			);
			try
			{
				Clipboard.SetText(report);
				PageBase.Toast(I18n.L("已复制到剪贴板"), I18n.L("可粘贴到聊天群或动态分享游戏成就"), ToastKind.Success);
			}
			catch
			{
				PageBase.Toast(I18n.L("复制失败"), "", ToastKind.Warning);
			}
		}, "\ue72d");
		_inst.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(ReloadAsync);
			}
		};
		_total.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
		SPanel border = Ui.V(8.0, Ui.Muted(I18n.L("累计游玩")), Ui.H(0.0, _total, _milestoneBadge), _sub, Ui.Sep().M(0, 12, 0, 0));
		FrameworkElement border2 = Ui.Fields(Ui.H(10.0, Ui.Txt(I18n.L("范围"), 12.0, bold: false, "B.InkMuted").VCenter(), _inst.VCenter()));
		base.Content = PageBase.ScrollBody(Ui.Section(I18n.L("游戏时长"), I18n.L("按实例与版本统计，每次退出游戏后自动累加"), Ui.H(8.0, shareBtn, button2, button)), border, border2, _list);
	}

	protected override async Task LoadAsync()
	{
		(List<InstanceInfo> loaded, string err) = await PageBase.Api.TryCallAsyncOrError("get_instances", null, new List<InstanceInfo>());
		List<InstanceInfo> source = loaded ?? new List<InstanceInfo>();
		if (err != null)
		{
			PageBase.Toast(I18n.L("实例列表读取失败"), err, ToastKind.Warning);
		}
		_sync = true;
		_inst.Fill(new string[1] { AllInstances }.Concat<string>(source.Select((InstanceInfo i) => i.Name)));
		_sync = false;
		await ReloadAsync();
	}

	public override Task RefreshAsync()
	{
		return ReloadAsync();
	}

	private Task ReloadAsync()
	{
		string text = _inst.Str();
		if (text == null || text.Length <= 0 || !(text != AllInstances))
		{
			return ReloadAllAsync();
		}
		return ReloadOneAsync(text);
	}

	private async Task ReloadAllAsync()
	{
		await ShowTotalAsync(await PageBase.Api.TryCallAsync("get_total_playtime", null, 0L));
		(Dictionary<string, JsonElement> loaded, string err) = await PageBase.Api.TryCallAsyncOrError("get_all_playtime", null, new Dictionary<string, JsonElement>());
		Dictionary<string, JsonElement> dictionary = loaded ?? new Dictionary<string, JsonElement>();
		_sub.Text = I18n.L("{0} 个实例有记录", dictionary.Count);
		_list.Children.Clear();
		if (dictionary.Count == 0)
		{
			if (err != null)
			{
				_list.Children.Add(Ui.EmptyError(I18n.L("读取游玩记录失败"), err, delegate
				{
					PageBase.Run(ReloadAsync);
				}));
			}
			else
			{
				_list.Children.Add(Ui.Empty("\ue823", I18n.L("还没有游玩记录"), I18n.L("启动一次游戏后这里就有数据了")));
			}
			return;
		}
		List<KeyValuePair<string, JsonElement>> ordered = dictionary.OrderByDescending<KeyValuePair<string, JsonElement>, long>((KeyValuePair<string, JsonElement> k) => Secs(k.Value)).ToList();
		_lastInstanceCount = dictionary.Count;
		if (ordered.Count > 0)
		{
			_lastTopInstanceName = ordered[0].Key;
			_lastTopInstanceHours = Math.Round((double)Secs(ordered[0].Value) / 3600.0, 1);
		}
		long max = Math.Max(1L, ordered.Max((KeyValuePair<string, JsonElement> k) => Secs(k.Value)));
		foreach (KeyValuePair<string, JsonElement> item in ordered)
		{
			string name = item.Key;
			long num = Secs(item.Value);
			ProgressBar progressBar = Ui.Prog();
			progressBar.Height = 6.0;
			List<string> list = Versions(item.Value).OrderByDescending(delegate(KeyValuePair<string, long> v)
			{
				KeyValuePair<string, long> keyValuePair = v;
				return keyValuePair.Value;
			}).Take(6).Select(delegate(KeyValuePair<string, long> v)
			{
				KeyValuePair<string, long> keyValuePair = v;
				string key = keyValuePair.Key;
				keyValuePair = v;
				return key + " " + Cached(keyValuePair.Value);
			})
				.ToList();
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.V(6.0, Ui.Txt(name, 14.0, bold: true), progressBar, Ui.Small((list.Count == 0) ? "—" : string.Join(" · ", list)).Wrap()), 0);
			grid.Add(Ui.V(2.0, Ui.Txt(Cached(num), 13.0, bold: true, "B.AccentDeep").Right(), Ui.Btn(I18n.L("查看"), BtnKind.Chip, delegate
			{
				_sync = true;
				_inst.Fill(_inst.Items.Cast<string>(), name);
				_sync = false;
				PageBase.Run(() => ReloadOneAsync(name));
			}).Right(), Ui.Btn(I18n.L("清除"), BtnKind.Ghost, delegate
			{
				PageBase.Run(() => ClearOneAsync(name));
			}).Right()).M(14.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			Border rowCard = Ui.Card(grid, 14.0);
			rowCard.ToolTip = I18n.L("双击查看这个实例的版本明细");
			rowCard.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
			{
				if (e.ClickCount == 2 && !Ui.FromButton(e.OriginalSource as DependencyObject))
				{
					e.Handled = true;
					_sync = true;
					_inst.Fill(_inst.Items.Cast<string>(), name);
					_sync = false;
					PageBase.Run(() => ReloadOneAsync(name));
				}
			};
			_list.Children.Add(rowCard);
			Motion.Progress(progressBar, (double)num * 100.0 / (double)max);
		}
		Motion.Stagger(_list, 30.0);
	}

	private async Task ReloadOneAsync(string instance)
	{
		(JsonElement data, string err) = await PageBase.Api.TryCallAsyncOrError<JsonElement>("get_playtime", new { instance });
		if (err != null && data.ValueKind == JsonValueKind.Undefined)
		{
			_total.Text = "—";
			_sub.Text = I18n.L("实例「{0}」", instance);
			_list.Children.Clear();
			_list.Children.Add(Ui.EmptyError(I18n.L("读取游玩记录失败"), err, delegate
			{
				PageBase.Run(() => ReloadOneAsync(instance));
			}));
			return;
		}
		long total = Secs(data);
		await ShowTotalAsync(total);
		List<KeyValuePair<string, long>> versions = Versions(data).OrderByDescending(delegate(KeyValuePair<string, long> v)
		{
			KeyValuePair<string, long> keyValuePair = v;
			return keyValuePair.Value;
		}).ToList();
		List<string> sessions = Sessions(data);
		_sub.Text = I18n.L("实例「{0}」 · {1} 个版本 · {2} 次会话", instance, versions.Count, sessions.Count);
		_list.Children.Clear();
		if (total <= 0 && versions.Count == 0)
		{
			_list.Children.Add(Ui.Empty("\ue823", I18n.L("这个实例还没有记录"), I18n.L("启动一次游戏后这里就有数据了")));
			return;
		}
		long num = ((versions.Count == 0) ? 1 : Math.Max(1L, versions.Max((KeyValuePair<string, long> v) => v.Value)));
		foreach (KeyValuePair<string, long> item in versions)
		{
			item.Deconstruct(out var key, out var value);
			string vid = key;
			long num2 = value;
			ProgressBar progressBar = Ui.Prog();
			progressBar.Height = 6.0;
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.V(6.0, Ui.Txt(vid, 13.5, bold: true), progressBar), 0);
			grid.Add(Ui.V(2.0, Ui.Txt(Cached(num2), 13.0, bold: true, "B.AccentDeep").Right(), Ui.Btn(I18n.L("清除"), BtnKind.Ghost, delegate
			{
				PageBase.Run(() => ClearOneAsync(instance, vid));
			}).Right()).M(14.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			_list.Children.Add(Ui.Card(grid, 13.0));
			Motion.Progress(progressBar, (double)num2 * 100.0 / (double)num);
		}
		if (sessions.Count > 0)
		{
			SPanel sPanel = Ui.V(4.0, Ui.Txt(I18n.L("最近会话"), 13.0, bold: true));
			foreach (string item2 in sessions.Take(10))
			{
				sPanel.Children.Add(Ui.Small(item2).Wrap());
			}
			_list.Children.Add(Ui.Card(sPanel, 13.0));
		}
		Motion.Stagger(_list);
	}

	private async Task ClearAllAsync()
	{
		if (await Dlg.Confirm(I18n.L("清空游戏时长"), I18n.L("所有实例的时长记录都会归零。"), I18n.L("清空"), I18n.L("取消"), danger: true))
		{
			await PageBase.Api.CallAsync("clear_playtime", new
			{
				instance = "",
				version = ""
			});
			await LoadAsync();
		}
	}

	private async Task ClearOneAsync(string instance, string version = "")
	{
		string body = string.IsNullOrEmpty(version) ? I18n.L("「{0}」的时长记录会归零，没法恢复。", instance) : I18n.L("「{0} / {1}」的时长记录会归零，没法恢复。", instance, version);
		if (!await Dlg.Confirm(I18n.L("清除时长记录"), body, I18n.L("清除"), I18n.L("取消"), danger: true))
		{
			return;
		}
		await PageBase.Api.CallAsync("clear_playtime", new { instance, version });
		await ReloadAsync();
	}

	private async Task ShowTotalAsync(long seconds)
	{
		var formatted = PlaytimeFormatter.Format(seconds);
		double target = formatted.ValueHours;
		Motion.CountUp(_total, _lastTotalHours, target, "0.0");
		_lastTotalHours = target;
		_sub.Text = I18n.L("累计：{0}", formatted.Subtext);

		var milestone = ModernUiVisuals.GetPlaytimeMilestone(target);
		_milestoneText.Text = $"{milestone.Title} (Lv.{milestone.Level})";
		_milestoneBadge.ToolTip = milestone.Description;

		await Task.CompletedTask;
	}

	private static string Cached(long secs)
	{
		return Fmt.Duration(secs);
	}

	private static long Secs(JsonElement el)
	{
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("total", out var value) || !value.TryGetInt64(out var value2))
		{
			return 0L;
		}
		return value2;
	}

	private static List<KeyValuePair<string, long>> Versions(JsonElement el)
	{
		List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>();
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("versions", out var value) || value.ValueKind != JsonValueKind.Object)
		{
			return list;
		}
		foreach (JsonProperty item in value.EnumerateObject())
		{
			if (item.Value.TryGetInt64(out var value2) && value2 > 0)
			{
				list.Add(new KeyValuePair<string, long>(item.Name, value2));
			}
		}
		return list;
	}

	private static List<string> Sessions(JsonElement el)
	{
		List<string> list = new List<string>();
		if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("sessions", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray().Reverse())
		{
			if (item.ValueKind == JsonValueKind.Object)
			{
				JsonElement value2;
				string text = (item.TryGetProperty("version", out value2) ? value2.ToString() : "");
				JsonElement value3;
				string text2 = (item.TryGetProperty("start", out value3) ? value3.ToString() : "");
				JsonElement value4;
				long value5;
				long secs = ((item.TryGetProperty("seconds", out value4) && value4.TryGetInt64(out value5)) ? value5 : 0);
				list.Add(string.Join(" · ", new string[3]
				{
					text,
					text2,
					Cached(secs)
				}.Where((string x) => !string.IsNullOrWhiteSpace(x))));
			}
		}
		return list;
	}
}
