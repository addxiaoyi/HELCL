using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HelL.Pages;
using HelL.Services;

namespace HelL;

public static class Smoke
{
	private static long _opSeq;

	private static readonly ConcurrentDictionary<long, long> _inflight = new ConcurrentDictionary<long, long>();

	private static readonly ConcurrentDictionary<long, long> _finishedAt = new ConcurrentDictionary<long, long>();

	private static readonly List<(string Page, string Label, long ClickedAt, long[] Ops, Dictionary<string, object?> Record)> _late = new List<(string, string, long, long[], Dictionary<string, object>)>();

	private const int DefaultPageBudgetMs = 9000;

	private const int DefaultClickBudgetMs = 15000;

	private const int MaxClicksPerPage = 4;

	private const int MinClicks = 20;

	private const int MinDialogs = 10;

	private static readonly List<Dictionary<string, object?>> _pages = new List<Dictionary<string, object>>();

	private static readonly List<string> _issues = new List<string>();

	private static string _dir = "";

	private static int _pageBudgetMs = 9000;

	private static int _clickBudgetMs = 15000;

	private static string _runLabel = "";

	private static readonly List<Dictionary<string, object?>> _dialogs = new List<Dictionary<string, object>>();

	private static readonly List<Dictionary<string, object?>> _clicks = new List<Dictionary<string, object>>();

	private static readonly string[] SafeButtons = new string[36]
	{
		"刷新", "刷新清单", "刷新新闻", "查看", "查看推荐", "查看系统信息", "检查更新", "版本设置", "存档管理", "模组管理",
		"全局模组", "选择 Java", "隔离细分", "皮肤", "添加离线账号", "皮肤站登录", "统一通行证", "保存为主题包", "加载", "导入",
		"导入本地文件", "选择版本", "链接安装", "挑着更新模组", "新建实例", "导入官方启动器", "新对话", "选择入口", "重新运行", "重置条件",
		"智能推荐", "扫描并清理", "扫描系统 Java", "测试 AI 连接", "复制启动命令", "模型库"
	};

	private static readonly string[] NeverClick = new string[16]
	{
		"删除", "卸载", "清空", "清除", "移除", "永久", "恢复出厂", "重置", "启动游戏", "安装",
		"导出", "提交", "发送", "撤销", "关闭壁纸", "保存设置"
	};

	public static bool Active { get; private set; }

	public static long OperationStarted()
	{
		if (!Active)
		{
			return 0L;
		}
		long num = Interlocked.Increment(ref _opSeq);
		_inflight[num] = Stopwatch.GetTimestamp();
		return num;
	}

	public static void OperationFinished(long id)
	{
		if (id != 0L && _inflight.TryRemove(id, out var _))
		{
			_finishedAt[id] = Stopwatch.GetTimestamp();
		}
	}

	private static long[] PendingSince(long since)
	{
		return _inflight.Where(delegate(KeyValuePair<long, long> kv)
		{
			KeyValuePair<long, long> keyValuePair = kv;
			return keyValuePair.Value >= since;
		}).Select(delegate(KeyValuePair<long, long> kv)
		{
			KeyValuePair<long, long> keyValuePair = kv;
			return keyValuePair.Key;
		}).ToArray();
	}

	private static long MsSince(long from, long to)
	{
		return (to - from) * 1000 / Stopwatch.Frequency;
	}

	[DllImport("user32.dll")]
	private static extern int GetWindowLong(IntPtr hwnd, int index);

	[DllImport("user32.dll")]
	private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

	public static void Note(string where, string message)
	{
		if (!Active)
		{
			return;
		}
		lock (_issues)
		{
			_issues.Add(where + ": " + message);
		}
	}

	public static void AutoAnswerDialog(UIElement card, Action dismiss)
	{
		FrameworkElement fe = card as FrameworkElement;
		if (fe == null)
		{
			return;
		}
		string title = FirstText(fe);
		Dictionary<string, object?> record = new Dictionary<string, object>
		{
			["title"] = title,
			["type"] = "in-app"
		};
		lock (_dialogs)
		{
			_dialogs.Add(record);
		}
		fe.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
		{
			try
			{
				record["shot"] = ShootDialog(title);
				List<ButtonBase> source = (from b in Descendants<ButtonBase>(fe)
					where b.IsEnabled
					select b).ToList();
				string[] source2 = new string[7] { "取消", "算了", "关闭", "跳过", "知道了", "以后再说", "留空" };
				string[] cancelLabels = source2.Select(T).ToArray();
				ButtonBase buttonBase = source.FirstOrDefault((ButtonBase b) => cancelLabels.Contains<string>(TextOf(b).Trim(), StringComparer.Ordinal));
				if (buttonBase == null)
				{
					lock (_dialogs)
					{
						record["answered_with"] = "取消（对话层关闭）";
					}
					dismiss();
				}
				else
				{
					lock (_dialogs)
					{
						record["answered_with"] = TextOf(buttonBase);
					}
					buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
				}
			}
			catch (Exception ex)
			{
				Note("dialog", "「" + title + "」自动应答失败：" + ex.Message);
			}
		});
	}

	public static void AutoCancelSystemDialog(string title)
	{
		if (!Active)
		{
			return;
		}
		lock (_dialogs)
		{
			_dialogs.Add(new Dictionary<string, object>
			{
				["title"] = title,
				["type"] = "system-file-dialog",
				["answered_with"] = "取消（冒烟）"
			});
		}
	}

	private static string FirstText(DependencyObject root)
	{
		return (from t in Descendants<TextBlock>(root)
			where !IsGlyph(t)
			select t.Text).FirstOrDefault((string s) => !string.IsNullOrWhiteSpace(s)) ?? "(无标题)";
	}

	private static bool IsGlyph(TextBlock t)
	{
		FontFamily fontFamily = t.FontFamily;
		if (fontFamily == null)
		{
			return false;
		}
		return fontFamily.Source?.Contains("MDL2", StringComparison.OrdinalIgnoreCase) ?? false;
	}

	private static string TextOf(DependencyObject el)
	{
		if (el is ContentControl contentControl)
		{
			if (contentControl.Content is string text && !string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
			if (contentControl.Content is DependencyObject root)
			{
				string text2 = FirstText(root);
				if (text2 != "(无标题)")
				{
					return text2;
				}
			}
		}
		string text3 = FirstText(el);
		if (text3 == "(无标题)" && el is FrameworkElement { ToolTip: string toolTip } && !string.IsNullOrWhiteSpace(toolTip))
		{
			return toolTip;
		}
		return text3;
	}

	private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
	{
		int j = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < j; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is T val)
			{
				yield return val;
			}
			foreach (T item in Descendants<T>(child))
			{
				yield return item;
			}
		}
	}

	public static int Run(string[] args)
	{
		Active = true;
		int seconds = ArgInt(args, "--smoke-timeout", 420);
		_pageBudgetMs = ArgInt(args, "--smoke-page-budget", 9000);
		_clickBudgetMs = ArgInt(args, "--smoke-click-budget", 15000);
		int num = ArgInt(args, "--smoke-search-timeout", CatalogPage.SearchTimeoutMs);
		bool flag = num != CatalogPage.SearchTimeoutMs;
		CatalogPage.SearchTimeoutMs = num;
		VersionPage.FetchTimeoutMs = num;
		bool num2 = _pageBudgetMs != 9000 || _clickBudgetMs != 15000 || flag;
		string text = ((I18n.Current == "zh_CN") ? "" : (I18n.Current + "-"));
		_runLabel = ((!num2) ? $"run-{text}{DateTime.Now:yyyyMMdd-HHmmss}" : string.Format("negative-{0}p{1}-c{2}{3}-{4:yyyyMMdd-HHmmss}", text, _pageBudgetMs, _clickBudgetMs, flag ? $"-s{num}" : "", DateTime.Now));
		_dir = Path.Combine(BridgeRootOrHere(), "_wpf_smoke", _runLabel);
		Directory.CreateDirectory(_dir);
		Watchdog(seconds);
		Application current = Application.Current;
		MainWindow win = new MainWindow();
		Motion.Enabled = false;
		win.ShowActivated = false;
		win.ShowInTaskbar = false;
		win.WindowStartupLocation = WindowStartupLocation.Manual;
		win.Left = SystemParameters.VirtualScreenLeft - win.Width - 100.0;
		win.Top = SystemParameters.VirtualScreenTop - win.Height - 100.0;
		win.SourceInitialized += delegate
		{
			IntPtr handle = new WindowInteropHelper(win).Handle;
			SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x8000000);
		};
		win.Show();
		current.Dispatcher.InvokeAsync((Func<Task>)async delegate
		{
			await DriveAsync(win);
		}, DispatcherPriority.ApplicationIdle);
		return 0;
	}

	private static void Watchdog(int seconds)
	{
		Thread thread = new Thread((ThreadStart)delegate
		{
			Thread.Sleep(TimeSpan.FromSeconds(seconds));
			try
			{
				Note("watchdog", $"超过 {seconds}s 仍未结束，强制退出");
				Dump(99, "watchdog-timeout", 0L);
			}
			catch
			{
			}
			try
			{
				AppServices.Host?.Dispose();
			}
			catch
			{
			}
			Environment.Exit(99);
		});
		thread.IsBackground = true;
		thread.Name = "smoke-watchdog";
		thread.Start();
	}

	private static async Task DriveAsync(MainWindow win)
	{
		Stopwatch sw = Stopwatch.StartNew();
		string bridge;
		try
		{
			bridge = ((await Task.WhenAny(win.BridgeSettled, Task.Delay(40000)) != win.BridgeSettled) ? "连接超时" : (AppServices.Ready ? "已连上" : "未连上（已降级）"));
		}
		catch (Exception ex)
		{
			bridge = "连接异常：" + ex.Message;
		}
		foreach (NavDef def in MainWindow.NavDefs)
		{
			Dictionary<string, object?> page = new Dictionary<string, object>
			{
				["id"] = def.Id,
				["title"] = def.Title
			};
			Stopwatch t0 = Stopwatch.StartNew();
			long navigatedAt = Stopwatch.GetTimestamp();
			int before = IssueCount();
			try
			{
				win.Navigate(def.Id, instant: true);
				await win.Dispatcher.InvokeAsync(delegate
				{
				}, DispatcherPriority.ContextIdle);
				PageBase page2 = win.GetPage(def.Id);
				if (page2 == null)
				{
					throw new InvalidOperationException("GetPage 返回 null");
				}
				Task load = page2.EnsureLoadedAsync();
				if (await Task.WhenAny(load, Task.Delay(_pageBudgetMs)) != load)
				{
					page["ok"] = false;
					page["error"] = $"加载超过 {_pageBudgetMs}ms 未返回";
					Note(def.Id, "加载超时");
				}
				else
				{
					await load;
					await win.Dispatcher.InvokeAsync(delegate
					{
					}, DispatcherPriority.ContextIdle);
					page["ok"] = IssueCount() == before;
				}
			}
			catch (Exception ex2)
			{
				page["ok"] = false;
				page["error"] = ex2.GetType().Name + ": " + ex2.Message;
				Note(def.Id, ex2.ToString());
			}
			page["ms"] = t0.ElapsedMilliseconds;
			Dictionary<string, object?> dictionary = page;
			dictionary["settle_ms"] = await SettleAsync(win, CatalogPage.SearchTimeoutMs + 2000, navigatedAt);
			if (Dlg.AnyOpen)
			{
				Note(def.Id, "截图时对话层还开着，这张图里会叠着弹窗");
			}
			page["shot"] = Shoot(win, def.Id);
			object obj = (page["cjk_texts"] = CjkOnScreen(win));
			List<Dictionary<string, object>> source = (List<Dictionary<string, object>>)obj;
			if (I18n.Current != "zh_CN")
			{
				page["cjk_ui_residue"] = source.Count((Dictionary<string, object> x) => object.Equals(x["is_ui_key"], true));
			}
			dictionary = page;
			dictionary["clicked"] = await ClickSafeButtonsAsync(win, win.GetPage(def.Id), def.Id);
			lock (_pages)
			{
				_pages.Add(page);
			}
		}
		if (_clicks.Count < 20)
		{
			Note("coverage", $"只点击了 {_clicks.Count} 个按钮，要求至少 {20} 个");
		}
		int num = _dialogs.Count((Dictionary<string, object> d) => object.Equals(d.GetValueOrDefault("type"), "in-app"));
		if (num < 10)
		{
			Note("coverage", $"只打开了 {num} 个真实 WPF 弹窗，要求至少 {10} 个；系统文件框模拟取消不计入");
		}
		if (I18n.Current != "zh_CN")
		{
			IReadOnlyCollection<string> missing = I18n.Missing;
			if (missing.Count > 0)
			{
				Note("i18n", string.Format("{0} 下 L() 缺词回落中文 {1} 条：{2}", I18n.Current, missing.Count, string.Join(" | ", missing.Take(20))));
			}
		}
		List<(string, string, long, long[], Dictionary<string, object>)> list2;
		lock (_late)
		{
			list2 = new List<(string, string, long, long[], Dictionary<string, object>)>(_late);
		}
		foreach (var item in list2)
		{
			int num2 = item.Item4.Count((long id) => _inflight.ContainsKey(id));
			lock (_clicks)
			{
				if (num2 > 0)
				{
					item.Item5["ok"] = false;
					item.Item5["hung"] = num2;
					Note(item.Item1, $"点「{item.Item2}」起的 {num2} 个异步操作到整轮结束仍未收场");
				}
				else
				{
					long value2;
					long to = item.Item4.Select((long id) => (!_finishedAt.TryGetValue(id, out value2)) ? 0 : value2).Max();
					item.Item5["late_ms"] = MsSince(item.Item3, to);
				}
			}
		}
		object value;
		int code = ((_pages.Any((Dictionary<string, object> p) => p.TryGetValue("ok", out value) && value is bool && !(bool)value) || IssueCount() > 0) ? 1 : 0);
		Dump(code, "finished", sw.ElapsedMilliseconds, bridge);
		try
		{
			AppServices.Host?.Dispose();
		}
		catch
		{
		}
		Application.Current.Shutdown(code);
		// 关窗后兜底退出，不能等它
		_ = Task.Run(async delegate
		{
			await Task.Delay(1500);
			Environment.Exit(code);
		});
	}

	private static async Task<long> SettleAsync(MainWindow win, int budgetMs, long since)
	{
		Stopwatch sw = Stopwatch.StartNew();
		while ((Dlg.AnyOpen || PendingSince(since).Length != 0) && sw.ElapsedMilliseconds < budgetMs)
		{
			await Task.Delay(80);
			await win.Dispatcher.InvokeAsync(delegate
			{
			}, DispatcherPriority.ContextIdle);
		}
		return sw.ElapsedMilliseconds;
	}

	private static async Task<int> ClickSafeButtonsAsync(MainWindow win, DependencyObject? page, string pageId)
	{
		if (page == null || _clickBudgetMs <= 0)
		{
			return 0;
		}
		int clicked = 0;
		List<ButtonBase> list;
		try
		{
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			list = (from b in Descendants<ButtonBase>(page)
				where b.IsEnabled && b.IsVisible
				where Safe(TextOf(b))
				where seen.Add(TextOf(b))
				select b).Take(4).ToList();
		}
		catch
		{
			return 0;
		}
		Stopwatch budget = Stopwatch.StartNew();
		foreach (ButtonBase item in list)
		{
			if (budget.ElapsedMilliseconds > _clickBudgetMs)
			{
				break;
			}
			string label = TextOf(item);
			int before = IssueCount();
			long clickedAt = Stopwatch.GetTimestamp();
			try
			{
				item.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
				await win.Dispatcher.InvokeAsync(delegate
				{
				}, DispatcherPriority.ContextIdle);
				Stopwatch settle = Stopwatch.StartNew();
				do
				{
					await Task.Delay(80);
					await win.Dispatcher.InvokeAsync(delegate
					{
					}, DispatcherPriority.ContextIdle);
				}
				while ((Dlg.AnyOpen || PendingSince(clickedAt).Length != 0 || settle.ElapsedMilliseconds < 350) && settle.ElapsedMilliseconds < _clickBudgetMs);
				if (Dlg.AnyOpen)
				{
					Note(pageId, "点「" + label + "」之后对话层未在预算内关闭");
				}
				clicked++;
				Dictionary<string, object> dictionary = new Dictionary<string, object>
				{
					["page"] = pageId,
					["label"] = label,
					["ok"] = IssueCount() == before,
					["ms"] = MsSince(clickedAt, Stopwatch.GetTimestamp())
				};
				if (pageId == "ai" && label == "模型库" && (bool)dictionary["ok"])
				{
					dictionary["post_click_shot"] = Shoot(win, pageId + "-model-library");
					dictionary["post_click_cjk_texts"] = CjkOnScreen(win);
				}
				long[] array = PendingSince(clickedAt);
				if (array.Length != 0)
				{
					dictionary["slow"] = true;
					lock (_late)
					{
						_late.Add((pageId, label, clickedAt, array, dictionary));
					}
				}
				lock (_clicks)
				{
					_clicks.Add(dictionary);
				}
			}
			catch (Exception ex)
			{
				Note(pageId, $"点「{label}」抛了：{ex}");
				lock (_clicks)
				{
					_clicks.Add(new Dictionary<string, object>
					{
						["page"] = pageId,
						["label"] = label,
						["ok"] = false,
						["error"] = ex.Message
					});
				}
			}
		}
		return clicked;
	}

	private static string T(string key)
	{
		return I18n.Peek(key) ?? key;
	}

	private static bool Safe(string label)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return false;
		}
		string trimmed = label.Trim();
		if (trimmed == T("链接安装"))
		{
			return true;
		}
		if (NeverClick.Any((string bad) => label.Contains(T(bad), StringComparison.Ordinal) || label.Contains(bad, StringComparison.Ordinal)))
		{
			return false;
		}
		return SafeButtons.Any((string ok) => T(ok) == trimmed);
	}

	private static List<Dictionary<string, object?>> CjkOnScreen(MainWindow win)
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> known = new HashSet<string>(I18n.KnownKeys, StringComparer.Ordinal);
		List<Dictionary<string, object?>> out_ = new List<Dictionary<string, object>>();
		try
		{
			Add(win.Title);
			foreach (FrameworkElement item in Descendants<FrameworkElement>(win))
			{
				if (!item.IsVisible)
				{
					continue;
				}
				FrameworkElement frameworkElement = item;
				if (!(frameworkElement is TextBlock textBlock))
				{
					if (!(frameworkElement is TextBox) && frameworkElement is ContentControl { Content: string content })
					{
						Add(content);
					}
				}
				else if (!IsGlyph(textBlock))
				{
					Add(textBlock.Text);
				}
				if (item.ToolTip is string text)
				{
					Add(text);
				}
			}
		}
		catch (Exception ex)
		{
			Note("cjk-scan", ex.Message);
		}
		return out_;
		void Add(string? text2)
		{
			if (!string.IsNullOrWhiteSpace(text2) && I18nCheck.HasCjk(text2))
			{
				string text3 = text2.Trim();
				if (seen.Add(text3) && out_.Count < 60)
				{
					out_.Add(new Dictionary<string, object>
					{
						["text"] = ((text3.Length > 80) ? (text3.Substring(0, 80) + "…") : text3),
						["is_ui_key"] = known.Contains(text3)
					});
				}
			}
		}
	}

	private static int IssueCount()
	{
		lock (_issues)
		{
			return _issues.Count;
		}
	}

	private static string Shoot(MainWindow win, string id)
	{
		try
		{
			int pixelWidth = (int)Math.Max(1.0, win.ActualWidth);
			int pixelHeight = (int)Math.Max(1.0, win.ActualHeight);
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap.Render(win);
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
			pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
			string text = Path.Combine(_dir, id + ".png");
			using FileStream stream = File.Create(text);
			pngBitmapEncoder.Save(stream);
			return text;
		}
		catch (Exception ex)
		{
			return "截图失败：" + ex.Message;
		}
	}

	private static string ShootDialog(string title)
	{
		try
		{
			if (!(Application.Current.MainWindow is MainWindow win))
			{
				return "主窗口不可用";
			}
			int count = _dialogs.Count;
			string text = string.Concat(title.Select((char c) => (!Path.GetInvalidFileNameChars().Contains(c)) ? c : '_'));
			if (text.Length > 32)
			{
				text = text.Substring(0, 32);
			}
			return Shoot(win, $"dialog-{count:00}-{text}");
		}
		catch (Exception ex)
		{
			return "截图失败：" + ex.Message;
		}
	}

	private static void Dump(int code, string state, long ms = 0L, string bridge = "")
	{
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["state"] = state;
		dictionary["exit_code"] = code;
		dictionary["run"] = _runLabel;
		dictionary["page_budget_ms"] = _pageBudgetMs;
		dictionary["click_budget_ms"] = _clickBudgetMs;
		dictionary["search_timeout_ms"] = CatalogPage.SearchTimeoutMs;
		dictionary["is_negative_control"] = _runLabel.StartsWith("negative-", StringComparison.Ordinal);
		dictionary["required_clicks"] = 20;
		dictionary["required_dialogs"] = 10;
		dictionary["dialog_threshold_kind"] = "in-app-only";
		dictionary["display_mode"] = "offscreen-no-activate";
		dictionary["lang"] = I18n.Current;
		dictionary["i18n_missing"] = I18n.Missing.OrderBy<string, string>((string k) => k, StringComparer.Ordinal).ToList();
		dictionary["skipped"] = new string[1] { "微软正版登录（无账号，按要求跳过）" };
		dictionary["click_evidence_scope"] = "event-dispatched; every async handler goes through PageBase.Run (pending counter awaited, exceptions recorded as issues); dialog layer settled";
		dictionary["dir"] = _dir;
		dictionary["elapsed_ms"] = ms;
		dictionary["bridge"] = bridge;
		dictionary["pages"] = Snapshot(_pages);
		dictionary["dialogs"] = Snapshot(_dialogs);
		dictionary["clicks"] = Snapshot(_clicks);
		dictionary["issues"] = IssuesSnapshot();
		Dictionary<string, object> value = dictionary;
		string text;
		try
		{
			text = JsonSerializer.Serialize(value, new JsonSerializerOptions
			{
				WriteIndented = true,
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
			});
		}
		catch (Exception ex)
		{
			text = "{\"state\":\"" + state + "\",\"exit_code\":" + code + ",\"run\":\"" + _runLabel + "\",\"serialize_error\":\"" + ex.Message.Replace("\"", "'") + "\"}";
		}
		try
		{
			File.WriteAllText(Path.Combine(_dir, "report.json"), text);
		}
		catch
		{
		}
		try
		{
			Console.Out.Write(text);
			Console.Out.Flush();
		}
		catch
		{
		}
	}

	private static List<Dictionary<string, object?>> Snapshot(List<Dictionary<string, object?>> src)
	{
		lock (src)
		{
			List<Dictionary<string, object>> list = new List<Dictionary<string, object>>(src.Count);
			foreach (Dictionary<string, object> item in src)
			{
				list.Add(new Dictionary<string, object>(item));
			}
			return list;
		}
	}

	private static List<string> IssuesSnapshot()
	{
		lock (_issues)
		{
			return new List<string>(_issues);
		}
	}

	private static int ArgInt(string[] args, string name, int fallback)
	{
		int num = Array.IndexOf<string>(args, name);
		if (num < 0 || num + 1 >= args.Length || !int.TryParse(args[num + 1], out var result))
		{
			return fallback;
		}
		return result;
	}

	private static string BridgeRootOrHere()
	{
		try
		{
			return BridgeHost.FindRoot();
		}
		catch
		{
			return AppContext.BaseDirectory;
		}
	}
}
