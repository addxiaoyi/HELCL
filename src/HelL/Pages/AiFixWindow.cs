using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

internal sealed class AiFixWindow : Window
{
	private readonly CrashReport _report;

	private readonly SPanel _messages = Ui.V(12.0);

	private readonly SmoothScroll _scroll;

	private readonly ChatInput _input = new ChatInput();

	private readonly Button _send;

	private readonly TextBlock _status = Ui.Small("");

	private readonly StringBuilder _stream = new StringBuilder();

	private readonly StringBuilder _round = new StringBuilder();

	private readonly Dictionary<string, ToolLine> _toolLines = new Dictionary<string, ToolLine>();

	private readonly Dictionary<string, ToolLine> _taskLines = new Dictionary<string, ToolLine>();

	private readonly Dictionary<string, string> _aiPendingTasks = new Dictionary<string, string>();

	private Bubble? _roundBubble;

	private PlanCard? _planCard;

	private bool _busy;

	private bool _kicked;

	private readonly string _chatId;

	private readonly BridgeClient? _client;

	public AiFixWindow(CrashReport report)
	{
		_report = report;
		SetResourceReference(Control.ForegroundProperty, "B.Ink");
		_chatId = $"fix-{DateTime.Now:yyyyMMddHHmmss}";
		base.Title = I18n.L("AI 修复");
		base.Width = 520.0;
		base.Height = 660.0;
		base.MinWidth = 430.0;
		base.MinHeight = 480.0;
		base.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		base.WindowStyle = WindowStyle.None;
		base.ResizeMode = ResizeMode.CanResize;
		base.UseLayoutRounding = true;
		SetResourceReference(Control.BackgroundProperty, "B.Canvas");
		SetResourceReference(Control.BorderBrushProperty, "B.Line");
		base.BorderThickness = new Thickness(1.0);
		WindowChrome chrome = new WindowChrome
		{
			CaptionHeight = 40.0,
			ResizeBorderThickness = new Thickness(6.0),
			GlassFrameThickness = new Thickness(0.0),
			CornerRadius = new CornerRadius(0.0),
			UseAeroCaptionButtons = false
		};
		WindowChrome.SetWindowChrome(this, chrome);
		Button button = new Button
		{
			Style = Ui.S("Btn.CaptionClose"),
			Content = "\ue8bb"
		};
		button.Click += delegate
		{
			Close();
		};
		WindowChrome.SetIsHitTestVisibleInChrome(button, hitTestVisible: true);
		Grid child = Ui.G(null, "*,Auto").Add(Ui.Txt(base.Title, 13.5, bold: true, "B.Ink").M(14.0, 0.0, 0.0, 0.0).VCenter(), 0).Add(button, 0, 1);
		Border border = new Border
		{
			Height = 40.0,
			Child = child
		};
		border.SetResourceReference(Control.BackgroundProperty, "B.Chrome");
		border.SetResourceReference(Control.BorderBrushProperty, "B.LineSoft");
		border.BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0);
		_send = Ui.Btn(I18n.L("发送"), BtnKind.Primary, OnMainButtonClick, "\ue724");
		_input.Tag = I18n.L("补充崩溃前做了什么、装了什么…  Enter 发送，Shift+Enter 换行");
		_input.Submitted += delegate(string text)
		{
			PageBase.Run(() => SendTextAsync(text));
		};
		_input.TextChanged += delegate
		{
			SyncMainButton();
		};
		_scroll = Ui.Scroll(_messages.M(4.0, 4.0, 10.0, 4.0));
		Border el = Ui.Card(Ui.V(6.0, _status, Ui.H(8.0, _input, _send)), 12.0);
		Grid content = Ui.G("Auto,*,Auto").Add(border, 0).Add(Ui.Card(_scroll, 10.0).M(12.0, 10.0, 12.0, 4.0), 1)
			.Add(el.M(12.0, 0.0, 12.0, 12.0), 2);
		base.Content = content;
		SyncMainButton();
		base.Loaded += delegate
		{
			PageBase.Run(KickOffAsync);
		};
		_client = AppServices.Client;
		_client.EventReceived += OnBridgeEvent;
	}

	private Task KickOffAsync()
	{
		if (_kicked)
		{
			return Task.CompletedTask;
		}
		_kicked = true;

		var localDiagnosis = CrashLogExpert.Analyze(_report.Detail, _report.ExitCode);
		if (localDiagnosis.IsIdentified)
		{
			string culprit = string.IsNullOrEmpty(localDiagnosis.SuspectedMod) ? "" : $"\n{I18n.L("引发崩溃模组：")}{localDiagnosis.SuspectedMod}";
			Add(new Bubble("assistant", $"{I18n.L("【离线专家快速诊断】")}\n{localDiagnosis.Reason}\n{I18n.L("建议：")}{localDiagnosis.Recommendation}{culprit}", RetryFrom));
		}

		return SendTextAsync(I18n.L("帮我看看游戏为什么闪退"));
	}

	private async Task<string> InterfaceLabelAsync()
	{
		Dictionary<string, JsonElement> dictionary = (await AppServices.Client.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		JsonElement value;
		string text = ((dictionary.TryGetValue("ai_mode", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "public") : "public");
		JsonElement value3;
		string value2 = ((dictionary.TryGetValue("ai_base_url", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
		JsonElement value4;
		bool flag = dictionary.TryGetValue("ai_api_key_set", out value4) && value4.ValueKind == JsonValueKind.True;
		if (text != "custom" || (string.IsNullOrWhiteSpace(value2) && !flag))
		{
			return I18n.L("公益接口（免费网关）");
		}
		JsonElement value5;
		string text2 = ((dictionary.TryGetValue("ai_model", out value5) && value5.ValueKind == JsonValueKind.String) ? (value5.GetString() ?? "") : "");
		return string.Format(I18n.L("自定义接口（{0}）"), (text2.Length > 0) ? text2 : I18n.L("未命名模型"));
	}

	internal static string BuildCrashContext(CrashReport r, string iface)
	{
		return CrashPromptFormatter.BuildCrashPrompt(
			r.Instance, r.Version, r.ExitCode, r.ExitHint,
			r.Headline, r.Summary, r.Detail, r.DirectFile, iface);
	}

	private void OnMainButtonClick(object sender, RoutedEventArgs e)
	{
		string text = _input.Text?.Trim() ?? "";
		if (text.Length > 0)
		{
			PageBase.Run(() => SendTextAsync(text));
		}
		else if (_busy)
		{
			PageBase.Run(StopAsync);
		}
	}

	private void SyncMainButton()
	{
		bool flag = !string.IsNullOrWhiteSpace(_input.Text);
		bool flag2 = _busy && !flag;
		_send.Content = Ui.H(6.0, Ui.Glyph(flag2 ? "\ue71a" : "\ue724", 13.0), Ui.Txt(flag2 ? I18n.L("停止") : I18n.L("发送")));
		_send.IsEnabled = flag || _busy;
	}

	private void RetryFrom(string text)
	{
		PageBase.Run(() => SendTextAsync(text));
	}

	private async Task SendTextAsync(string raw, bool echo = true)
	{
		string text = (raw ?? "").Trim();
		if (text.Length == 0)
		{
			return;
		}
		if (_busy)
		{
			JsonElement jsonElement = await AppServices.Client.TryCallAsync<JsonElement>("ai_steer", new { text });
			if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("ok", out var value) && value.ValueKind == JsonValueKind.True)
			{
				if (echo)
				{
					_input.Clear();
					Add(new Bubble("user", text, RetryFrom));
					_status.Text = I18n.L("已插队") + " · " + I18n.L("这句话会立刻交给正在运行的助手");
					SyncMainButton();
				}
				return;
			}
		}
		if (echo)
		{
			_input.Clear();
		}
		SetBusy(on: true);
		_status.Text = I18n.L("思考中…");
		if (echo)
		{
			Add(new Bubble("user", text, RetryFrom));
		}
		_stream.Clear();
		_roundBubble = OpenRound();
		Dictionary<string, object?> launch = new Dictionary<string, object>
		{
			["instance"] = _report.Instance ?? "",
			["username"] = AppServices.Window?.Prefs.LastUser ?? ""
		};
		CrashReport report = _report;
		CrashReport r = report;
		string context = BuildCrashContext(r, await InterfaceLabelAsync());
		OpResult opResult = await AppServices.Client.TryCallAsync<OpResult>("ai_send", new
		{
			text = text,
			chat_id = _chatId,
			launch = launch,
			context = context
		});
		if (opResult == null || !opResult.Ok)
		{
			string text2 = opResult?.Message;
			string text3 = ((text2 != null && text2.Length > 0) ? text2 : I18n.L("后端没有响应，请稍后再试"));
			if (_roundBubble != null)
			{
				_messages.Children.Remove(_roundBubble);
			}
			Add(new Bubble("error", text3, RetryFrom));
			ResetRunState();
			_status.Text = text3;
		}
	}

	private async Task StopAsync()
	{
		JsonElement jsonElement = await AppServices.Client.TryCallAsync<JsonElement>("ai_stop");
		if (jsonElement.ValueKind != JsonValueKind.Object || !jsonElement.TryGetProperty("busy", out var value) || value.ValueKind != JsonValueKind.True)
		{
			ResetRunState();
		}
		_status.Text = I18n.L("已停止");
	}

	private void SetBusy(bool on)
	{
		_busy = on;
		SyncMainButton();
	}

	private void ResetRunState()
	{
		_roundBubble = null;
		_planCard = null;
		_round.Clear();
		_stream.Clear();
		_status.Text = "";
		SetBusy(on: false);
	}

	private Bubble OpenRound(string? thinking = null)
	{
		_round.Clear();
		Bubble bubble = new Bubble("assistant", "…", RetryFrom);
		bubble.SetThinking(thinking ?? I18n.L("正在想…"));
		Add(bubble);
		return bubble;
	}

	private void CloseRound()
	{
		if (_roundBubble != null)
		{
			_roundBubble.SetText(_round.ToString());
			_roundBubble = null;
			_round.Clear();
		}
	}

	private void OnBridgeEvent(object? sender, BridgeEvent ev)
	{
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnBridgeEvent(sender, ev);
			});
			return;
		}
		string text = ev.Event;
		if (text == null)
		{
			return;
		}
		switch (text.Length)
		{
		case 8:
			switch (text[0])
			{
			case 'a':
				if (text == "ai.delta" && Mine(ev))
				{
					_stream.Append(ev.Text);
					_round.Append(ev.Text);
					if (_roundBubble == null)
					{
						_roundBubble = OpenRound();
					}
					_roundBubble.SetText(_round.ToString(), live: true);
					ScrollDown();
				}
				break;
			case 'p':
			{
				if (text == "progress" && _taskLines.TryGetValue(ev.TaskId, out ToolLine value2))
				{
					value2.SetProgress(ev.Current, ev.Total, ev.Message);
				}
				break;
			}
			case 'f':
			{
				if (!(text == "finished"))
				{
					break;
				}
				if (_taskLines.TryGetValue(ev.TaskId, out ToolLine value))
				{
					value.Finish(ev.Success, ev.Message);
				}
				if (_aiPendingTasks.TryGetValue(ev.TaskId, out string taskName))
				{
					_aiPendingTasks.Remove(ev.TaskId);
					if (!_busy)
					{
						_status.Text = I18n.L("后台任务完成") + " · " + I18n.L("助手回来汇报结果");
					}
					PageBase.Run(() => SendTextAsync(AiPage.PendingTaskReport(taskName, ev), echo: false));
				}
				break;
			}
			}
			break;
		case 7:
			switch (text[3])
			{
			case 'd':
			{
				if (!(text == "ai.done") || !Mine(ev))
				{
					break;
				}
				string text2 = StopNote(ev);
				if (_round.Length == 0 && text2.Length > 0)
				{
					OpenRound();
				}
				if (_round.Length > 0 || _roundBubble != null)
				{
					string text3 = _round.ToString();
					if (text2.Length > 0 && !text3.Contains(text2))
					{
						text3 = ((text3.Length > 0) ? (text3 + "\n\n" + text2) : text2);
					}
					_roundBubble.SetText(text3);
				}
				foreach (var (key, value3) in AiPage.PendingTasksOf(ev))
				{
					_aiPendingTasks[key] = value3;
				}
				ResetRunState();
				string unsent3 = AiPage.UnsentOf(ev);
				if (unsent3.Length > 0)
				{
					PageBase.Run(() => SendTextAsync(unsent3, echo: false));
				}
				break;
			}
			case 'f':
			{
				if (!(text == "ai.fail") || !Mine(ev))
				{
					break;
				}
				if (_round.Length > 0)
				{
					_roundBubble?.SetText(_round.ToString());
				}
				else if (ev.Stopped)
				{
					_roundBubble?.SetText(I18n.L("已停止"));
				}
				else if (_roundBubble != null)
				{
					_roundBubble.SetText(ev.Text);
				}
				else
				{
					Add(new Bubble("error", ev.Text, RetryFrom));
				}
				ResetRunState();
				string unsent2 = AiPage.UnsentOf(ev);
				if (unsent2.Length > 0)
				{
					PageBase.Run(() => SendTextAsync(unsent2, echo: false));
				}
				break;
			}
			}
			break;
		case 9:
			if (text == "ai.status" && Mine(ev))
			{
				OnStatus(ev);
			}
			break;
		case 10:
			if (text == "ai.confirm" && Mine(ev))
			{
				ShowConfirm(ev);
			}
			break;
		case 6:
			if (text == "ai.ask" && Mine(ev))
			{
				ShowAsk(ev);
			}
			break;
		}
	}

	private bool Mine(BridgeEvent ev)
	{
		string text = ChatIdOf(ev);
		if (text.Length <= 0)
		{
			return _busy;
		}
		return text == _chatId;
	}

	private static string ChatIdOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("chat_id", out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private void Add(UIElement el)
	{
		_messages.Children.Add(el);
		Motion.FadeIn(el, 200.0, 8.0);
		ScrollDown();
	}

	private void ScrollDown()
	{
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			_scroll.ScrollToEnd();
		});
	}

	private ToolLine Line(string name, string text, ToolState state)
	{
		if (_toolLines.TryGetValue(name, out ToolLine value))
		{
			value.SetText(text);
			value.SetState(state);
			return value;
		}
		value = new ToolLine(text, name);
		value.SetState(state);
		_toolLines[name] = value;
		Add(value);
		return value;
	}

	private void OnStatus(BridgeEvent ev)
	{
		string name = (string.IsNullOrEmpty(ev.Name) ? ev.Kind : ev.Name);
		string text = (string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label);
		switch (ev.Kind)
		{
		case "plan":
			ShowPlan(ev);
			break;
		case "checkpoint_warn":
		{
			JsonElement value;
			string text2 = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("message", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "");
			_status.Text = ((text2.Length > 0) ? text2 : I18n.L("检查点不可用，本轮改动无法自动撤回"));
			break;
		}
		case "model_fallback":
		{
			JsonElement value2;
			string text4 = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("model", out value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? "") : "");
			_status.Text = I18n.L("已切换备用模型") + ((text4.Length > 0) ? (" · " + text4) : "");
			break;
		}
		case "thinking":
			_status.Text = I18n.L("思考中…");
			if (_round.Length == 0)
			{
				OpenRound();
			}
			break;
		case "think":
		{
			JsonElement value3;
			bool flag = ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("after_tools", out value3) && value3.ValueKind == JsonValueKind.True;
			_status.Text = (flag ? I18n.L("搜完了，正在整理…") : I18n.L("思考中…"));
			if (_round.Length == 0)
			{
				OpenRound(flag ? I18n.L("搜完了，正在整理…") : I18n.L("正在想…"));
			}
			break;
		}
		case "tool":
			if (ev.Name == "ask_user")
			{
				if (_round.Length == 0)
				{
					OpenRound(I18n.L("请在下面选一下"));
				}
			}
			else
			{
				CloseRound();
				Line(name, I18n.L("准备：") + text, ToolState.Prepare);
				_status.Text = I18n.L("正在执行操作…");
			}
			break;
		case "tool_run":
		case "tool_start":
			CloseRound();
			Line(name, I18n.L("执行中：") + text, ToolState.Running);
			_status.Text = I18n.L("正在执行操作…");
			break;
		case "tool_done":
		{
			CloseRound();
			ToolLine toolLine = Line(name, I18n.L("完成：") + text, ToolState.Done);
			string text3 = TaskIdOf(ev);
			if (text3.Length > 0)
			{
				toolLine.BindTask();
				_taskLines[text3] = toolLine;
			}
			_status.Text = I18n.L("操作完成");
			break;
		}
		case "tool_skip":
			CloseRound();
			Line(name, I18n.L("已跳过：") + text, ToolState.Skipped);
			break;
		default:
			if (!string.IsNullOrWhiteSpace(ev.Label))
			{
				_status.Text = ev.Label;
			}
			break;
		}
	}

	private static string TaskIdOf(BridgeEvent ev)
	{
		if (!string.IsNullOrEmpty(ev.TaskId))
		{
			return ev.TaskId;
		}
		if (ev.Payload.ValueKind != JsonValueKind.Object)
		{
			return "";
		}
		if (ev.Payload.TryGetProperty("task_id", out var value) && value.ValueKind == JsonValueKind.String)
		{
			return value.GetString() ?? "";
		}
		if (!ev.Payload.TryGetProperty("result", out var value2) || value2.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		string text = value2.GetString() ?? "";
		if (!text.StartsWith("{", StringComparison.Ordinal))
		{
			return "";
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(text);
			JsonElement value3;
			return (jsonDocument.RootElement.TryGetProperty("task_id", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "";
		}
		catch
		{
			return "";
		}
	}

	private static string StopReasonOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("stop_reason", out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static string DetailOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("detail", out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static string StopNote(BridgeEvent ev)
	{
		string text = StopReasonOf(ev) switch
		{
			"no_tool_call" => I18n.L("它没有真的开始执行：模型只回了文字，没有调用任何工具。"), 
			"max_rounds" => I18n.L("步骤太多，先停在这里。你可以让我继续。"), 
			"pending_task" => I18n.L("下载/安装还在后台跑，完成后助手会回来汇报。"), 
			"stream_failed" => I18n.L("接口这轮没有返回内容，已停止。"), 
			"empty_response" => I18n.L("接口返回了空回复。"), 
			_ => "", 
		};
		if (text.Length == 0)
		{
			return "";
		}
		string text2 = DetailOf(ev);
		if (text2.Length > 0 && !text.Contains(text2))
		{
			text = text + I18n.L("（") + text2 + I18n.L("）");
		}
		return I18n.L("（提示：") + text + I18n.L("）");
	}

	private void ShowPlan(BridgeEvent ev)
	{
		List<(string, string)> list = new List<(string, string)>();
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("items", out var value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item2 in value.EnumerateArray())
			{
				if (item2.ValueKind == JsonValueKind.Object)
				{
					JsonElement value2;
					string text = (item2.TryGetProperty("title", out value2) ? (value2.GetString() ?? "") : "");
					JsonElement value3;
					string item = (item2.TryGetProperty("status", out value3) ? (value3.GetString() ?? "pending") : "pending");
					if (!string.IsNullOrWhiteSpace(text))
					{
						list.Add((text, item));
					}
				}
			}
		}
		if (list.Count != 0)
		{
			if (_planCard != null)
			{
				_messages.Children.Remove(_planCard);
				_planCard = null;
			}
			_planCard = new PlanCard(list);
			Add(_planCard);
		}
	}

	private void ShowConfirm(BridgeEvent ev)
	{
		CloseRound();
		string label = (string.IsNullOrWhiteSpace(ev.Label) ? ev.Name : ev.Label);
		JsonElement value;
		string text = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("reason", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "");
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("preview", out var value2) && value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("lines", out var value3) && value3.ValueKind == JsonValueKind.Array)
		{
			List<string> list = new List<string>();
			if (value2.TryGetProperty("head", out var value4) && value4.ValueKind == JsonValueKind.String)
			{
				list.Add(value4.GetString() ?? "");
			}
			foreach (JsonElement item in value3.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					list.Add(item.GetString() ?? "");
				}
			}
			string text2 = string.Join("\n", list.Where((string l) => l.Length > 0));
			if (text2.Length > 0)
			{
				text = (string.IsNullOrWhiteSpace(text) ? text2 : (text2 + "\n" + text));
			}
		}
		ConfirmCard el = new ConfirmCard(label, text, ConfirmCard.AllowAlways(ev), delegate(bool ok, bool always, string scope)
		{
			PageBase.Run(async delegate
			{
				await AppServices.Client.TryCallAsync<object>("ai_confirm", new { ok, always, scope });
			});
		});
		Add(el);
		_status.Text = I18n.L("等你确认");
	}

	private void ShowAsk(BridgeEvent ev)
	{
		CloseRound();
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("questions", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			PageBase.Run(async delegate
			{
				await AppServices.Client.TryCallAsync<object>("ai_answer", new
				{
					result = (object)null
				});
			});
			return;
		}
		JsonElement value2;
		AskCard el = new AskCard(ev.Payload.TryGetProperty("title", out value2) ? (value2.GetString() ?? "") : "", value, delegate(object? result)
		{
			PageBase.Run(async delegate
			{
				await AppServices.Client.TryCallAsync<object>("ai_answer", new { result });
			});
		});
		Add(el);
		_status.Text = I18n.L("等你回答");
	}

	protected override void OnClosed(EventArgs e)
	{
		if (_client != null)
		{
			_client.EventReceived -= OnBridgeEvent;
		}
		if (_busy)
		{
			PageBase.Fire(() => (_client ?? AppServices.Client).TryCallAsync<JsonElement>("ai_stop"));
		}
		base.OnClosed(e);
	}
}
