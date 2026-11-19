using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HelL.Models;
using HelL.Pages;
using HelL.Services;

namespace HelL;

public partial class MainWindow : Window
{
	private struct POINT
	{
		public int x;

		public int y;
	}

	private struct RECT
	{
		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	private struct MINMAXINFO
	{
		public POINT ptReserved;

		public POINT ptMaxSize;

		public POINT ptMaxPosition;

		public POINT ptMinTrackSize;

		public POINT ptMaxTrackSize;
	}

	private struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public int dwFlags;
	}

	private sealed class NavRow
	{
		public required string Key { get; init; }

		public required ToggleButton Button { get; init; }

		public string? Section { get; init; }

		public bool IsSectionHeader
		{
			get
			{
				if (NavModel.IsSection(Key))
				{
					return Section == null;
				}
				return false;
			}
		}
	}

	private readonly Dictionary<string, PageBase> _pages = new Dictionary<string, PageBase>();

	private PageBase? _current;

	private string _currentId = "";

	private Border? _dockCard;

	private ProgressBar? _dockBar;

	private TextBlock? _dockTitle;

	private TextBlock? _dockStatus;

	private bool _bridgeReady;

	private bool _drained;

	private bool _closeAllowed;

	private readonly TaskCompletionSource<object?> _bridgeSettled = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

	private bool _reconnecting;

	private DispatcherTimer? _refreshTimer;

	private bool? _pendingDark;

	private readonly Dictionary<string, bool> _downloadTitle = new Dictionary<string, bool>();

	public const int SideMinWidth = 140;

	public const int SideMaxWidth = 320;

	private const double SideDefaultWidth = 196.0;

	public static readonly List<NavDef> NavDefs = new List<NavDef>
	{
		new NavDef("launch", I18n.L("启动"), "\ue7c1", () => new LaunchPage()),
		new NavDef("instance", I18n.L("版本管理"), "\ue80a", () => new InstancePage()),
		new NavDef("version", I18n.L("原版游戏"), "\ue7fc", () => new VersionPage()),
		new NavDef("mod", I18n.L("模组下载"), "\uea86", () => new CatalogPage(CatalogKind.Mod)),
		new NavDef("mods", I18n.L("模组管理"), "\ue8fd", () => new CatalogPage(CatalogKind.Mod, installedFirst: true)),
		new NavDef("modpack", I18n.L("整合包"), "\ue7b8", () => new CatalogPage(CatalogKind.Modpack)),
		new NavDef("datapack", I18n.L("数据包"), "\ue8f1", () => new CatalogPage(CatalogKind.Datapack)),
		new NavDef("resourcepack", I18n.L("资源包"), "\ueb9f", () => new CatalogPage(CatalogKind.ResourcePack)),
		new NavDef("shader", I18n.L("光影包"), "\ue706", () => new CatalogPage(CatalogKind.Shader)),
		new NavDef("world", I18n.L("世界"), "\ue909", () => new CatalogPage(CatalogKind.World)),
		new NavDef("account", I18n.L("账号"), "\ue77b", () => new AccountPage()),
		new NavDef("java", "Java", "\uec32", () => new JavaPage()),
		new NavDef("ai", I18n.L("AI 助手"), "\ue99a", () => new AiPage()),
		new NavDef("multiplayer", I18n.L("联机"), "\ue701", () => new MultiplayerPage()),
		new NavDef("servers", I18n.L("服务器"), "\ue968", () => new ServersPage()),
		new NavDef("playtime", I18n.L("游戏时长"), "\ue823", () => new PlaytimePage()),
		new NavDef("feedback", I18n.L("反馈与帮助"), "\ue8bd", () => new FeedbackPage()),
		new NavDef("settings", I18n.L("设置"), "\ue713", () => new SettingsPage()),
		new NavDef("plugins", "插件市场", "\uea86", () => new PluginsPage()),
		new NavDef("tasks", I18n.L("下载任务"), "\ue9d5", () => new TasksPage())
	};

	private static readonly Dictionary<string, string> NavGlyphs = new Dictionary<string, string>
	{
		["launch"] = "\ue7c1",
		["download"] = "\ue896",
		["ai"] = "\ue99a",
		["more"] = "\ue712",
		["tasks"] = "\ue9d5",
		["version"] = "\ue7fc",
		["mod"] = "\uea86",
		["modpack"] = "\ue7b8",
		["datapack"] = "\ue8f1",
		["resource"] = "\ueb9f",
		["shader"] = "\ue706",
		["world"] = "\ue909",
		["java"] = "\uec32",
		["instance"] = "\ue80a",
		["mods"] = "\ue8fd",
		["account"] = "\ue77b",
		["multiplayer"] = "\ue701",
		["servers"] = "\ue968",
		["playtime"] = "\ue823",
		["feedback"] = "\ue8bd",
		["settings"] = "\ue713"
	};

	private readonly Dictionary<string, NavRow> _navRows = new Dictionary<string, NavRow>();

	private readonly Dictionary<string, StackPanel> _groupPanels = new Dictionary<string, StackPanel>();

	private UiPrefs _prefs = new UiPrefs();
    private bool _restartForAppearance;

	private NavConfig _navCfg = new NavConfig();

	private Border? _taskBadge;

	private TextBlock? _taskBadgeText;

	private Point _dragStart;

	private NavRow? _dragRow;

	private bool _dragging;

	private Border? _dropLine;

	private NavRow? _dropTarget;

	private bool _dropBefore;

	private static readonly Dictionary<string, string> HeaderGlyphs = new Dictionary<string, string>
	{
		[I18n.L("账户")] = "\ue77b",
		[I18n.L("游戏")] = "\ue7fc",
		[I18n.L("通用")] = "\ue713"
	};

	public string CurrentPageId => _currentId;

	public Task BridgeSettled => _bridgeSettled.Task;

	private static string PrefsPath
	{
		get
		{
			string path;
			try
			{
				path = BridgeHost.FindRoot();
			}
			catch
			{
				path = AppContext.BaseDirectory;
			}
			return System.IO.Path.Combine(path, "wpf-ui.json");
		}
	}

	public UiPrefs Prefs => _prefs;

	public NavConfig NavConfig => _navCfg;

	public MainWindow()
	{
		InitializeComponent();
        DesktopTransparency.Prepare(this);
		SetResourceReference(Control.ForegroundProperty, "B.Ink");
		base.Title = I18n.L("HelL 启动器");
		PageTitle.Text = I18n.L("启动");
		ThemeBtn.ToolTip = I18n.L("切换深色 / 浅色");
		DropVeilTitle.Text = I18n.L("松手就装");
		DropVeilHint.Text = I18n.L("整合包、模组、资源包、光影、数据包、存档、皮肤、壁纸都认；认不准会问你一句。");
		AppServices.Window = this;
		Dlg.Host = DialogHost;
		Dlg.BlurTarget = ContentLayer;
		AppThemeSkin();
		base.SourceInitialized += delegate
		{
			Backdrop.Apply(this);
		};
		Wallpaper.Attach(WallHost, ChromeBar, SideBar, PageWash);
		SideFrost.Attach(WallHost, SideBar, _prefs.SideFrost, _prefs.SideFrostRadius);
		SideSkin.Attach(SideBar, NavScroll, SideCol, SideGrip, GripCol, RootBorder);
		MinBtn.Click += delegate
		{
			base.WindowState = WindowState.Minimized;
		};
		MaxBtn.Click += delegate
		{
			ToggleMax();
		};
		CloseBtn.Click += delegate
		{
			Close();
		};
		ThemeBtn.Click += delegate
		{
			ToggleTheme();
		};
		base.StateChanged += delegate
		{
			SyncMaxGlyph();
			Wallpaper.SetWindowActive(IsActive && base.WindowState != WindowState.Minimized);
		};
		base.Activated += delegate
		{
			Wallpaper.SetWindowActive(IsActive && base.WindowState != WindowState.Minimized);
		};
		base.Deactivated += delegate
		{
			Wallpaper.SetWindowActive(active: false);
		};
		base.PreviewDragEnter += delegate(object _, DragEventArgs e)
		{
			ShowDropVeil(e.Data.GetDataPresent(DataFormats.FileDrop));
		};
		base.PreviewDragOver += delegate(object _, DragEventArgs e)
		{
			ShowDropVeil(e.Data.GetDataPresent(DataFormats.FileDrop));
		};
		base.PreviewDragLeave += delegate
		{
			ShowDropVeil(on: false);
		};
		base.PreviewDrop += delegate
		{
			ShowDropVeil(on: false);
		};
		base.DragEnter += OnDragOver;
		base.DragOver += OnDragOver;
		base.Drop += OnDrop;
		SideGrip.DragDelta += SideGrip_DragDelta;
        NavToggle.Click += (_, _) => ToggleNavigation();
		SideGrip.DragCompleted += SideGrip_DragCompleted;
		base.PreviewKeyDown += OnShortcut;
		base.Closing += OnClosing;
		base.Closed += delegate
		{
			if (_stagedUpdate != null)
			{
				try
				{
					HelL.Core.Launcher.Updater.ApplyStaged(_stagedUpdate, restart: false);
				}
				catch (Exception)
				{
				}
				_stagedUpdate = null;
			}
			Wallpaper.Shutdown();
			AppServices.Host?.Dispose();
            if (_restartForAppearance) RestartAppearanceProcess();
		};
		TaskStore.Added += delegate
		{
			base.Dispatcher.Invoke(SyncTasks);
		};
		TaskStore.Updated += delegate
		{
			base.Dispatcher.Invoke(SyncDock);
		};
		TaskStore.Cleared += delegate
		{
			base.Dispatcher.Invoke(SyncTasks);
		};
		LoadUiPrefs();
		SideFrost.SetRadius(_prefs.SideFrostRadius);
		SideFrost.SetEnabled(_prefs.SideFrost);
		BuildNav();
		BuildDock();
		Navigate("launch", instant: true);
		base.Loaded += delegate
		{
			ApplySkinNow();
			Run(() => ConnectAsync());
		};
	}

	private static void AppThemeSkin()
	{
		try
		{
			SettingsPage.ApplySkin();
		}
		catch
		{
		}
	}

	internal void ApplySkinNow()
	{
		AppThemeSkin();
		foreach (NavRow value in _navRows.Values)
		{
			Restyle(value.Button);
		}
		SideSkin.Apply();
		SyncRail();
		SyncCaption();
		Backdrop.Refresh(this);
	}

	/// <summary>macOS 皮肤把标题栏按钮换成左上角红黄绿圆点；Win11 皮肤维持右侧码表。</summary>
	private void SyncCaption()
	{
		try
		{
			bool mac = Glass.IsMac;
			MaxBtn.ClearValue(Control.BackgroundProperty);
			CaptionHost.Children.Clear();
			if (!mac)
			{
				CaptionHost.HorizontalAlignment = HorizontalAlignment.Right;
				CaptionHost.Margin = new Thickness(0);
				BrandHost.Margin = new Thickness(20, 0, 0, 0);
				ThemeBtn.Style = Ui.S("Btn.Caption");
				ThemeBtn.ClearValue(Button.WidthProperty);
				ThemeBtn.ClearValue(Button.HeightProperty);
				ThemeBtn.Margin = new Thickness(0, 0, 142, 0);
				MinBtn.Style = Ui.S("Btn.Caption");
				MinBtn.ClearValue(FrameworkElement.WidthProperty);
				MinBtn.ClearValue(FrameworkElement.HeightProperty);
				MinBtn.ClearValue(FrameworkElement.MarginProperty);
				MaxBtn.Style = Ui.S("Btn.Caption");
				MaxBtn.ClearValue(FrameworkElement.WidthProperty);
				MaxBtn.ClearValue(FrameworkElement.HeightProperty);
				MaxBtn.ClearValue(FrameworkElement.MarginProperty);
				CloseBtn.Style = Ui.S("Btn.CaptionClose");
				CloseBtn.ClearValue(FrameworkElement.WidthProperty);
				CloseBtn.ClearValue(FrameworkElement.HeightProperty);
				CloseBtn.ClearValue(FrameworkElement.MarginProperty);
				CaptionHost.Children.Add(MinBtn);
				CaptionHost.Children.Add(MaxBtn);
				CaptionHost.Children.Add(CloseBtn);
				return;
			}
			CaptionHost.HorizontalAlignment = HorizontalAlignment.Left;
			CaptionHost.Margin = new Thickness(12, 0, 0, 0);
			BrandHost.Margin = new Thickness(108, 0, 0, 0);
			ThemeBtn.Style = Ui.S("Btn.Caption");
			ThemeBtn.Width = 38;
			ThemeBtn.Height = 36;
			ThemeBtn.Margin = new Thickness(0, 0, 10, 0);
			CloseBtn.Style = Ui.S("Skin.DotClose");
			CloseBtn.Width = 26;
			CloseBtn.Height = 32;
			CloseBtn.Margin = new Thickness(0);
			MinBtn.Style = Ui.S("Skin.DotMin");
			MinBtn.Width = 26;
			MinBtn.Height = 32;
			MinBtn.Margin = new Thickness(0);
			MaxBtn.Style = Ui.S("Skin.DotMax");
			MaxBtn.Width = 26;
			MaxBtn.Height = 32;
			MaxBtn.Margin = new Thickness(0);
			CaptionHost.Children.Add(CloseBtn);
			CaptionHost.Children.Add(MinBtn);
			CaptionHost.Children.Add(MaxBtn);
		}
		catch (Exception ex) { Smoke.Note("caption-style", ex.ToString()); }
	}

	private void SyncRail()
	{
		SideSkin.SyncRail();
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (_drained)
		{
			if (!_closeAllowed)
			{
				e.Cancel = true;
			}
			return;
		}
		if (!_bridgeReady)
		{
			return;
		}
		e.Cancel = true;
		_drained = true;
		Run(async delegate
		{
			try
			{
				foreach (var launch in _pages.Values.OfType<LaunchPage>()) await launch.FlushPendingEditsAsync();
			}
			catch (Exception ex)
			{
				_drained = false;
                _restartForAppearance = false;
				Smoke.Note("layout-close", ex.ToString());
				Toast("布局尚未保存", "请稍后重试关闭：" + ex.Message, ToastKind.Error);
				return;
			}
			try
			{
				Task<object> task = AppServices.Client.TryCallAsync<object>("shutdown", new
				{
					timeout_ms = 800
				});
				await Task.WhenAny(task, Task.Delay(1500));
			}
			catch
			{
			}
			try
			{
				_closeAllowed = true;
				Close();
			}
			catch (InvalidOperationException)
			{
			}
		});
	}

	private async Task ConnectAsync(bool interactive = true)
	{
		DetachHost();
		try
		{
			BridgeHost host = await BridgeHost.StartAsync();
			AppServices.Host = host;
			AppServices.Client = host.Client;
			host.Client.EventReceived += OnBridgeEvent;
			host.Client.StreamStateChanged += delegate(object _, bool ok)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					if (ok)
					{
						Run(ReconcileTasksAsync);
					}
				});
			};
			host.Exited += delegate
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					Run(ReconnectAfterDeathAsync);
				});
			};
			_bridgeReady = true;
			await SyncAppearanceFromBridgeAsync();
			await LoadNavFromBridgeAsync();
			await Wallpaper.ReloadAsync();
			await Typography.Load();
			await ReconcileTasksAsync();
			await ReloadCurrentAsync();
			_bridgeSettled.TrySetResult(null);
			if (!Smoke.Active)
			{
				await FirstRunWizard.MaybeShowAsync();
			}
			if (!Smoke.Active)
			{
				await FeedbackConsent.MaybeAskAsync();
			}
			PageBase.Fire(WarmUpAsync);
			if (!Smoke.Active)
			{
				Run(AutoCheckUpdateAsync);
			}
		}
		catch (Exception ex)
		{
			_bridgeSettled.TrySetResult(null);
			if (Smoke.Active)
			{
				Smoke.Note("bridge", ex.ToString());
				return;
			}
			if (!interactive)
			{
				Toast(I18n.L("连接后端失败"), ex.Message, ToastKind.Error);
				return;
			}
			if (await Dlg.Confirm(I18n.L("连接后端失败"), ex.Message + I18n.L("\n\n可以点「重试」；如果反复出现，请关掉所有 HelL 窗口后再打开。"), I18n.L("重试"), I18n.L("退出")))
			{
				await ConnectAsync();
			}
			else
			{
				Close();
			}
		}
	}

	private async Task SyncAppearanceFromBridgeAsync()
	{
		bool? pendingDark = _pendingDark;
		if (pendingDark.HasValue)
		{
			bool valueOrDefault = pendingDark == true;
			_pendingDark = null;
			await PushDarkAsync(valueOrDefault);
		}
		Dictionary<string, JsonElement> dictionary = await AppServices.Client.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
		if (dictionary != null)
		{
			bool flag = App.IsDark;
			JsonElement value;
			bool flag2 = dictionary.TryGetValue("ui_dark", out value);
			if (flag2)
			{
				flag2 = (int)(value.ValueKind - 5) <= 1;
			}
			if (flag2)
			{
				flag = value.GetBoolean();
			}
			string color = null;
			if (dictionary.TryGetValue("theme_color", out var value2) && value2.ValueKind == JsonValueKind.String)
			{
				color = value2.GetString();
			}
			string preset = null;
			if (dictionary.TryGetValue("ui_bg_preset", out var value3) && value3.ValueKind == JsonValueKind.String)
			{
				preset = value3.GetString();
			}
			bool flag3 = SettingsPage.SyncPersistedThemeColor(color);
			bool flag4 = SettingsPage.SyncPersistedBackground(preset);
			bool num = flag != App.IsDark;
			if (num)
			{
				ApplyThemeState(flag, persistLocal: false);
			}
			else if (flag3 || flag4)
			{
				SettingsPage.ApplyVisuals();
			}
			SyncThemeIndicator();
			if (num)
			{
				SaveUiPrefs();
			}
		}
	}

	private void DetachHost()
	{
		BridgeHost host = AppServices.Host;
		if (host != null)
		{
			try
			{
				host.Client.EventReceived -= OnBridgeEvent;
			}
			catch
			{
			}
			try
			{
				host.Dispose();
			}
			catch
			{
			}
			AppServices.Host = null;
			_bridgeReady = false;
		}
	}

	private async Task ReconnectAfterDeathAsync()
	{
		if (_reconnecting || _drained)
		{
			return;
		}
		_reconnecting = true;
		try
		{
			await Task.Delay(400);
			await ConnectAsync(interactive: false);
		}
		catch (Exception ex)
		{
			Toast(I18n.L("连接后端失败"), ex.Message, ToastKind.Error);
		}
		finally
		{
			_reconnecting = false;
		}
	}

	private async Task ReconcileTasksAsync()
	{
		if (!_bridgeReady)
		{
			return;
		}
		JsonElement jsonElement = await AppServices.Client.TryCallAsync<JsonElement>("list_tasks");
		if (jsonElement.ValueKind != JsonValueKind.Object)
		{
			return;
		}
		List<(string, string)> list = new List<(string, string)>();
		if (jsonElement.TryGetProperty("running", out var value) && value.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item4 in value.EnumerateArray())
			{
				JsonElement value2;
				string text = (item4.TryGetProperty("task_id", out value2) ? (value2.GetString() ?? "") : "");
				JsonElement value3;
				string item = (item4.TryGetProperty("title", out value3) ? (value3.GetString() ?? "") : "");
				if (text.Length > 0)
				{
					list.Add((text, item));
				}
			}
		}
		List<(string, bool, string)> list2 = new List<(string, bool, string)>();
		if (jsonElement.TryGetProperty("finished", out var value4) && value4.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item5 in value4.EnumerateArray())
			{
				JsonElement value5;
				string text2 = (item5.TryGetProperty("task_id", out value5) ? (value5.GetString() ?? "") : "");
				if (text2.Length != 0)
				{
					JsonElement value6;
					bool item2 = item5.TryGetProperty("success", out value6) && value6.ValueKind == JsonValueKind.True;
					JsonElement value7;
					string item3 = (item5.TryGetProperty("message", out value7) ? (value7.GetString() ?? "") : "");
					list2.Add((text2, item2, item3));
				}
			}
		}
		if (TaskStore.Reconcile(list, list2))
		{
			SyncTasks();
			SyncDock();
		}
	}

	private async Task WarmUpAsync()
	{
		await Task.Delay(900);
		string[] array = new string[3] { "instance", "version", "tasks" };
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (!(_currentId == text))
			{
				PageBase page = GetPage(text);
				if (page != null)
				{
					await page.EnsureLoadedAsync();
				}
				await Task.Delay(140);
			}
		}
	}

	private static string? _stagedUpdate;

	private static bool _autoUpdateChecked;

	/// <summary>启动后按「自动检查更新」设置静默检查一次，有新版就问一句。</summary>
	private async Task AutoCheckUpdateAsync()
	{
		if (_autoUpdateChecked)
		{
			return;
		}
		_autoUpdateChecked = true;
		try
		{
			if (!HelL.Core.Py.Truthy(HelL.Core.Launcher.Config.CONFIG.Get("auto_check_update", true)))
			{
				return;
			}
			await Task.Delay(3000);
			Task<UpdateInfo> task = AppServices.Client.TryCallAsync<UpdateInfo>("check_update");
			Task completed = await Task.WhenAny(task, Task.Delay(8000));
			if (completed != task)
			{
				return;
			}
			UpdateInfo info = task.Result;
			if (info == null || !info.Ok || !info.HasUpdate)
			{
				return;
			}
			string body = (I18n.L("HelL {0} 已发布。", info.Version) + "\n" + info.Notes).Trim();
			if (await Dlg.Confirm(I18n.L("发现新版本"), body + I18n.L("\n\n现在下载更新？下载完会提示你重启。"), I18n.L("下载更新"), I18n.L("以后再说")))
			{
				await AppServices.Client.StartTaskAsync("start_self_update");
			}
		}
		catch (Exception)
		{
		}
	}

	private async Task OfferRestartForUpdateAsync(BridgeEvent ev)
	{
		string package = "";
		string version = "";
		try
		{
			if (ev.Payload.ValueKind == JsonValueKind.Object)
			{
				if (ev.Payload.TryGetProperty("package", out JsonElement p))
				{
					package = p.GetString() ?? "";
				}
				if (ev.Payload.TryGetProperty("version", out JsonElement v))
				{
					version = v.ToString();
				}
			}
		}
		catch
		{
		}
		if (package.Length == 0)
		{
			return;
		}
		_stagedUpdate = package;
		if (await Dlg.Confirm(I18n.L("更新已下载"), I18n.L("重启 HelL 即可更新到 {0}。", version), I18n.L("立即重启"), I18n.L("关闭时再装")))
		{
			try
			{
				HelL.Core.Launcher.Updater.ApplyStaged(package, restart: true);
				_stagedUpdate = null;
				Application.Current.Shutdown();
			}
			catch (Exception ex)
			{
				Toast(I18n.L("更新失败"), ex.Message, ToastKind.Error);
			}
		}
		else
		{
			Toast(I18n.L("更新已就绪"), I18n.L("关闭启动器时会自动装好，下次打开就是新版"), ToastKind.Success);
		}
	}

	private void OnBridgeEvent(object? sender, BridgeEvent ev)
	{
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
			{
				OnBridgeEvent(sender, ev);
			});
			return;
		}
		TaskStore.Handle(ev);
		switch (ev.Event)
		{
		case "update_staged":
			Run(() => OfferRestartForUpdateAsync(ev));
			break;
		case "task_count_changed":
			SyncTasks();
			break;
		case "ui_changed":
			ScheduleRefresh();
			break;
		case "finished":
			if (!ev.Success)
			{
				string message = ev.Message;
				if (message != null && message.Length > 0 && ev.Message != "已取消")
				{
					Toast(TaskStore.Get(ev.TaskId)?.Title ?? I18n.L("任务失败"), ev.Message, ToastKind.Error);
					break;
				}
			}
			if (ev.Success)
			{
				TaskRow taskRow = TaskStore.Get(ev.TaskId);
				if (taskRow != null && !taskRow.Title.StartsWith("启动游戏", StringComparison.Ordinal))
				{
					Toast(taskRow.Title, string.IsNullOrEmpty(ev.Message) ? I18n.L("已完成") : ev.Message, ToastKind.Success);
				}
			}
			break;
		}
		foreach (PageBase value in _pages.Values)
		{
			value.OnEvent(ev);
		}
		SyncDock();
	}

	private void ScheduleRefresh()
	{
		if (_refreshTimer == null)
		{
			_refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromMilliseconds(420.0)
			};
		}
		_refreshTimer.Stop();
		_refreshTimer.Tick -= RefreshTick;
		_refreshTimer.Tick += RefreshTick;
		_refreshTimer.Start();
	}

	private void RefreshTick(object? sender, EventArgs e)
	{
		_refreshTimer?.Stop();
		PageBase current = _current;
		if (current != null)
		{
			Run(current.RefreshAsync);
		}
	}

	private async Task ReloadCurrentAsync()
	{
		if (_current != null)
		{
			await _current.EnsureLoadedAsync();
			_current.OnShown();
		}
	}

	public void Navigate(string id, bool instant = false)
	{
		if (_currentId == id)
		{
			return;
		}
		PageBase page = GetPage(id);
		if (page == null)
		{
			return;
		}
		bool forward = NavOrderIndex(id) >= NavOrderIndex(_currentId);
		_current?.OnHidden();
		_currentId = id;
		_current = page;
		PageTitle.Text = page.Title;
		SyncNavSelection();
		if (instant || !Motion.Enabled)
		{
			PageHost.Content = page;
		}
		else
		{
			Motion.PageSwap(PageHost, page, forward);
		}
		SyncDock();
		if (_bridgeReady)
		{
			Run(async delegate
			{
				await page.EnsureLoadedAsync();
				page.OnShown();
			});
		}
	}

	private static void Run(Func<Task> work)
	{
		PageBase.Run(work, I18n.L("出错"));
	}

	public PageBase? GetPage(string id)
	{
		if (_pages.TryGetValue(id, out PageBase value))
		{
			return value;
		}
		NavDef navDef = NavDefs.FirstOrDefault((NavDef d) => d.Id == id);
		if ((object)navDef == null)
		{
			return null;
		}
		value = navDef.Create();
		value.Id = id;
		_pages[id] = value;
		return value;
	}

	private void ToggleMax()
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void SyncMaxGlyph()
	{
		bool flag = base.WindowState == WindowState.Maximized;
		MaxBtn.Content = (flag ? "\ue923" : "\ue922");
		MaxBtn.ToolTip = I18n.L(flag ? "还原" : "最大化");
		System.Windows.Automation.AutomationProperties.SetName(MaxBtn, I18n.L(flag ? "还原" : "最大化"));
        // DWM owns the outer edge and corner; an additional WPF stroke creates a visible seam at fractional DPI.
		RootBorder.BorderThickness = new Thickness(0);
        RootBorder.Clip = null;
        WinVer.Corner(new WindowInteropHelper(this).Handle, round: !flag);
	}

	internal void SyncThemeIndicator()
	{
		ThemeBtn.Content = (App.IsDark ? "\ue706" : "\ue708");
	}

	internal void ApplyThemeState(bool dark, bool persistLocal = true)
	{
		App.ApplyTheme(dark);
		if (persistLocal)
		{
			SaveUiPrefs();
		}
	}

	internal void SetDark(bool dark, bool persistBackend = true)
	{
		if (dark != App.IsDark)
		{
			ApplyThemeState(dark);
		}
		else
		{
			SyncThemeIndicator();
		}
		if (persistBackend)
		{
			Run(() => PushDarkAsync(dark));
		}
	}

	private async Task PushDarkAsync(bool dark)
	{
		if (!_bridgeReady)
		{
			_pendingDark = dark;
			return;
		}
		try
		{
			await AppServices.Client.CallAsync<object>("save_settings", new
			{
				data = new Dictionary<string, object> { ["ui_dark"] = dark }
			});
		}
		catch (Exception ex)
		{
			Toast(I18n.L("深浅色没能保存到后端"), ex.Message, ToastKind.Warning);
		}
	}

	private void ToggleTheme()
	{
		SetDark(!App.IsDark);
		Motion.Pulse(ThemeBtn, 1.14);
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
		SnapCaption.Attach(this, MaxBtn);
		SyncMaxGlyph();
	}

	private void OnShortcut(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.F5)
		{
			if (_current != null)
			{
				Run(async delegate
				{
					await _current.RefreshAsync();
				});
			}
			e.Handled = true;
		}
		else
		{
			if (Keyboard.Modifiers != ModifierKeys.Control)
			{
				return;
			}
			int num = e.Key switch
			{
				Key.D1 => 0, 
				Key.D2 => 1, 
				Key.D3 => 2, 
				Key.D4 => 3, 
				Key.D5 => 4, 
				Key.D6 => 5, 
				Key.D7 => 6, 
				Key.D8 => 7, 
				Key.D9 => 8, 
				_ => -1, 
			};
			if (num >= 0)
			{
				List<string> list = VisibleFlatOrder();
				if (num < list.Count)
				{
					Navigate(list[num]);
				}
				e.Handled = true;
			}
			else if (e.Key == Key.OemComma)
			{
				Navigate("settings");
				e.Handled = true;
			}
		}
	}

	private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == 0x001A || msg == 0x031A || msg == 0x031E)
		{
			Dispatcher.BeginInvoke(new Action(() => Backdrop.Refresh(this)));
		}
		if (msg == 561)
		{
			Backdrop.PmSetMoving(hwnd, moving: true);
		}
		if (msg == 562)
		{
			Backdrop.PmSetMoving(hwnd, moving: false);
		}
		if (msg != 36)
		{
			return IntPtr.Zero;
		}
		MINMAXINFO structure = Marshal.PtrToStructure<MINMAXINFO>(lParam);
		IntPtr intPtr = MonitorFromWindow(hwnd, 2);
		if (intPtr != IntPtr.Zero)
		{
			MONITORINFO info = new MONITORINFO
			{
				cbSize = Marshal.SizeOf<MONITORINFO>()
			};
			if (GetMonitorInfo(intPtr, ref info))
			{
				RECT rcWork = info.rcWork;
				RECT rcMonitor = info.rcMonitor;
				structure.ptMaxPosition.x = Math.Abs(rcWork.left - rcMonitor.left);
				structure.ptMaxPosition.y = Math.Abs(rcWork.top - rcMonitor.top);
				structure.ptMaxSize.x = Math.Abs(rcWork.right - rcWork.left);
				structure.ptMaxSize.y = Math.Abs(rcWork.bottom - rcWork.top);
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				structure.ptMinTrackSize.x = (int)Math.Ceiling(MinWidth * dpi.DpiScaleX);
				structure.ptMinTrackSize.y = (int)Math.Ceiling(MinHeight * dpi.DpiScaleY);
				Marshal.StructureToPtr(structure, lParam, fDeleteOld: true);
				handled = true;
			}
		}
		return IntPtr.Zero;
	}

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

	private void ShowDropVeil(bool on)
	{
		if (!_bridgeReady)
		{
			on = false;
		}
		if (on == (DropVeil.Visibility == Visibility.Visible))
		{
			return;
		}
		if (on)
		{
			DropVeil.Visibility = Visibility.Visible;
			Motion.Fade(DropVeil, 1.0, 120.0);
		}
		else
		{
			Motion.FadeOut(DropVeil, 110.0, delegate
			{
				DropVeil.Visibility = Visibility.Collapsed;
			});
		}
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = ((_bridgeReady && e.Data.GetDataPresent(DataFormats.FileDrop)) ? DragDropEffects.Copy : DragDropEffects.None);
		e.Handled = true;
	}

	private void OnDrop(object sender, DragEventArgs e)
	{
		e.Handled = true;
		if (!_bridgeReady || !e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			return;
		}
		object data = e.Data.GetData(DataFormats.FileDrop);
		string[] paths = data as string[];
		if (paths != null && paths.Length != 0)
		{
			Activate();
			Run(() => FileDrop.HandleAsync(this, paths));
		}
	}

	public void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
	{
		if (!base.Dispatcher.CheckAccess())
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				Toast(title, body, kind);
			});
			return;
		}
		DispatcherTimer timer = null;
		while (ToastHost.Children.Count >= 4)
		{
			ToastHost.Children.RemoveAt(0);
		}
		// 反编译产物原先把元组装箱后用指针读取，读到的是引用本身，导致 Glyph 收到 "(, B.Danger)" 而崩溃
		var (item, item2) = kind switch
		{
			ToastKind.Success => ("\uec61", "B.Ok"), 
			ToastKind.Warning => ("\ue7ba", "B.Warn"), 
			ToastKind.Error => ("\uea39", "B.Danger"), 
			_ => ("\ue946", "B.Info"), 
		};
		Border border = new Border
		{
			Width = 3.0,
			CornerRadius = new CornerRadius(2.0)
		};
		border.SetResourceReference(Border.BackgroundProperty, item2);
		// 错误信息往往是栈回溯，先留第一行结论再截断，别把“哪里出错”截没
		string bodyText = body ?? "";
		if (kind == ToastKind.Error && bodyText.Length > 220)
		{
			int num3 = bodyText.IndexOf('\n');
			string body3 = (num3 > 0) ? bodyText.Substring(0, num3) : bodyText;
			bodyText = ((body3.Length > 220) ? (body3.Substring(0, 219) + "…") : (body3 + "…"));
		}
		else if (bodyText.Length > 220)
		{
			bodyText = bodyText.Substring(0, 219) + "…";
		}
		SPanel sPanel = Ui.V(2.0, Ui.Txt(title, 13.0, bold: true).Wrap(), string.IsNullOrWhiteSpace(bodyText) ? null : Ui.Muted(bodyText));
		sPanel.MaxWidth = 300.0;
		if (kind == ToastKind.Error && body != null && body.Length > 220)
		{
			sPanel.ToolTip = body;
		}
        SPanel message = Ui.H(10.0, border, Ui.Glyph(item, 15.0, item2), sPanel);
		Border card = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			Padding = new Thickness(12.0, 10.0, 14.0, 11.0),
			BorderThickness = new Thickness(1.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0),
			Cursor = Cursors.Hand
		};
		var surface = new PopupShadow { Child = card, CornerRadius = card.CornerRadius, Margin = card.Margin };
		card.Margin = new Thickness(0);
		if (kind == ToastKind.Error)
		{
			card.Cursor = Cursors.Arrow;
			card.Child = Ui.G(null, "*,Auto").Add(message.VCenter(), 0)
				.Add(Ui.IconBtn("\ue711", I18n.L("关闭"), delegate
				{
					RemoveToast();
				}, 11.0).M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		}
        else card.Child = message;
		card.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		card.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		ToastHost.Children.Add(surface);
		Motion.SlideIn(surface);
		if (kind != ToastKind.Error)
		{
			timer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(4)
			};
			timer.Tick += delegate
			{
				timer.Stop();
				RemoveToast();
			};
			timer.Start();
			card.MouseLeftButtonUp += delegate
			{
				RemoveToast();
			};
			card.MouseEnter += delegate
			{
				timer.Stop();
			};
			card.MouseLeave += delegate
			{
				timer.Start();
			};
		}
		void RemoveToast()
		{
			timer?.Stop();
			if (ToastHost.Children.Contains(surface))
			{
				Motion.SlideOut(surface, delegate
				{
					ToastHost.Children.Remove(surface);
				});
			}
		}
	}
	private void BuildDock()
	{
		_dockTitle = Ui.Txt("", 12.5, bold: true).Trim();
		_dockStatus = Ui.Small("");
		_dockBar = Ui.Prog();
		_dockBar.Height = 4.0;
		Button button = Ui.Btn(I18n.L("查看"), BtnKind.Soft, delegate
		{
			Navigate("tasks");
		});
		button.Padding = new Thickness(11.0, 4.0, 11.0, 5.0);
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(Ui.Glyph("\ue896", 15.0, "B.Accent").M(0.0, 0.0, 10.0), 0);
		grid.Add(Ui.V(3.0, _dockTitle, _dockStatus, _dockBar).VCenter(), 0, 1);
		grid.Add(button.M(12.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		_dockCard = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(14.0, 10.0, 12.0, 11.0),
			BorderThickness = new Thickness(1.0),
			Child = grid
		};
		_dockCard.SetResourceReference(Border.BackgroundProperty, "B.Paper");
		_dockCard.SetResourceReference(Border.BorderBrushProperty, "B.Line");
		DockHost.Child = new PopupShadow { Child = _dockCard, CornerRadius = _dockCard.CornerRadius };
	}

    internal void RefreshTaskDock() => SyncDock();
	private void SyncDock()
	{
        string visibleTask = PageHost.Content is VersionPage installer ? installer.VisibleInstallTask : "";
		TaskRow taskRow = TaskStore.Rows.LastOrDefault((TaskRow r) => !r.Finished && r.Id != visibleTask && IsDownloadTitle(r.Title));
		if (taskRow == null || !(_currentId != "tasks"))
		{
			if (DockHost.Visibility == Visibility.Visible)
			{
				Motion.FadeOut(DockHost, 140.0, delegate
				{
					DockHost.Visibility = Visibility.Collapsed;
				});
			}
			return;
		}
		if (DockHost.Visibility != Visibility.Visible)
		{
			DockHost.Visibility = Visibility.Visible;
			Motion.FadeIn(DockHost, 220.0, 14.0);
		}
		_dockTitle.Text = taskRow.Title;
		_dockStatus.Text = (string.IsNullOrEmpty(taskRow.Speed) ? taskRow.Status : (taskRow.Status + "    " + taskRow.Speed));
		_dockBar.IsIndeterminate = taskRow.Indeterminate;
		if (!taskRow.Indeterminate)
		{
			Motion.Progress(_dockBar, taskRow.Progress);
		}
	}

	private bool IsDownloadTitle(string title)
	{
		if (string.IsNullOrEmpty(title))
		{
			return true;
		}
		if (_downloadTitle.TryGetValue(title, out var value))
		{
			return value;
		}
		_downloadTitle[title] = true;
		Run(async delegate
		{
			bool flag = await AppServices.Client.TryCallAsync("is_download_title", new { title }, fallback: true);
			if (!_downloadTitle.TryGetValue(title, out var value2) || value2 != flag)
			{
				_downloadTitle[title] = flag;
				SyncDock();
			}
		});
		return true;
	}

	public void FlyToTasks(FrameworkElement source, string text = "", string? colorKey = null)
	{
		if (!Motion.Enabled || !base.IsLoaded)
		{
			return;
		}
		NavRow value;
		ToggleButton target = (_navRows.TryGetValue("tasks", out value) ? value.Button : null);
		if (target == null || !source.IsVisible || !target.IsVisible)
		{
			return;
		}
		Point point;
		Point point2;
		try
		{
			point = source.TransformToAncestor(this).Transform(new Point(source.ActualWidth / 2.0, source.ActualHeight / 2.0));
			point2 = target.TransformToAncestor(this).Transform(new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0));
		}
		catch
		{
			return;
		}
		Border chip = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(10.0, 4.0, 10.0, 5.0),
			Child = Ui.Txt(string.IsNullOrEmpty(text) ? I18n.L("已加入队列") : text, 11.5, bold: true, "B.OnAccentStrong")
		};
		chip.SetResourceReference(Border.BackgroundProperty, colorKey ?? "B.AccentStrong");
		FlyHost.Children.Add(chip);
		chip.Measure(new Size(500.0, 60.0));
		double width = chip.DesiredSize.Width;
		double height = chip.DesiredSize.Height;
		Canvas.SetLeft(chip, point.X - width / 2.0);
		Canvas.SetTop(chip, point.Y - height / 2.0);
		TranslateTransform translateTransform = new TranslateTransform();
		ScaleTransform scaleTransform = new ScaleTransform(0.7, 0.7);
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scaleTransform);
		transformGroup.Children.Add(translateTransform);
		chip.RenderTransform = transformGroup;
		chip.RenderTransformOrigin = new Point(0.5, 0.5);
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(620.0);
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		DoubleAnimation animation = new DoubleAnimation(0.0, point2.X - point.X, timeSpan)
		{
			EasingFunction = easingFunction
		};
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
		{
			Duration = timeSpan
		};
		double num = point2.Y - point.Y;
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(num * 0.28 - 46.0, KeyTime.FromPercent(0.45), new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		}));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(num, KeyTime.FromPercent(1.0), new CubicEase
		{
			EasingMode = EasingMode.EaseIn
		}));
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames2 = new DoubleAnimationUsingKeyFrames
		{
			Duration = timeSpan
		};
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.15)));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.72)));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
		DoubleAnimation doubleAnimation = new DoubleAnimation(0.7, 0.42, timeSpan)
		{
			EasingFunction = easingFunction
		};
		doubleAnimation.Completed += delegate
		{
			FlyHost.Children.Remove(chip);
			if (target != null)
			{
				Motion.Pulse(target);
			}
		};
		translateTransform.BeginAnimation(TranslateTransform.XProperty, animation);
		translateTransform.BeginAnimation(TranslateTransform.YProperty, doubleAnimationUsingKeyFrames);
		chip.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames2);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, doubleAnimation);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimation.Clone());
	}

	private void LoadUiPrefs()
	{
		try
		{
			_prefs = (File.Exists(PrefsPath) ? (JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(PrefsPath)) ?? new UiPrefs()) : new UiPrefs());
		}
		catch
		{
			_prefs = new UiPrefs();
		}
		foreach (string item in _prefs.Expanded.Keys.Where((string k) => k.StartsWith('#')).ToList())
		{
			_prefs.Expanded[item.Substring(1)] = _prefs.Expanded[item];
			_prefs.Expanded.Remove(item);
		}
		_prefs.Expanded.TryAdd("download", value: false);
		_prefs.Expanded.TryAdd("more", value: false);
		SideSkin.Adopt(_prefs.SideWidth);
        SideSkin.SetCollapsed(_prefs.SideCollapsed);
		Motion.Enabled = _prefs.Anim;
		Motion.Scale = Clamp.Of(_prefs.AnimScale, 0.4, 2.2);
		ApplyThemeState(_prefs.Dark, persistLocal: false);
	}

	public void SaveUiPrefs()
	{
		// Collapsing the rail must not overwrite the user's expanded width.
		_prefs.SideWidth = SideSkin.ExpandedWidth;
		_prefs.Dark = App.IsDark;
		_prefs.Anim = Motion.UserEnabled;
		_prefs.AnimScale = Motion.Scale;
		try
		{
			File.WriteAllText(PrefsPath, JsonSerializer.Serialize(_prefs, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
        catch (Exception ex)
        {
            Smoke.Note("ui-preferences", ex.ToString());
            Toast(I18n.L("界面偏好未能保存"), ex.Message, ToastKind.Warning);
        }
	}

	public async Task LoadNavFromBridgeAsync()
	{
		JsonElement settings;
		try
		{
			settings = await AppServices.Client.CallAsync("get_settings");
		}
		catch (Exception ex)
		{
			Toast(I18n.L("侧栏设置读取失败"), ex.Message, ToastKind.Warning);
			return;
		}
		ApplyNavSettings(settings);
	}

	public void ApplyNavSettings(JsonElement settings)
	{
		_navCfg = HelL.NavConfig.FromJson(settings);
		int sidebarWidth = _navCfg.SidebarWidth;
		if (sidebarWidth >= 140 && sidebarWidth <= 320)
		{
			SideSkin.Adopt(_navCfg.SidebarWidth);
			_prefs.SideWidth = _navCfg.SidebarWidth;
		}
		BuildNav();
	}

	private async Task ApplyNavPatchAsync(IReadOnlyDictionary<string, object?> patch)
	{
		_navCfg = _navCfg.With(patch);
		BuildNav();
		if (!AppServices.Ready)
		{
			return;
		}
		try
		{
			await AppServices.Client.CallAsync<object>("update_settings", new
			{
				settings = NavModel.ToRpcArgs(patch)
			});
		}
		catch (Exception ex)
		{
			Toast(I18n.L("侧栏设置未能保存"), ex.Message, ToastKind.Warning);
		}
	}

	public void ResetNavLayout()
	{
		Run(() => ApplyNavPatchAsync(NavModel.DefaultNavPatch()));
	}

	public void SetNavStyle(string style)
	{
		if (NavModel.StyleLabels.ContainsKey(style) && !(style == NavModel.NavStyle(_navCfg)))
		{
			Run(() => ApplyNavPatchAsync(new Dictionary<string, object> { ["ui_nav_style"] = style }));
		}
	}

	private void SideGrip_DragDelta(object sender, DragDeltaEventArgs e)
	{
		SideSkin.Resize(e.HorizontalChange);
		_prefs.SideWidth = SideSkin.VisibleWidth;
	}

	private void SideGrip_DragCompleted(object sender, DragCompletedEventArgs e)
	{
		int w = (int)Math.Round(SideSkin.VisibleWidth);
		SaveUiPrefs();
		if (w == _navCfg.SidebarWidth)
		{
			return;
		}
		_navCfg.SidebarWidth = w;
		if (!AppServices.Ready)
		{
			return;
		}
		Run(async delegate
		{
			try
			{
				await AppServices.Client.CallAsync<object>("update_settings", new
				{
					settings = new Dictionary<string, object> { ["ui_sidebar_width"] = w }
				});
			}
			catch
			{
			}
		});
	}

	private void BuildNav()
	{
        if (_navigationMenu != null) _navigationMenu.IsOpen = false;
        SyncNavigationToggle();
		NavHost.Children.Clear();
		PinnedHost.Children.Clear();
		_navRows.Clear();
		_groupPanels.Clear();
		_taskBadge = null;
		_taskBadgeText = null;
		Dictionary<string, List<string>> dictionary = NavModel.SectionMembersFromConfig(_navCfg);
		Panel panel = NavHost;
		foreach (NavEntry item in NavModel.NavItemsFromConfig(_navCfg))
		{
			switch (item.Kind)
			{
			case NavEntryKind.Header:
				panel.Children.Add(_prefs.SideCollapsed ? Ui.Sep().M(18, 10, 18, 6) : MakeHeader(item.Label));
				break;
			case NavEntryKind.Stretch:
				PinnedHost.Children.Add(Ui.Sep().M(14.0, 0.0, 14.0, 6.0));
				panel = PinnedHost;
				break;
			case NavEntryKind.Item:
				if (NavModel.IsSection(item.Key))
				{
					panel.Children.Add(BuildGroup(item.Key, item.Label, dictionary[item.Key]));
				}
				else
				{
					panel.Children.Add(MakeRow(item.Key, item.Label, null, draggable: true));
				}
				break;
			}
		}
		SideFooter.Visibility = ((PinnedHost.Children.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		SyncNavSelection();
		SyncTasks();
		SideSkin.Apply();
	}

	private static UIElement MakeHeader(string label)
	{
        return Ui.Txt(label, 12, false, "B.InkMuted").M(22, 16, 12, 6);
	}

	private UIElement BuildGroup(string sec, string label, List<string> memberKeys)
	{
        if (_prefs.SideCollapsed) return BuildCompactGroup(sec, label, memberKeys);
		bool value;
		bool flag = _prefs.Expanded.TryGetValue(sec, out value) && value;
		TextBlock textBlock = Ui.Glyph("\ue76c", 10.0, "B.InkFaint");
		RotateTransform rot = new RotateTransform(flag ? 90 : 0);
		textBlock.RenderTransform = rot;
		textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
		ToggleButton header = MakeRow(sec, label, null, draggable: true, textBlock);
		StackPanel panel = new StackPanel
		{
			ClipToBounds = true
		};
		_groupPanels[sec] = panel;
		foreach (string memberKey in memberKeys)
		{
			panel.Children.Add(MakeRow(memberKey, NavModel.NavLabel(memberKey), sec, draggable: true));
		}
		panel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		header.Click += delegate
		{
			header.IsChecked = false;
			SyncNavSelection();
			bool flag2 = panel.Visibility != Visibility.Visible;
			_prefs.Expanded[sec] = flag2;
			SyncNavSelection();
			SaveUiPrefs();
			DoubleAnimation animation = new DoubleAnimation(flag2 ? 90 : 0, TimeSpan.FromMilliseconds((!Motion.Enabled) ? 1 : 180))
			{
				EasingFunction = new CubicEase()
			};
			rot.BeginAnimation(RotateTransform.AngleProperty, animation);
			if (flag2)
			{
				panel.Visibility = Visibility.Visible;
				panel.BeginAnimation(FrameworkElement.HeightProperty, null);
				panel.Height = double.NaN;
				panel.Measure(new Size(Math.Max(0.0, SideSkin.VisibleWidth - SideSkin.PadX * 2.0), double.PositiveInfinity));
				double height = panel.DesiredSize.Height;
				panel.Height = 0.0;
				Motion.AnimateHeight(panel, height, 200.0, delegate
				{
					panel.Height = double.NaN;
				});
			}
			else
			{
				panel.Height = panel.ActualHeight;
				Motion.AnimateHeight(panel, 0.0, 170.0, delegate
				{
					panel.Visibility = Visibility.Collapsed;
					panel.Height = double.NaN;
				});
			}
		};
		return new StackPanel
		{
			Tag = sec,
			Children = 
			{
				(UIElement)header,
				(UIElement)panel
			}
		};
	}

	private ToggleButton Restyle(ToggleButton btn)
	{
		btn.Style = Ui.S(_prefs.SideCollapsed ? "NavItem.Icon" : Glass.IsMac ? "NavItem.Capsule" : "NavItem");
		return btn;
	}

	private ToggleButton MakeRow(string key, string label, string? section, bool draggable, FrameworkElement? trailing = null)
	{
		bool flag = section != null;
		TextBlock textBlock = Ui.Glyph(NavGlyphs.GetValueOrDefault(key, "\ue80a"));
		TextBlock textBlock2 = Ui.Txt(label).VCenter();
		if (flag) textBlock2.FontSize = 13;
		textBlock2.TextTrimming = TextTrimming.CharacterEllipsis;
		Grid grid = Ui.G(null, "18,*,Auto");
		grid.Add(textBlock, 0);
		grid.Add(textBlock2.M(10.0, 0.0, 0.0, 0.0), 0, 1);
		if (key == "tasks")
		{
			_taskBadgeText = new TextBlock
			{
				Text = "0",
				FontSize = 11.0,
				FontWeight = FontWeights.SemiBold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			_taskBadge = new Border
			{
				CornerRadius = new CornerRadius(8.5),
				MinWidth = 18.0,
				Height = 17.0,
				Padding = new Thickness(5.0, 0.0, 5.0, 1.0),
				Visibility = Visibility.Collapsed,
				VerticalAlignment = VerticalAlignment.Center,
				Child = _taskBadgeText
			};
			_taskBadge.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
			_taskBadgeText.SetResourceReference(TextBlock.ForegroundProperty, "B.OnAccentStrong");
			grid.Add(_taskBadge, 0, 2);
		}
		else if (trailing != null)
		{
			grid.Add(trailing.VCenter(), 0, 2);
		}
		bool mac = Glass.IsMac;
		ToggleButton btn = new ToggleButton
		{
			Style = Ui.S(_prefs.SideCollapsed ? "NavItem.Icon" : mac ? "NavItem.Capsule" : "NavItem"),
			Content = grid,
			Tag = key,
			Height = _prefs.SideCollapsed ? 46 : flag ? 34 : 40,
			Margin = new Thickness(!_prefs.SideCollapsed && flag ? (mac ? 10 : 12) : 0, 0.0, 0.0, 0.0),
			HorizontalContentAlignment = HorizontalAlignment.Stretch
		};
        System.Windows.Automation.AutomationProperties.SetName(btn, label);
        btn.ToolTip = label;
        if (_prefs.SideCollapsed)
        {
            grid.Children.Remove(textBlock2);
            if (trailing != null) grid.Children.Remove(trailing);
            grid.ColumnDefinitions.Clear();
            textBlock.FontSize = 18;
            textBlock.HorizontalAlignment = HorizontalAlignment.Center;
            if (key == "tasks" && _taskBadge != null)
            {
                Grid.SetColumn(_taskBadge, 0);
                _taskBadge.HorizontalAlignment = HorizontalAlignment.Right;
                _taskBadge.VerticalAlignment = VerticalAlignment.Top;
                _taskBadge.Margin = new Thickness(0, -4, -5, 0);
            }
        }
		if (flag)
		{
			textBlock.Opacity = 0.85;
		}
		NavRow navRow = new NavRow
		{
			Key = key,
			Button = btn,
			Section = section
		};
		if (!navRow.IsSectionHeader)
		{
			btn.Click += delegate
			{
				btn.IsChecked = true;
				if (NavModel.PageForNavKey.TryGetValue(key, out string value))
				{
					Navigate(value);
				}
			};
		}
		btn.ContextMenu = RowMenu(navRow);
		if (draggable)
		{
			AttachDrag(navRow);
		}
		_navRows[key] = navRow;
		return btn;
	}

	private ContextMenu RowMenu(NavRow row)
	{
		ContextMenu contextMenu = new ContextMenu();
		bool flag = NavModel.NavStyle(_navCfg) == "grouped";
		string title = NavModel.NavLabel(row.Key);
		if (row.Section != null)
		{
			Item(contextMenu, I18n.L("固定到侧栏"), delegate
			{
				NavModel.PinResult res2 = NavModel.PinNavConfig(_navCfg, row.Key);
				if ((object)res2 != null)
				{
					if (res2.Error != null)
					{
						Toast(I18n.L("无法移出"), res2.Error, ToastKind.Warning);
					}
					else
					{
						Run(() => ApplyNavPatchAsync(res2.Patch));
					}
				}
			});
			foreach (string other in NavModel.SectionIds.Where((string s) => s != row.Section))
			{
				Item(contextMenu, I18n.L("移到「{0}」分区", NavModel.NavLabel(other)), delegate
				{
					MoveMember(row.Key, row.Section, other, -1);
				});
			}
		}
		else if (NavModel.AllSubKeys.Contains(row.Key))
		{
			foreach (string sec in NavModel.VisibleSections(_navCfg))
			{
				Item(contextMenu, I18n.L("放回「{0}」分区", NavModel.NavLabel(sec)), delegate
				{
					(Dictionary<string, object?> Patch, string Section)? res = NavModel.UnpinNavConfig(_navCfg, row.Key, sec);
					if (res.HasValue)
					{
						Run(() => ApplyNavPatchAsync(res.Value.Patch));
						Toast(I18n.L("已取消固定"), I18n.L("「{0}」放回了「{1}」", title, NavModel.NavLabel(res.Value.Section)));
					}
				});
			}
		}
		if (row.Section == null && (NavModel.IsTop(row.Key) || flag))
		{
			Item(contextMenu, row.IsSectionHeader ? I18n.L("隐藏此分区") : I18n.L("隐藏此入口"), delegate
			{
				List<string> hidden = _navCfg.Hidden.Where((string k) => k != row.Key).ToList();
				hidden.Add(row.Key);
				Run(() => ApplyNavPatchAsync(new Dictionary<string, object> { ["ui_nav_hidden"] = hidden }));
				if (PageOf(row.Key) == _currentId)
				{
					Navigate("launch");
				}
				Toast(I18n.L("已隐藏"), I18n.L("「{0}」可在侧栏右键菜单或设置里恢复", title));
			});
		}
		if (_navCfg.Hidden.Count > 0)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = I18n.L("恢复隐藏的入口"),
				Icon = Ui.Glyph("\ue7b3", 13.0)
			};
			foreach (string item in _navCfg.Hidden.Where((string k) => NavModel.IsTop(k) || NavModel.AllSubKeys.Contains(k)))
			{
				string key = item;
				Item(menuItem, NavModel.NavLabel(key), delegate
				{
					ShowNavItem(key, visible: true);
				});
			}
			if (menuItem.Items.Count > 0)
			{
				contextMenu.Items.Add(menuItem);
			}
		}
		contextMenu.Items.Add(new Separator());
		if (row.IsSectionHeader)
		{
			Item(contextMenu, I18n.L("全部展开 / 收起"), delegate
			{
				bool value = !_prefs.Expanded.GetValueOrDefault(row.Key);
				string[] sectionIds = NavModel.SectionIds;
				foreach (string key2 in sectionIds)
				{
					_prefs.Expanded[key2] = value;
				}
				SaveUiPrefs();
				BuildNav();
			});
		}
		string otherStyle = (flag ? "compact" : "grouped");
		Item(contextMenu, I18n.L("切换排法：") + NavModel.StyleLabels[otherStyle], delegate
		{
			SetNavStyle(otherStyle);
		});
		MenuItem frost = new MenuItem
		{
			Header = I18n.L("侧栏毛玻璃"),
			IsCheckable = true,
			IsChecked = SideFrost.Enabled,
			Icon = Ui.Glyph("\ue790", 13.0)
		};
		frost.Click += delegate
		{
			SideFrost.SetEnabled(frost.IsChecked);
			_prefs.SideFrost = frost.IsChecked;
			SaveUiPrefs();
		};
		contextMenu.Items.Add(frost);
		Item(contextMenu, I18n.L("恢复默认侧栏"), ResetNavLayout);
		return contextMenu;
	}

	private static void Item(ItemsControl menu, string header, Action act)
	{
		MenuItem menuItem = new MenuItem
		{
			Header = header
		};
		string text = MenuGlyph(header);
		if (text == "\ued1a")
		{
			menuItem.Icon = Lucide.Icon("M9.88 9.88a3 3 0 1 0 4.24 4.24 M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68 M6.61 6.61A13.53 13.53 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61 M2 2l20 20", 13.0, "B.InkSoft");
		}
		else if (text != null)
		{
			menuItem.Icon = Ui.Glyph(text, 13.0);
		}
		menuItem.Click += delegate
		{
			act();
		};
		menu.Items.Add(menuItem);
	}

	private static string? MenuGlyph(string header)
	{
		if (header == I18n.L("固定到侧栏"))
		{
			return "\ue718";
		}
		if (header == I18n.L("隐藏此分区") || header == I18n.L("隐藏此入口"))
		{
			return "\ued1a";
		}
		if (header == I18n.L("全部展开 / 收起"))
		{
			return "\ue70d";
		}
		if (header.StartsWith(I18n.L("切换排法："), StringComparison.Ordinal))
		{
			return "\ue8a9";
		}
		if (header == I18n.L("恢复默认侧栏"))
		{
			return "\ue72c";
		}
		string[] sectionIds = NavModel.SectionIds;
		for (int i = 0; i < sectionIds.Length; i++)
		{
			string text = NavModel.NavLabel(sectionIds[i]);
			if (header == I18n.L("移到「{0}」分区", text))
			{
				return "\ue8ab";
			}
			if (header == I18n.L("放回「{0}」分区", text))
			{
				return "\ue77a";
			}
		}
		foreach (KeyValuePair<string, string> navGlyph in NavGlyphs)
		{
			if (NavModel.NavLabel(navGlyph.Key) == header)
			{
				return navGlyph.Value;
			}
		}
		return null;
	}

	private static string? PageOf(string navKey)
	{
		if (!NavModel.PageForNavKey.TryGetValue(navKey, out string value))
		{
			return null;
		}
		return value;
	}

	private void MoveMember(string key, string from, string to, int index)
	{
		Dictionary<string, List<string>> members = NavModel.SectionMembersFromConfig(_navCfg);
		if (!members[from].Remove(key) && !members[to].Contains(key))
		{
			return;
		}
		members[to].Remove(key);
		List<string> list = members[to];
		list.Insert((index >= 0 && index <= list.Count) ? index : list.Count, key);
		if (members[from].Count == 0 && from != to)
		{
			Toast(I18n.L("无法移出"), I18n.L("该分区只剩这一个子页，移走会变空栏"), ToastKind.Warning);
			return;
		}
		_prefs.Expanded[to] = true;
		Run(() => ApplyNavPatchAsync(new Dictionary<string, object> { ["ui_section_members"] = members }));
	}

	public void ShowNavItem(string navKey, bool visible)
	{
		List<string> hidden = _navCfg.Hidden.Where((string k) => k != navKey).ToList();
		if (!visible)
		{
			hidden.Add(navKey);
		}
		Run(() => ApplyNavPatchAsync(new Dictionary<string, object> { ["ui_nav_hidden"] = hidden }));
	}

	public bool IsNavHidden(string navKey)
	{
		return _navCfg.Hidden.Contains(navKey);
	}

	private List<string> VisibleFlatOrder()
	{
		List<string> list = new List<string>();
		Dictionary<string, List<string>> dictionary = NavModel.SectionMembersFromConfig(_navCfg);
		foreach (NavEntry item in NavModel.NavItemsFromConfig(_navCfg))
		{
			if (item.Kind != NavEntryKind.Item)
			{
				continue;
			}
			if (NavModel.IsSection(item.Key))
			{
				foreach (string item2 in dictionary[item.Key])
				{
					string text = PageOf(item2);
					if (text != null && !list.Contains(text))
					{
						list.Add(text);
					}
				}
			}
			else
			{
				string text2 = PageOf(item.Key);
				if (text2 != null && !list.Contains(text2))
				{
					list.Add(text2);
				}
			}
		}
		return list;
	}

	private int NavOrderIndex(string pageId)
	{
		int num = VisibleFlatOrder().IndexOf(pageId);
		if (num >= 0)
		{
			return num;
		}
		return 99;
	}

	private void SyncNavSelection()
	{
		NavRow? active = _navRows.Values.FirstOrDefault(row => !row.IsSectionHeader && PageOf(row.Key) == _currentId);
		string? collapsedSection = active?.Section;
        if (_prefs.SideCollapsed && active == null)
            collapsedSection = NavModel.SectionMembersFromConfig(_navCfg).FirstOrDefault(pair => pair.Value.Any(key => PageOf(key) == _currentId)).Key;
		if (!_prefs.SideCollapsed && collapsedSection != null && _prefs.Expanded.GetValueOrDefault(collapsedSection)) collapsedSection = null;
		foreach (NavRow value in _navRows.Values)
		{
			value.Button.IsChecked = value == active || value.IsSectionHeader && value.Key == collapsedSection;
		}
	}

	private void SyncTasks()
	{
		int runningCount = TaskStore.RunningCount;
		if (_taskBadge != null && _taskBadgeText != null)
		{
			_taskBadgeText.Text = ((runningCount > 99) ? "99+" : runningCount.ToString());
			bool flag = runningCount > 0;
			if (flag && _taskBadge.Visibility != Visibility.Visible)
			{
				_taskBadge.Visibility = Visibility.Visible;
				Motion.Pulse(_taskBadge, 1.25);
			}
			else if (!flag)
			{
				_taskBadge.Visibility = Visibility.Collapsed;
			}
			SyncDock();
		}
		SideSkin.SyncRail();
	}

	private void AttachDrag(NavRow row)
	{
		ToggleButton btn = row.Button;
		btn.PreviewMouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			_dragStart = e.GetPosition(this);
			_dragRow = row;
			_dragging = false;
		};
		btn.PreviewMouseMove += delegate(object _, MouseEventArgs e)
		{
			if (_dragRow == row && e.LeftButton == MouseButtonState.Pressed)
			{
				Point position = e.GetPosition(this);
				if (!_dragging && (Math.Abs(position.Y - _dragStart.Y) > 14.0 || Math.Abs(position.X - _dragStart.X) > 20.0))
				{
					_dragging = true;
					btn.Opacity = 0.45;
					btn.CaptureMouse();
					if (_dropLine == null)
					{
						_dropLine = new Border
						{
							Height = 2.0,
							CornerRadius = new CornerRadius(1.0),
							Margin = new Thickness(14.0, 1.0, 14.0, 1.0)
						};
					}
					_dropLine.SetResourceReference(Border.BackgroundProperty, "B.Accent");
				}
				if (_dragging)
				{
					UpdateDrop(position);
				}
			}
		};
		btn.PreviewMouseLeftButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			if (_dragRow == row && _dragging)
			{
				btn.ReleaseMouseCapture();
				btn.Opacity = 1.0;
				RemoveDropLine();
				bool num = _dropTarget != null && _dropTarget.Key != row.Key;
				if (_dropTarget != null)
				{
					CommitDrop(row, _dropTarget, _dropBefore);
				}
				if (num)
				{
					e.Handled = true;
				}
			}
			_dragRow = null;
			_dragging = false;
			_dropTarget = null;
		};
	}

	private void RemoveDropLine()
	{
		if (_dropLine?.Parent is Panel panel)
		{
			panel.Children.Remove(_dropLine);
		}
	}

	private void UpdateDrop(Point mouse)
	{
		RemoveDropLine();
		_dropTarget = null;
		Point point = PointToScreen(mouse);
		foreach (NavRow value in _navRows.Values)
		{
			if (value == _dragRow || !value.Button.IsVisible)
			{
				continue;
			}
			Point point2 = value.Button.PointFromScreen(point);
			double actualHeight = value.Button.ActualHeight;
			if (!(point2.Y < -3.0) && !(point2.Y > actualHeight + 3.0))
			{
				_dropTarget = value;
				_dropBefore = point2.Y < actualHeight / 2.0;
				if (value.Button.Parent is Panel panel)
				{
					int val = panel.Children.IndexOf(value.Button) + ((!_dropBefore) ? 1 : 0);
					panel.Children.Insert(Math.Min(val, panel.Children.Count), _dropLine);
				}
				break;
			}
		}
	}

	private void CommitDrop(NavRow dragged, NavRow target, bool before)
	{
		if (dragged.Key == target.Key)
		{
			return;
		}
		Dictionary<string, object?> patch = null;
		if (dragged.Section != null)
		{
			if (target.Section != null)
			{
				Dictionary<string, List<string>> dictionary = NavModel.SectionMembersFromConfig(_navCfg);
				int num = dictionary[target.Section].IndexOf(target.Key);
				if (num >= 0)
				{
					if (dragged.Section == target.Section && dictionary[target.Section].IndexOf(dragged.Key) < num)
					{
						num--;
					}
					MoveMember(dragged.Key, dragged.Section, target.Section, num + ((!before) ? 1 : 0));
				}
				return;
			}
			if (target.IsSectionHeader)
			{
				if (target.Key != dragged.Section)
				{
					MoveMember(dragged.Key, dragged.Section, target.Key, -1);
				}
				return;
			}
			NavModel.PinResult pinResult = NavModel.PinNavConfig(_navCfg, dragged.Key, target.Key, before);
			if ((object)pinResult == null)
			{
				return;
			}
			if (pinResult.Error != null)
			{
				Toast(I18n.L("无法移出"), pinResult.Error, ToastKind.Warning);
				return;
			}
			patch = pinResult.Patch;
		}
		else if (NavModel.AllSubKeys.Contains(dragged.Key) && (target.IsSectionHeader || target.Section != null))
		{
			string text = target.Section ?? target.Key;
			int index = -1;
			if (target.Section != null)
			{
				int num2 = NavModel.SectionMembersFromConfig(_navCfg)[text].IndexOf(target.Key);
				index = ((num2 < 0) ? (-1) : (num2 + ((!before) ? 1 : 0)));
			}
			(Dictionary<string, object>, string)? tuple = NavModel.UnpinNavConfig(_navCfg, dragged.Key, text, index);
			if (!tuple.HasValue)
			{
				return;
			}
			patch = tuple.Value.Item1;
			_prefs.Expanded[tuple.Value.Item2] = true;
			Toast(I18n.L("已取消固定"), I18n.L("「{0}」放回了「{1}」", NavModel.NavLabel(dragged.Key), NavModel.NavLabel(tuple.Value.Item2)));
		}
		else if (target.Section == null)
		{
			patch = NavModel.ReorderNavConfig(_navCfg, dragged.Key, target.Key, before);
		}
		if (patch != null)
		{
			Run(() => ApplyNavPatchAsync(patch));
		}
	}

}
