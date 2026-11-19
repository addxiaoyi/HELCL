using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FileMemory = HelL.Core.Launcher.Ai.FileMemory;
using McpServer = HelL.Core.Launcher.Ai.McpServer;
using HelL.Models;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class AiPage : PageBase
{
	private readonly SPanel _chatList = Ui.V(4.0);

	private readonly SPanel _messages = Ui.V(12.0);

	private readonly SmoothScroll _scroll;

	private readonly ChatInput _input = new ChatInput();

	private readonly List<string> _pendingImages = new List<string>();
	private readonly ChatAttachments _imagePreviews = new();
	private bool _visionChecking;
	private bool _followOutput = true;
	private bool _scrollQueued;

	private readonly TextBlock _imgChip = new TextBlock();

	private readonly ComboBox _perm = Ui.Combo(new string[5]
	{
		I18n.L("每次确认"),
		I18n.L("写直接执行"),
		I18n.L("只看不动"),
		I18n.L("全自动"),
		I18n.L("自定义")
	}, null, 112.0);


	private readonly Button _send;

	private readonly Button _stop;

	private readonly Button _retry;

	private readonly Button _rewind;

	private readonly TextBlock _status = Ui.Small("");

	private readonly TextBlock _usageLabel = Ui.Small("");

	private bool _syncPerm;





	private readonly StringBuilder _stream = new StringBuilder();

	private readonly StringBuilder _round = new StringBuilder();

	private readonly Dictionary<string, ToolLine> _toolLines = new Dictionary<string, ToolLine>();

	private readonly Dictionary<string, TaskCard> _taskCards = new Dictionary<string, TaskCard>();

	private readonly Popup _slashPop = new Popup
	{
		Placement = PlacementMode.Top,
		StaysOpen = true,
		AllowsTransparency = true,
		Focusable = false
	};

	private readonly SPanel _slashRows = Ui.V(2.0);

	private List<SlashCommand> _slashItems = new List<SlashCommand>();

	private int _slashSel = -1;

	private readonly Border _mcpBadge;

	private readonly TextBlock _mcpText = Ui.Txt("", 11.5, bold: false, "B.InkMuted");

	private Bubble? _roundBubble;

	private AiStoreDto _store = new AiStoreDto();

	private bool _busy;

	private string _runChatId = "";

	private bool _abandoned;

	private TaskCompletionSource<object?>? _runEnded;

	private readonly Dictionary<string, (string Name, string ChatId)> _aiPendingTasks = new Dictionary<string, (string, string)>();

	private static readonly string[] PermissionModes = new string[5] { "default", "acceptEdits", "plan", "yolo", "custom" };

	private PlanCard? _planCard;

	private readonly Border _hist;

	private readonly TextBox _histSearch;

	private readonly TextBlock _titleText = Ui.Txt("", 14.0, bold: true, "B.Ink");

	private readonly Border _instPill;

	private readonly TextBlock _instText = Ui.Txt("", 11.5, bold: false, "B.AccentText");

	private readonly ContentControl _ringHost = new ContentControl
	{
		Width = 18.0,
		Height = 18.0
	};

	private readonly Border _composer;

	private readonly ContentControl _sendStop = new ContentControl();

	private readonly SPanel _dock = Ui.V(8.0);

	private readonly TextBlock _permTip = Ui.Txt("", 11.5, bold: false, "B.InkMuted");

	private readonly TextBlock _placeholder = Ui.Txt("", 13.5, bold: false, "B.InkFaint");

	private FrameworkElement? _emptyView;

	private StepGroup? _group;

	private bool _needWho = true;

	private Bubble? _lastUser;

	private ConfirmCard? _pendingConfirm;

	private long _contextWindow = 131072L;

	private bool _histOpen = true;
	private Dlg.Layer? _historyLayer;

	public override string Title => I18n.L("AI 助手");

	private AiChatDto? ActiveChat => _store.Chats.FirstOrDefault((AiChatDto c) => c.Id == _store.ActiveId) ?? _store.Chats.FirstOrDefault();

	private static (string Title, string Sub, string Icon, string Prompt)[] QuickStarts => new(string, string, string, string)[4]
	{
		(I18n.L("下个最新版"), I18n.L("装好最新正式版并建一个实例"), "M12 3v12 M6 11l6 6 6-6 M4 21h16", I18n.L("帮我下载最新正式版 Minecraft")),
		(I18n.L("装 Fabric + 钠"), I18n.L("一步配好 1.20.1 性能优化"), "M12 2l10 6-10 6L2 8z M2 14l10 6 10-6", I18n.L("给我装 1.20.1 Fabric，再装钠和 Iris 光影")),
		(I18n.L("崩溃分析"), I18n.L("读最近一次崩溃报告找原因"), "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8l6 6v12a2 2 0 0 1-2 2z M14 2v6h6 M8 13h8 M8 17h5", I18n.L("刚才启动失败了，读一下日志告诉我原因")),
		(I18n.L("扫模组冲突"), I18n.L("检查版本不兼容和重复模组"), "M3 7V5a2 2 0 0 1 2-2h2 M17 3h2a2 2 0 0 1 2 2v2 M21 17v2a2 2 0 0 1-2 2h-2 M7 21H5a2 2 0 0 1-2-2v-2 M7 12h10", I18n.L("扫一下当前实例的模组冲突"))
	};

	internal SPanel SelfTestMessages => _messages;

	internal SPanel SelfTestDock => _dock;

	internal ContentControl SelfTestSendStop => _sendStop;

	internal Button SelfTestSend => _send;

	internal Button SelfTestStop => _stop;

	internal TextBlock SelfTestPlaceholder => _placeholder;

	internal Border SelfTestHist => _hist;

	internal SPanel SelfTestChatList => _chatList;

	internal TextBlock SelfTestUsage => _usageLabel;

	internal ComboBox SelfTestPerm => _perm;

	internal TextBlock SelfTestPermTip => _permTip;

	public AiPage()
	{
		_send = new Button
		{
			Template = AiIcons.FlatButtonTemplate(),
			Width = 32.0,
			Height = 32.0,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			ToolTip = I18n.L("发送（Enter）"),
			Content = AiIcons.Icon("M12 19V5 M5 12l7-7 7 7", 16.0, "B.OnAccentStrong")
		};
		_send.SetResourceReference(Control.BackgroundProperty, "B.AccentStrong");
		_send.Click += delegate
		{
			PageBase.Run(SendAsync);
		};
		_stop = new Button
		{
			Template = AiIcons.FlatButtonTemplate(),
			Width = 32.0,
			Height = 32.0,
			Padding = new Thickness(0.0),
			Cursor = Cursors.Hand,
			ToolTip = I18n.L("停止生成"),
			Content = new Border
			{
				Width = 10.0,
				Height = 10.0,
				CornerRadius = new CornerRadius(2.0),
				Background = Brushes.White
			}
		};
		_stop.SetResourceReference(Control.BackgroundProperty, "B.Ink");
		((Border)_stop.Content).SetResourceReference(Border.BackgroundProperty, "B.Canvas");
		_stop.Click += delegate
		{
			PageBase.Run(StopAsync);
		};
		_retry = Ui.Btn(I18n.L("重试"), BtnKind.Chip, delegate
		{
			PageBase.Run(RetryAsync);
		}, "\ue72c");
		_rewind = Ui.Btn(I18n.L("撤回"), BtnKind.Chip, delegate
		{
			PageBase.Run(RewindAsync);
		}, "\ue72b");
		_stop.IsEnabled = false;
		_retry.IsEnabled = false;
		_perm.ToolTip = I18n.L("AI 权限等级");
		_perm.Width = 104.0;
		_perm.SelectionChanged += delegate
		{
			SyncPermTip();
			if (!_syncPerm)
			{
				PageBase.Run(SavePermissionModeAsync, I18n.L("权限设置保存失败"));
			}
		};
		_messages.MaxWidth = 760.0;
		_messages.HorizontalAlignment = HorizontalAlignment.Stretch;
		_messages.Margin = new Thickness(28.0, 18.0, 28.0, 12.0);
		_scroll = Ui.Scroll(new Grid
		{
			Children = { (UIElement)_messages }
		});
		_input.Submitted += delegate(string text)
		{
			SlashCommand cmd = AiChatOps.ExactSlash(text);
			if (cmd != null)
			{
				_input.Clear();
				RunSlash(cmd);
			}
			else
			{
				PageBase.Run(() => SendTextAsync(text));
			}
		};
		_input.KeyIntercept = SlashKey;
		Button button = Ui.Btn(I18n.L("新对话"), BtnKind.Ghost, delegate
		{
			PageBase.Run(NewChatAsync);
		}, "\ue710");
		button.HorizontalAlignment = HorizontalAlignment.Stretch;
		button.HorizontalContentAlignment = HorizontalAlignment.Left;
		_histSearch = Ui.Input(I18n.L("搜索对话"));
		_histSearch.TextChanged += delegate
		{
			RenderChats();
		};
		Grid grid = Ui.G("Auto,Auto,Auto,*");
		grid.Add(button, 0);
		grid.Add(_histSearch.M(0.0, 4.0), 1);
		grid.Add(Ui.Txt(I18n.L("最近"), 11.0, bold: false, "B.InkFaint").M(8.0, 12.0, 0.0, 4.0), 2);
		grid.Add(Ui.Scroll(_chatList), 3);
		_hist = new Border
		{
			Width = 192.0,
			Padding = new Thickness(10.0, 12.0, 10.0, 12.0),
			BorderThickness = new Thickness(0.0, 0.0, 1.0, 0.0),
			Child = grid
		};
		_hist.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
		_hist.SetResourceReference(Border.BackgroundProperty, "B.SurfaceInset");
		Button el = AiIcons.Flat("M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M9 3v18", null, I18n.L("显示 / 隐藏对话列表"), delegate
		{
			if (ActualWidth < 860)
			{
				OpenHistory();
				return;
			}
			_histOpen = !_histOpen;
			_hist.Visibility = ((!_histOpen) ? Visibility.Collapsed : Visibility.Visible);
		}, "B.InkMuted", 16.0);
		_titleText.TextTrimming = TextTrimming.CharacterEllipsis;
		_instPill = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(9.0, 3.0, 9.0, 3.0),
			Child = Ui.H(5.0, AiIcons.Icon("M21 8l-9-5-9 5v8l9 5 9-5z M3 8l9 5 9-5 M12 13v8", 12.0, "B.AccentText").VCenter(), _instText.VCenter(), Ui.Txt("▾", 10.0, bold: false, "B.AccentText").VCenter()),
			ToolTip = I18n.L("助手默认操作的实例，点击切换"),
			Cursor = Cursors.Hand,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		_instPill.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		_instPill.SetResourceReference(Border.BorderBrushProperty, "B.AccentSoft2");
		_instPill.MouseLeftButtonUp += delegate
		{
			PageBase.Run(OpenInstanceMenuAsync, I18n.L("读取实例失败"));
		};
		Border mcpDot = new Border
		{
			Width = 7.0,
			Height = 7.0,
			CornerRadius = new CornerRadius(3.5)
		};
		mcpDot.SetResourceReference(Border.BackgroundProperty, "B.Ok");
		_mcpText.TextTrimming = TextTrimming.CharacterEllipsis;
		_mcpBadge = new Border
		{
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			Child = Ui.H(5.0, mcpDot.VCenter(), _mcpText.VCenter()),
			Visibility = Visibility.Collapsed,
			Background = Brushes.Transparent
		};
		McpServer.StateChanged += delegate
		{
			base.Dispatcher.BeginInvoke((Action)SyncMcp);
		};
		McpServer.ToolCalled += delegate
		{
			base.Dispatcher.BeginInvoke((Action)SyncMcp);
		};
		_usageLabel.VerticalAlignment = VerticalAlignment.Center;
		_usageLabel.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		Button more = AiIcons.Flat("M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M19 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M5 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z", null, I18n.L("更多"), delegate
		{
		}, "B.InkMuted", 16.0);
		more.ContextMenu = MoreMenu();
		more.Click += delegate
		{
			more.ContextMenu.PlacementTarget = more;
			more.ContextMenu.IsOpen = true;
		};
		Grid grid2 = Ui.G(null, "Auto,*,Auto,Auto");
		grid2.Add(el.VCenter(), 0);
		grid2.Add(BuildModelHeader().M(8, 0, 12, 0), 0, 1);
        grid2.Add(Ui.Btn(I18n.L("拓展"), BtnKind.Ghost, (_, _) => OpenExtensions(), "\ue713").VCenter(), 0, 2);
		grid2.Add(more.VCenter(), 0, 3);
		Border border = new Border
		{
			MinHeight = 64.0,
			Padding = new Thickness(16.0, 8.0, 12.0, 8.0),
			Child = grid2
		};
		border.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
        border.BorderThickness = new Thickness(0, 0, 0, 1);
        border.SetResourceReference(Border.BackgroundProperty, "B.GlassStrong");
		_input.Style = Ui.S("Input.Composer");
		_input.BorderThickness = new Thickness(0.0);
		_input.Background = Brushes.Transparent;
		_input.Padding = new Thickness(2.0, 4.0, 2.0, 4.0);
		_input.FontSize = 14;
		_input.SetResourceReference(Control.ForegroundProperty, "B.Ink");
		_input.SetResourceReference(TextBoxBase.CaretBrushProperty, "B.Ink");
		_input.TextChanged += delegate
		{
			SyncPlaceholder();
			SyncSlash();
		};
		_placeholder.IsHitTestVisible = false;
		_placeholder.Style = Ui.S("T.Control");
		_placeholder.Margin = new Thickness(4.0, 4.0, 0.0, 0.0);
		_placeholder.VerticalAlignment = VerticalAlignment.Top;
		Grid grid3 = new Grid
		{
			Children = 
			{
				(UIElement)_input,
				(UIElement)_placeholder
			}
		};
		Button el2 = AiIcons.Flat("M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M16 8v5a3 3 0 0 0 6 0v-1a10 10 0 1 0-4 8", null, I18n.L("引用当前实例 / 最近崩溃日志"), delegate
		{
			InsertMention();
		}, "B.InkMuted", 15.0);
		Button el3 = AiIcons.Flat("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", null, I18n.L("按工具 / 目录细调 AI 能做什么"), delegate
		{
			PageBase.Run(OpenPermissionPanelAsync);
		}, "B.InkMuted", 15.0);
		Button elImg = AiIcons.Flat("M3 5h18v14H3z M8.5 8.5a1.5 1.5 0 1 0 0 3a1.5 1.5 0 1 0 0-3z M21 15l-5-5L5 19", null, I18n.L("附图片（也可以直接粘贴或拖进来）"), PickImage, "B.InkMuted", 15.0);
		SetupImageInput();
		_sendStop.Content = _send;
		SPanel composerTools = Ui.H(6, elImg, el2, _perm, el3, _imgChip, _mcpBadge, _ringHost, _usageLabel);
		_cloudControls = new FrameworkElement[] { elImg, el2, _perm, el3 };
		composerTools.Wrap = true;
		Grid grid4 = Ui.G(null, "*,Auto");
		grid4.Add(composerTools, 0);
		grid4.Add(_sendStop.M(10, 0, 0, 0).VCenter(), 0, 1);
		_composer = new Border
		{
			CornerRadius = new CornerRadius(8.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(16.0, 14.0, 12.0, 10.0),
			Child = Ui.V(6.0, _imagePreviews, grid3, grid4)
		};
		_composer.SetResourceReference(Border.BackgroundProperty, "B.Popup");
		_composer.SizeChanged += (_, _) => SyncDockHeight();
		_imagePreviews.Removed += path => { _pendingImages.Remove(path); SyncImageChip(); };
		_scroll.ScrollChanged += (_, e) =>
		{
			if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
				_followOutput = _scroll.ScrollableHeight - _scroll.VerticalOffset < 24;
		};
		HookImageDrop(_composer);
		_composer.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
		_input.GotKeyboardFocus += delegate
		{
			_composer.SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		};
		_input.LostKeyboardFocus += delegate
		{
			_composer.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
		};
		_status.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		_status.TextTrimming = TextTrimming.CharacterEllipsis;
		Grid grid5 = Ui.G(null, "Auto,*,Auto");
		grid5.Add(AiIcons.Icon("M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z", 12.0, "B.InkFaint").VCenter(), 0);
		grid5.Add(_permTip.M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid5.Add(_status.VCenter(), 0, 2);
		Border slashCard = new Border
		{
			CornerRadius = new CornerRadius(10.0),
			BorderThickness = new Thickness(1.0),
			Padding = new Thickness(4.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			Child = _slashRows
		};
		slashCard.SetResourceReference(Border.BackgroundProperty, "B.Popup");
		slashCard.SetResourceReference(Border.BorderBrushProperty, "B.PopupBorder");
		_slashPop.Child = slashCard;
		_slashPop.PlacementTarget = _composer;
		SPanel sPanel = Ui.V(8.0, _dock, _instPill, _composer, grid5.M(4.0, 0.0, 4.0));
		sPanel.MaxWidth = 760.0;
		sPanel.Margin = new Thickness(28.0, 4.0, 28.0, 14.0);
		Grid grid6 = Ui.G("Auto,*,Auto");
		grid6.Add(border, 0);
		grid6.Add(_scroll, 1);
		grid6.Add(sPanel, 2);
		ConfigureConversationLayout(grid6, sPanel);
		Grid grid7 = Ui.G(null, "Auto,*");
		grid7.Add(_hist, 0);
		grid7.Add(grid6, 0, 1);
		grid7.ClipToBounds = true;
		AttachWorkspace(grid7);
		SizeChanged += (_, _) =>
		{
			if (_historyLayer != null && ActualWidth >= 860) _historyLayer.Dismiss();
			if (_historyLayer == null) _hist.Visibility = _histOpen && ActualWidth >= 860 ? Visibility.Visible : Visibility.Collapsed;
		};
		SyncPlaceholder();
		SyncPermTip();
		SyncInstChip();
		SyncMcp();
	}

	private void OpenHistory()
	{
		if (_historyLayer != null || !(_hist.Parent is Grid parent)) return;
		parent.Children.Remove(_hist);
		_hist.Visibility = Visibility.Visible;
		_hist.Width = double.NaN;
		_hist.Height = 340;
		var holder = new ContentControl { Content = _hist };
		_historyLayer = Dlg.Panel(I18n.L("对话列表"), holder, 420, onClose: () =>
		{
			holder.Content = null;
			_hist.Width = 192;
			_hist.Height = double.NaN;
			parent.Children.Add(_hist);
			_historyLayer = null;
			_hist.Visibility = _histOpen && ActualWidth >= 860 ? Visibility.Visible : Visibility.Collapsed;
		});
	}

	private void SyncInstChip()
	{
		string text = PageBase.Win?.Prefs.CatalogInstance ?? "";
		_instText.Text = I18n.L("实例：{0}", (text.Length > 0) ? text : I18n.L("默认"));
	}

	private async Task OpenInstanceMenuAsync()
	{
		List<InstanceInfo> list = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		string current = PageBase.Win?.Prefs.CatalogInstance ?? "";
		ContextMenu menu = new ContextMenu
		{
			PlacementTarget = _instPill,
			Placement = PlacementMode.Top
		};
		if (list.Count == 0)
		{
			menu.Items.Add(new MenuItem
			{
				Header = I18n.L("还没有实例"),
				IsEnabled = false
			});
		}
		foreach (InstanceInfo item in list)
		{
			string name = item.Name;
			MenuItem mi = new MenuItem
			{
				Header = (string.IsNullOrEmpty(item.Mc) ? name : (name + "  ·  " + item.Mc)),
				IsCheckable = true,
				IsChecked = (name == current)
			};
			mi.Click += delegate
			{
				SetAssistantInstance(name);
			};
			menu.Items.Add(mi);
		}
		menu.IsOpen = true;
	}

	private void SetAssistantInstance(string name)
	{
		MainWindow win = PageBase.Win;
		if (win == null)
		{
			return;
		}
		win.Prefs.CatalogInstance = name ?? "";
		win.SaveUiPrefs();
		SyncInstChip();
		PageBase.Toast(I18n.L("已切换实例"), I18n.L("助手接下来默认操作「{0}」", name));
	}

	private void SyncMcp()
	{
		bool running = McpServer.Running;
		_mcpBadge.Visibility = ((!running) ? Visibility.Collapsed : Visibility.Visible);
		if (!running)
		{
			return;
		}
		string last = McpServer.LastTool;
		_mcpText.Text = (last.Length > 0) ? I18n.L("MCP 已开启 · {0} {1}", last, McpServer.LastToolAt.ToString("HH:mm")) : I18n.L("MCP 已开启");
		string tip = I18n.L("MCP Server 地址：{0}", McpServer.Url);
		if (last.Length > 0)
		{
			tip = tip + "\n" + I18n.L("最近一次外部调用：{0}（{1}）", last, McpServer.LastToolAt.ToString("yyyy-MM-dd HH:mm:ss"));
		}
		_mcpBadge.ToolTip = tip;
	}

	private void SyncSlash()
	{
		List<SlashCommand> items = AiChatOps.MatchSlash(_input.Text);
		if (items.Count == 0 || !_input.IsKeyboardFocusWithin)
		{
			CloseSlash();
			return;
		}
		_slashItems = items;
		_slashSel = 0;
		RenderSlash();
		_slashPop.Width = Math.Max(260.0, _composer.ActualWidth);
		_slashPop.HorizontalOffset = 0.0;
		_slashPop.IsOpen = true;
	}

	private void RenderSlash()
	{
		_slashRows.Children.Clear();
		for (int i = 0; i < _slashItems.Count; i++)
		{
			SlashCommand cmd = _slashItems[i];
			Grid grid = Ui.G(null, "Auto,*");
			grid.Add(Ui.Txt("/" + I18n.L(cmd.Name), 13.0, bold: true, "B.Ink").VCenter(), 0);
			grid.Add(Ui.Txt(I18n.L(cmd.Desc), 12.0, bold: false, "B.InkMuted").M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			Button row = new Button
			{
				Template = AiIcons.FlatButtonTemplate(),
				Content = grid,
				Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = Cursors.Hand,
				Focusable = true
			};
			if (i == _slashSel)
			{
				row.SetResourceReference(Control.BackgroundProperty, "B.AccentSoft");
			}
			else
			{
				row.Background = Brushes.Transparent;
			}
			row.Click += delegate
			{
				PickSlash(cmd);
			};
			_slashRows.Children.Add(row);
		}
	}

	private void CloseSlash()
	{
		_slashPop.IsOpen = false;
		_slashItems = new List<SlashCommand>();
		_slashSel = -1;
	}

	private bool SlashKey(Key key)
	{
		if (!_slashPop.IsOpen || _slashItems.Count == 0)
		{
			return false;
		}
		switch (key)
		{
		case Key.Up:
		case Key.Down:
			_slashSel = AiChatOps.MoveSelection(_slashSel, (key == Key.Down) ? 1 : (-1), _slashItems.Count);
			RenderSlash();
			return true;
		case Key.Return:
		case Key.Tab:
			if (_slashSel >= 0 && _slashSel < _slashItems.Count)
			{
				PickSlash(_slashItems[_slashSel]);
				return true;
			}
			return false;
		case Key.Escape:
			CloseSlash();
			return true;
		default:
			return false;
		}
	}

	private void PickSlash(SlashCommand cmd)
	{
		CloseSlash();
		_input.Clear();
		RunSlash(cmd);
		_input.Focus();
	}

	private void RunSlash(SlashCommand cmd)
	{
		switch (cmd.Local)
		{
		case "new_chat":
			PageBase.Run(NewChatAsync);
			return;
		case "clear":
			PageBase.Run(ClearChatAsync);
			return;
		case "rewind":
			if (_busy)
			{
				PageBase.Toast(I18n.L("回合还在跑"), I18n.L("先停止再撤回"), ToastKind.Warning);
			}
			else
			{
				PageBase.Run(RewindAsync);
			}
			return;
		}
		if (cmd.Prompt.Length > 0)
		{
			PageBase.Run(() => SendTextAsync(I18n.L(cmd.Prompt)));
		}
	}

	private async Task ClearChatAsync()
	{
		AiChatDto chat = ActiveChat;
		if (chat == null || chat.Messages.Count == 0)
		{
			PageBase.Toast(I18n.L("当前对话是空的"), "");
			return;
		}
		if (await Dlg.Confirm(I18n.L("清空当前对话？"), I18n.L("这个对话的全部消息会被删除，无法恢复（不会还原磁盘改动）。"), I18n.L("清空"), null, danger: true))
		{
			string id = chat.Id;
			await SwitchChatAsync(async delegate
			{
				await PageBase.Api.TryCallAsync("ai_delete_chat", new
				{
					chat_id = id
				}, _store);
				return await PageBase.Api.TryCallAsync("ai_new_chat", null, _store);
			});
		}
	}

	public override void OnHidden()
	{
		CloseSlash();
		base.OnHidden();
	}

	private void SyncPlaceholder()
	{
		if (_pendingConfirm != null && !_pendingConfirm.Answered)
		{
			_placeholder.Text = _localMode ? "请先允许或拒绝上方的工具操作" : I18n.L("先处理上面的确认，或直接输入新要求来拒绝");
		}
		else if (_busy)
		{
			_placeholder.Text = _localMode ? "正在回复，可以先写好下一条消息…" : I18n.L("回复中也可以继续输入，会立刻交给助手");
		}
		else
		{
			_placeholder.Text = _localMode ? "给本地模型发送消息…  Enter 发送，Shift+Enter 换行" : I18n.L("问点什么，@ 引用实例或崩溃日志…  Enter 发送，Shift+Enter 换行");
		}
		_placeholder.Visibility = ((!string.IsNullOrEmpty(_input.Text)) ? Visibility.Collapsed : Visibility.Visible);
	}

	private void SyncPermTip()
	{
		if (_localMode) { _permTip.Text = _localTools ? "本地工具助手 · 操作前确认" : "本地文字聊天 · 使用最近上下文 · 不执行工具操作"; return; }
		int selectedIndex = _perm.SelectedIndex;
		TextBlock permTip = _permTip;
		permTip.Text = selectedIndex switch
		{
			1 => I18n.L("当前「写直接执行」：写入不再询问，删除仍会先问你"), 
			2 => I18n.L("当前「只看不动」：只读取和分析，不会改任何文件"), 
			3 => I18n.L("当前「全自动」：所有操作都不再询问，请谨慎使用"), 
			4 => I18n.L("当前「自定义」：按权限管理里的规则执行"), 
			_ => I18n.L("当前「每次确认」：读取直接做，写入和删除都会先问你"), 
		};
		_permTip.SetResourceReference(TextBlock.ForegroundProperty, (selectedIndex == 3) ? "B.Danger" : "B.InkMuted");
		_perm.SetResourceReference(Control.ForegroundProperty, (selectedIndex == 3) ? "B.Danger" : "B.Ink");
	}

	private void InsertMention()
	{
		string text = PageBase.Win?.Prefs.CatalogInstance ?? "";
		string text2 = ((text.Length > 0) ? I18n.L("@实例「{0}」 ", text) : I18n.L("@最近的崩溃日志 "));
		int caretIndex = _input.CaretIndex;
		_input.Text = _input.Text.Insert(Math.Min(caretIndex, _input.Text.Length), text2);
		_input.CaretIndex = Math.Min(caretIndex + text2.Length, _input.Text.Length);
		_input.Focus();
	}

	private ContextMenu MoreMenu()
	{
		ContextMenu contextMenu = new ContextMenu();
		MenuItem menuItem = new MenuItem
		{
			Header = I18n.L("新对话")
		};
		menuItem.Click += delegate
		{
			PageBase.Run(NewChatAsync);
		};
		MenuItem menuItem2 = new MenuItem
		{
			Header = I18n.L("管理权限")
		};
		menuItem2.Click += delegate
		{
			PageBase.Run(OpenPermissionPanelAsync);
		};
		MenuItem rewind = new MenuItem
		{
			Header = I18n.L("撤回上一轮")
		};
		rewind.Click += delegate
		{
			if (!_busy)
			{
				PageBase.Run(RewindAsync);
			}
		};
		MenuItem del = new MenuItem
		{
			Header = I18n.L("删除这个对话")
		};
		del.Click += delegate
		{
			string id = _store.ActiveId;
			if (!string.IsNullOrEmpty(id))
			{
				PageBase.Run(() => SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_delete_chat", new
				{
					chat_id = id
				}, _store)));
			}
		};
		contextMenu.Items.Add(menuItem);
		contextMenu.Items.Add(menuItem2);
		contextMenu.Items.Add(rewind);
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(del);
		contextMenu.Opened += delegate
		{
			rewind.IsEnabled = !_busy && LastUserText().Length > 0;
			del.IsEnabled = ActiveChat != null;
		};
		return contextMenu;
	}

	private async Task SyncPermissionAsync()
	{
		Dictionary<string, JsonElement> obj = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		string text = "default";
		if (obj.TryGetValue("ai_permission_mode", out var value) && value.ValueKind == JsonValueKind.String)
		{
			text = value.GetString() ?? text;
		}
		int num = Array.IndexOf<string>(PermissionModes, text);
		if (num < 0)
		{
			num = 0;
		}
		_syncPerm = true;
		try
		{
			_perm.SelectedIndex = num;
		}
		finally
		{
			_syncPerm = false;
		}
	}

	private async Task SavePermissionModeAsync()
	{
		string text = ((_perm.SelectedIndex >= 0 && _perm.SelectedIndex < PermissionModes.Length) ? PermissionModes[_perm.SelectedIndex] : "default");
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["ai_permission_mode"] = text,
				["ai_confirm_writes"] = text != "yolo"
			}
		});
	}

	private async Task OpenPermissionPanelAsync()
	{
		string instance = (await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = "default"
		}, "default")) ?? "default";
		AiPermissionPanel panel = new AiPermissionPanel(instance);
		await panel.RefreshAsync();
		Dlg.Layer layer = Dlg.Panel(I18n.L("权限管理"), panel, 860.0);
		panel.CloseRequested = layer.Close;
	}

	protected override async Task LoadAsync()
	{
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, new AiStoreDto())) ?? new AiStoreDto();
		await SyncPermissionAsync();
		await SyncModelAsync();
		if (_busy && !_store.Busy)
		{
			ResetRunState();
		}
		else if (!_busy && _store.Busy)
		{
			SetBusy(on: true);
		}
		RenderChats();
		if (!_busy || _runChatId.Length <= 0 || !(_runChatId == _store.ActiveId))
		{
			RenderMessages();
			RestorePendingCard();
		}
	}

	private void RestorePendingCard()
	{
		JsonElement? pc = _store.PendingCard;
		if (!pc.HasValue || pc.GetValueOrDefault().ValueKind != JsonValueKind.Object || !_store.Busy)
		{
			return;
		}
		string text = GetStr("kind");
		if (text.Length == 0)
		{
			return;
		}
		string text2 = GetStr("chat_id");
		if (text2.Length <= 0 || !(text2 != _store.ActiveId))
		{
			BridgeEvent ev = new BridgeEvent
			{
				Name = GetStr("name"),
				Label = GetStr("label"),
				Payload = pc.Value
			};
			SetBusy(on: true);
			if (_runChatId.Length == 0)
			{
				_runChatId = ((text2.Length > 0) ? text2 : _store.ActiveId);
			}
			if (text == "confirm")
			{
				ShowConfirm(ev);
			}
			else if (text == "ask")
			{
				ShowAsk(ev);
			}
		}
		string GetStr(string prop)
		{
			if (!pc.Value.TryGetProperty(prop, out var value) || value.ValueKind != JsonValueKind.String)
			{
				return "";
			}
			return value.GetString() ?? "";
		}
	}

	private void RenderChats()
	{
		_chatList.Children.Clear();
		string text = _histSearch?.Text?.Trim() ?? "";
		int num = 0;
		foreach (AiChatDto chat in _store.Chats)
		{
			string id = chat.Id;
			string text2 = chat.Title;
			if (string.IsNullOrWhiteSpace(text2))
			{
				AiMsgDto aiMsgDto = chat.Messages.FirstOrDefault((AiMsgDto m) => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content));
				if (aiMsgDto != null)
				{
					text2 = aiMsgDto.Content.Replace("\n", " ").Trim();
					if (text2.Length > 24)
					{
						text2 = text2.Substring(0, 24) + "…";
					}
				}
			}
			if (string.IsNullOrWhiteSpace(text2))
			{
				text2 = I18n.L("新对话");
			}
			if (text.Length > 0 && text2.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			num++;
			bool flag = id == _store.ActiveId;
			bool num2 = _busy && id == _runChatId;
			TextBlock textBlock = Ui.Txt(text2, 12.5, flag, flag ? "B.Ink" : "B.InkMuted");
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			Grid grid = Ui.G(null, "Auto,*");
			if (num2)
			{
				Border border = new Border
				{
					Width = 6.0,
					Height = 6.0,
					CornerRadius = new CornerRadius(3.0),
					Margin = new Thickness(0.0, 0.0, 7.0, 0.0),
					ToolTip = I18n.L("正在运行")
				};
				border.SetResourceReference(Border.BackgroundProperty, "B.Accent");
				grid.Add(border.VCenter(), 0);
			}
			grid.Add(textBlock.VCenter(), 0, 1);
			Button button = new Button
			{
				Template = AiIcons.FlatButtonTemplate(),
				Content = grid,
				Padding = new Thickness(9.0, 7.0, 9.0, 7.0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = Cursors.Hand,
				ToolTip = text2,
				Focusable = true
			};
			if (button.Content is Grid grid2)
			{
				grid2.HorizontalAlignment = HorizontalAlignment.Stretch;
			}
			if (flag)
			{
				button.SetResourceReference(Control.BackgroundProperty, "B.Hover");
			}
			else
			{
				button.Background = Brushes.Transparent;
				button.MouseEnter += delegate
				{
					button.SetResourceReference(Control.BackgroundProperty, "B.Hover");
				};
				button.MouseLeave += delegate
				{
					button.Background = Brushes.Transparent;
				};
			}
			button.Click += delegate
			{
				_historyLayer?.Dismiss();
				if (!(id == _store.ActiveId))
				{
					PageBase.Run(() => SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_set_active", new
					{
						chat_id = id
					}, _store)));
				}
			};
			button.ContextMenu = DelMenu(id);
			_chatList.Children.Add(button.Stretch());
		}
		if (num == 0)
		{
			_chatList.Children.Add(Ui.Txt((text.Length > 0) ? I18n.L("没有匹配的对话") : I18n.L("还没有对话"), 12.0, bold: false, "B.InkFaint").M(8.0, 4.0));
		}
		AiChatDto activeChat = ActiveChat;
		_titleText.Text = ((activeChat == null || string.IsNullOrWhiteSpace(activeChat.Title)) ? I18n.L("新对话") : activeChat.Title);
		SyncInstChip();
	}

	private ContextMenu DelMenu(string id)
	{
		ContextMenu contextMenu = new ContextMenu();
		MenuItem menuItem = new MenuItem
		{
			Header = I18n.L("删除这个对话")
		};
		menuItem.Click += delegate
		{
			PageBase.Run(() => SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_delete_chat", new
			{
				chat_id = id
			}, _store)));
		};
		contextMenu.Items.Add(menuItem);
		return contextMenu;
	}

	private Task NewChatAsync()
	{
		_historyLayer?.Dismiss();
		AiChatDto activeChat = ActiveChat;
		if (activeChat != null && activeChat.Messages.Count == 0 && string.IsNullOrWhiteSpace(activeChat.Title) && !_busy)
		{
			_input.Focus();
			return Task.CompletedTask;
		}
		return SwitchChatAsync(() => PageBase.Api.TryCallAsync("ai_new_chat", null, _store));
	}

	private async Task SwitchChatAsync(Func<Task<AiStoreDto?>> op)
	{
		string before = _store.ActiveId;
		_store = (await op()) ?? _store;
		if (_store.ActiveId != before || (_busy && ActiveChat == null))
		{
			await AbandonRunAsync();
		}
		if (!_busy)
		{
			_status.Text = "";
		}
		RenderChats();
		RenderMessages();
	}

	private async Task AbandonRunAsync()
	{
		if (_busy)
		{
			_abandoned = true;
			TaskCompletionSource<object?> ended = (_runEnded = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));
			if (!BackendBusy(await PageBase.Api.TryCallAsync<JsonElement>("ai_stop")))
			{
				_roundBubble = null;
				_round.Clear();
				_stream.Clear();
				_runChatId = "";
				_status.Text = "";
				SetBusy(on: false);
				_runEnded?.TrySetResult(null);
				_runEnded = null;
			}
			else
			{
				await Task.WhenAny(ended.Task, Task.Delay(3000));
				ResetRunState();
				PageBase.Toast(I18n.L("已停止上一个对话的回合"), I18n.L("切换对话时正在运行的那一轮已中断"));
			}
		}
	}

	private static bool BackendBusy(JsonElement r)
	{
		if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("busy", out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private Bubble OpenRound(string? thinking = null)
	{
		Bubble roundBubble = _roundBubble;
		if (roundBubble != null)
		{
			if (thinking != null)
			{
				roundBubble.SetThinking(thinking);
			}
			return roundBubble;
		}
		_round.Clear();
		_group?.Refresh();
		_group = null;
		Bubble bubble = new Bubble("assistant", "…", RetryFrom);
		bubble.SetThinking(thinking ?? I18n.L("正在想…"));
		Add(bubble);
		return _roundBubble = bubble;
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

	private void ResetRunState()
	{
		_roundBubble = null;
		_round.Clear();
		_stream.Clear();
		_runChatId = "";
		_abandoned = false;
		_status.Text = "";
		SetBusy(on: false);
		_runEnded?.TrySetResult(null);
		_runEnded = null;
	}

	private bool RunIsDisplayed(BridgeEvent ev)
	{
		string text = ChatIdOf(ev);
		if (text.Length == 0)
		{
			text = _runChatId;
		}
		if (text.Length != 0)
		{
			return text == _store.ActiveId;
		}
		return true;
	}

	private static string ChatIdOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("chat_id", out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private void RenderMessages()
	{
		_messages.Children.Clear();
		_toolLines.Clear();
		_taskCards.Clear();
		_roundBubble = null;
		_round.Clear();
		_stream.Clear();
		_group = null;
		_needWho = true;
		_lastUser = null;
		_emptyView = null;
		_planCard = null;
		AiChatDto activeChat = ActiveChat;
		_titleText.Text = ((activeChat == null || string.IsNullOrWhiteSpace(activeChat.Title)) ? I18n.L("新对话") : activeChat.Title);
		if (activeChat == null || activeChat.Messages.Count == 0)
		{
			_emptyView = EmptyState();
			_messages.Children.Add(_emptyView);
			SyncConversationLayout();
			_retry.IsEnabled = false;
			_rewind.IsEnabled = false;
			return;
		}
		Bubble lastAssistant = null;
		foreach (AiMsgDto message in activeChat.Messages)
		{
			if (message.Role == "user" && message.Id.StartsWith("compact_", StringComparison.Ordinal))
			{
				_messages.Children.Add(Divider(I18n.L("更早的对话已压缩成摘要")));
				continue;
			}
			bool flag;
			switch (message.Role)
			{
			case "user":
			case "assistant":
			case "error":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				string text = message.Content;
				if (!string.IsNullOrWhiteSpace(message.Note) && !text.Contains(message.Note, StringComparison.Ordinal))
				{
					text = (text + "\n\n" + message.Note).Trim();
				}
				Bubble bubble = new Bubble(message.Role, text, RetryFrom);
				bubble.OrigText = message.Content;
				bubble.IsSteer = message.Id.StartsWith("steer_", StringComparison.Ordinal);
				if (message.Role == "user")
				{
					_needWho = true;
					_lastUser = bubble;
					lastAssistant = null;
				}
				else
				{
					AddWhoIfNeeded(animate: false);
					lastAssistant = bubble;
				}
				_messages.Children.Add(bubble);
			}
		}
		AiPlanDto plan = activeChat.Plan;
		if (plan != null)
		{
			List<AiPlanItemDto> items = plan.Items;
			if (items != null && items.Count > 0)
			{
				_planCard = new PlanCard((from i in activeChat.Plan.Items
					where !string.IsNullOrWhiteSpace(i.Title)
					select (Title: i.Title, Status: i.Status)).ToList());
				_messages.Children.Add(_planCard);
			}
		}
		Motion.Stagger(_messages, 18.0, 200.0, 8.0);
		_retry.IsEnabled = !_busy && LastUserText().Length > 0;
		_rewind.IsEnabled = _retry.IsEnabled;
		SyncTurnActions(lastAssistant);
		SyncConversationLayout();
		ScrollDown(force: true);
	}

	private void SyncTurnActions(Bubble? lastAssistant = null)
	{
		foreach (Bubble item in _messages.Children.OfType<Bubble>())
		{
			if (item.IsMine)
			{
				item.Rewind = null;
				item.Edit = null;
			}
			else
			{
				item.Retry = null;
			}
		}
		if (_busy)
		{
			return;
		}
		if (_lastUser != null)
		{
			_lastUser.Rewind = delegate
			{
				PageBase.Run(RewindAsync);
			};
		}
		foreach (Bubble item2 in _messages.Children.OfType<Bubble>())
		{
			if (item2.IsMine && !item2.IsSteer)
			{
				item2.Edit = EditFrom;
			}
		}
		Bubble bubble = lastAssistant ?? _messages.Children.OfType<Bubble>().LastOrDefault((Bubble b) => !b.IsMine);
		int num = ((bubble == null) ? (-1) : _messages.Children.IndexOf(bubble));
		int num2 = ((_lastUser == null) ? (-1) : _messages.Children.IndexOf(_lastUser));
		if (bubble != null && num > num2 && LastUserText().Length > 0)
		{
			bubble.Retry = delegate
			{
				PageBase.Run(RetryAsync);
			};
		}
	}

	private FrameworkElement EmptyState()
	{
		TextBlock textBlock = Ui.Txt(I18n.L("Minecraft 助手"), 24.0, bold: true, "B.Ink").Wrap();
		textBlock.HorizontalAlignment = HorizontalAlignment.Center;
		textBlock.TextAlignment = TextAlignment.Center;
		textBlock.Margin = new Thickness(0.0, 14.0, 0.0, 0.0);
		TextBlock textBlock2 = Ui.Txt(I18n.L(_localMode && !_localTools ? "聊玩法、选模组，或一起读懂日志。" : "安装游戏、管理模组，一起排查启动问题。"), 13.0, bold: false, "B.InkMuted").Wrap();
		textBlock2.TextAlignment = TextAlignment.Center;
		textBlock2.HorizontalAlignment = HorizontalAlignment.Center;
		textBlock2.Margin = new Thickness(0.0, 6.0, 0.0, 18.0);
		UniformGrid uniformGrid = new UniformGrid
		{
			Columns = 2,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			MaxWidth = 620.0
		};
		uniformGrid.SizeChanged += (_, _) => uniformGrid.Columns = uniformGrid.ActualWidth < 480 ? 1 : 2;
		(string, string, string, string)[] quickStarts = _localMode ? LocalQuickStarts : QuickStarts;
		for (int i = 0; i < quickStarts.Length; i++)
		{
			(string, string, string, string) tuple = quickStarts[i];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			string item3 = tuple.Item3;
			string item4 = tuple.Item4;
			string p = item4;
			Grid grid = Ui.G(null, "Auto,*");
			grid.Add(AiIcons.Icon(item3, 18.0, "B.InkMuted").VCenter(), 0);
			grid.Add(Ui.V(2.0, Ui.Txt(item, 13.0, bold: true, "B.Ink").Wrap(), Ui.Txt(item2, 12.0, bold: false, "B.InkMuted").Wrap()).M(11.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			Button card = new Button
			{
				Style = Ui.S("Btn.Ghost"),
				Content = grid,
				Padding = new Thickness(6.0, 10.0, 6.0, 10.0),
				Margin = new Thickness(0, 0, 14, 0),
				BorderThickness = new Thickness(0),
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				Cursor = Cursors.Hand
			};
			System.Windows.Automation.AutomationProperties.SetName(card, item);
			card.SetResourceReference(Control.BorderBrushProperty, "B.LineSoft");
			card.Click += delegate
			{
				PageBase.Run(() => SendTextAsync(p));
			};
			uniformGrid.Children.Add(card);
		}
		SPanel sPanel = Ui.V(0.0, textBlock, textBlock2, uniformGrid);
		sPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
		sPanel.MaxWidth = 620;
		sPanel.VerticalAlignment = VerticalAlignment.Center;
		sPanel.Margin = new Thickness(0.0, 0.0, 0.0, 8.0);
		return sPanel;
	}

	private static FrameworkElement Divider(string text)
	{
		Grid grid = Ui.G(null, "*,Auto,*");
		Border border = new Border
		{
			Height = 1.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		Border border2 = new Border
		{
			Height = 1.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		border.SetResourceReference(Border.BackgroundProperty, "B.Line");
		border2.SetResourceReference(Border.BackgroundProperty, "B.Line");
		grid.Add(border, 0);
		grid.Add(Ui.H(6.0, AiIcons.Icon("M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8 M10 12h4", 12.0, "B.InkFaint").VCenter(), Ui.Txt(text, 11.5, bold: false, "B.InkFaint").VCenter()).M(10.0, 0.0, 10.0), 0, 1);
		grid.Add(border2, 0, 2);
		grid.Margin = new Thickness(0.0, 2.0, 0.0, 2.0);
		return grid;
	}

	private static FrameworkElement Note(string text, string icon = "M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z M12 9v4 M12 17h.01")
	{
		Border border = new Border();
		border.CornerRadius = new CornerRadius(8.0);
		border.Padding = new Thickness(10.0, 6.0, 10.0, 6.0);
		border.Child = Ui.H(8.0, AiIcons.Icon(icon, 13.0, "B.Warn").VCenter(), Ui.Txt(text, 12.0, bold: false, "B.Warn").Wrap().VCenter());
		border.SetResourceReference(Border.BackgroundProperty, "B.WarnSoft");
		return border;
	}

	private void AddWhoIfNeeded(bool animate = true)
	{
		if (_needWho)
		{
			_needWho = false;
			Border border = new Border
			{
				Width = 22.0,
				Height = 22.0,
				CornerRadius = new CornerRadius(7.0),
				Child = AiIcons.Icon("M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z", 12.0, "B.OnAccentStrong").Mid()
			};
			border.SetResourceReference(Border.BackgroundProperty, "B.AccentStrong");
			SPanel sPanel = Ui.H(7.0, border.VCenter(), Ui.Txt(I18n.L("HelL 助手"), 12.0, bold: false, "B.InkMuted").VCenter());
			sPanel.Margin = new Thickness(0.0, 6.0, 0.0, -4.0);
			sPanel.Tag = "who";
			_messages.Children.Add(sPanel);
			if (animate)
			{
				Motion.FadeIn(sPanel, 200.0, 8.0);
			}
		}
	}

	private void ScrollDown(bool force = false)
	{
		if (force) _followOutput = true;
		if (!_followOutput || _scrollQueued) return;
		_scrollQueued = true;
		base.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)delegate
		{
			_scrollQueued = false;
			if (_followOutput) _scroll.ScrollToEnd();
		});
	}

	private void Add(UIElement el)
	{
		if (_emptyView != null)
		{
			_messages.Children.Remove(_emptyView);
			_emptyView = null;
			SyncConversationLayout();
		}
		if (el is FrameworkElement { Parent: Panel parent })
		{
			if (parent == _messages)
			{
				ScrollDown();
				return;
			}
			parent.Children.Remove(el);
		}
		if (el is Bubble { IsMine: not false } bubble)
		{
			_needWho = true;
			_group = null;
			_lastUser = bubble;
		}
		else if (!(el is ConfirmCard))
		{
			AddWhoIfNeeded();
		}
		_messages.Children.Add(el);
		Motion.FadeIn(el, 200.0, 8.0);
		ScrollDown(force: el is Bubble { IsMine: true });
	}

	private string LastUserText()
	{
		return ActiveChat?.Messages.LastOrDefault((AiMsgDto m) => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content) && !m.Id.StartsWith("steer_", StringComparison.Ordinal))?.Content ?? "";
	}

	private static readonly string[] _imgExts = new string[6] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

	private void PickImage()
	{
		string path = Dlg.PickFile(I18n.L("图片") + "|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp", I18n.L("选择图片"));
		if (path != null)
		{
			AddImage(path);
		}
	}

	private void AddImage(string path)
	{
		if (string.IsNullOrEmpty(path) || _pendingImages.Contains(path) || !_imgExts.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
		{
			return;
		}
		if (_pendingImages.Count >= 4)
		{
			PageBase.Toast(I18n.L("最多附 4 张图"), "", ToastKind.Warning);
			return;
		}
		_pendingImages.Add(path);
		SyncImageChip();
		_ = CheckVisionAsync();
	}

	private async Task CheckVisionAsync()
	{
		if (_visionChecking || _pendingImages.Count == 0) return;
		_visionChecking = true;
		try
		{
			JsonElement r = await PageBase.Api.CallAsync<JsonElement>("ai_vision_status");
			if (_pendingImages.Count == 0) return;
			if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("supported", out var s) && s.ValueKind == JsonValueKind.False)
			{
				_imagePreviews.Notice = I18n.L("当前模型不支持图片，请切换支持识图的模型。");
			}
			else _imagePreviews.Notice = "";
		}
		catch (Exception ex)
		{
			_imagePreviews.Notice = I18n.L("暂时无法确认模型是否支持图片。");
			Smoke.Note("attachment-vision", ex.Message);
		}
		finally { _visionChecking = false; }
	}

	private void SyncImageChip()
	{
		_imgChip.Text = (_pendingImages.Count > 0) ? I18n.L("{0} 张图 ×", _pendingImages.Count) : "";
		_imgChip.ToolTip = (_pendingImages.Count > 0) ? (string.Join("\n", _pendingImages) + "\n" + I18n.L("点击清空")) : null;
		_imagePreviews.Update(_pendingImages);
	}

	private void SetupImageInput()
	{
		_imgChip.FontSize = 12.0;
		_imgChip.Cursor = Cursors.Hand;
		_imgChip.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
		_imgChip.MouseLeftButtonUp += delegate
		{
			_pendingImages.Clear();
			SyncImageChip();
		};
		_input.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
			{
				return;
			}
			try
			{
				if (Clipboard.ContainsImage())
				{
					System.Windows.Media.Imaging.BitmapSource img = Clipboard.GetImage();
					string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HelL-paste");
					System.IO.Directory.CreateDirectory(dir);
					string file = System.IO.Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
					using (System.IO.FileStream fs = System.IO.File.Create(file))
					{
						System.Windows.Media.Imaging.PngBitmapEncoder enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
						enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
						enc.Save(fs);
					}
					AddImage(file);
					e.Handled = true;
				}
				else if (Clipboard.ContainsFileDropList())
				{
					foreach (string f in Clipboard.GetFileDropList())
					{
						AddImage(f);
					}
					e.Handled = _pendingImages.Count > 0;
				}
			}
			catch (Exception)
			{
			}
		};
	}

	private void HookImageDrop(UIElement target)
	{
		target.AllowDrop = true;
		target.PreviewDragOver += delegate(object _, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop) && ((string[])e.Data.GetData(DataFormats.FileDrop)).Any((string f) => _imgExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())))
			{
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
			}
		};
		target.PreviewDrop += delegate(object _, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				return;
			}
			string[] files = ((string[])e.Data.GetData(DataFormats.FileDrop)).Where((string f) => _imgExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray();
			if (files.Length > 0)
			{
				foreach (string f in files)
				{
					AddImage(f);
				}
				e.Handled = true;
			}
		};
	}

	private Task SendAsync()
	{
		return SendTextAsync(_input.Text);
	}

	private void RetryFrom(string text)
	{
		PageBase.Run(() => SendTextAsync(text));
	}

	private Task RetryAsync()
	{
		string text = LastUserText();
		if (text.Length != 0)
		{
			return SendTextAsync(text);
		}
		return Task.CompletedTask;
	}

	private void EditFrom(Bubble bubble, string newText)
	{
		PageBase.Run(() => EditResendAsync(bubble, newText), I18n.L("编辑重发失败"));
	}

	/// <summary>编辑后重发：用撤回检查点逻辑一轮轮回退到这句话之前，再用新内容重跑。</summary>
	private async Task EditResendAsync(Bubble bubble, string newText)
	{
		if (_busy)
		{
			PageBase.Toast(I18n.L("回合还在跑"), I18n.L("先停止再编辑"), ToastKind.Warning);
			return;
		}
		string target = (bubble.OrigText ?? "").Trim();
		List<Bubble> mine = _messages.Children.OfType<Bubble>().Where((Bubble b) => b.IsMine).ToList();
		int at = mine.IndexOf(bubble);
		int occurrence = (at < 0) ? 0 : mine.Skip(at + 1).Count((Bubble b) => (b.OrigText ?? "").Trim() == target);
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
		List<(string, string, string)> msgs = (ActiveChat?.Messages ?? new List<AiMsgDto>()).Select((AiMsgDto m) => (m.Role, m.Id, m.Content)).ToList();
		int rounds = AiChatOps.RoundsToRewind(msgs, target, occurrence);
		if (rounds <= 0)
		{
			RenderMessages();
			PageBase.Toast(I18n.L("没法从这句重发"), I18n.L("这句话可能已被压缩进摘要，或是插队消息"), ToastKind.Warning);
			return;
		}
		int restored = 0;
		for (int i = 0; i < rounds; i++)
		{
			JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_rewind", new
			{
				chat_id = (_store.ActiveId ?? "")
			});
			bool ok = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
			if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("restored_files", out var rf) && rf.TryGetInt32(out var n))
			{
				restored += n;
			}
			if (!ok)
			{
				_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
				RenderMessages();
				string msg = (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message", out var me) && me.ValueKind == JsonValueKind.String) ? (me.GetString() ?? "") : "";
				_status.Text = (msg.Length > 0) ? msg : I18n.L("回退没有全部成功，已停下，请检查后再发");
				return;
			}
		}
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
		RenderMessages();
		await SendTextAsync(newText);
		if (restored > 0)
		{
			PageBase.Toast(I18n.L("已回退并重发"), I18n.L("{0} 个文件的改动已还原", restored), ToastKind.Success);
		}
	}

	private async Task SendTextAsync(string raw, bool echo = true)
	{
		if (_switchingModel) { Toast("正在切换模型，请稍后发送"); return; }
		if (_localMode && (_busy || _pendingImages.Count > 0))
		{
			Toast(_busy ? "请等待当前回复完成，或先停止回复" : "本地聊天仅支持文字，请先移除图片", "", ToastKind.Warning);
			return;
		}
		string text = (raw ?? "").Trim();
		if (text.Length == 0 && echo && _pendingImages.Count > 0)
		{
			text = I18n.L("看看这张图");
		}
		if (text.Length == 0)
		{
			return;
		}
		if (_busy && _pendingConfirm != null && !_pendingConfirm.Answered)
		{
			_pendingConfirm.Reject();
		}
		if (_busy)
		{
			JsonElement jsonElement = await PageBase.Api.TryCallAsync<JsonElement>("ai_steer", new { text });
			if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("ok", out var value) && value.ValueKind == JsonValueKind.True)
			{
				if (echo)
				{
					_input.Clear();
					Add(new Bubble("user", text, RetryFrom)
					{
						IsSteer = true
					});
					PageBase.Toast(I18n.L("已插队"), I18n.L("这句话会立刻交给正在运行的助手"));
				}
				return;
			}
			TaskCompletionSource<object> taskCompletionSource = _runEnded ?? (_runEnded = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));
			await Task.WhenAny(taskCompletionSource.Task, Task.Delay(3000));
			if (_busy)
			{
				PageBase.Toast(I18n.L("上一条还在处理"), I18n.L("等它说完，或点「停止」再发"), ToastKind.Warning);
				return;
			}
		}
		if (echo)
		{
			_input.Clear();
		}
		_runChatId = _store.ActiveId;
		SetBusy(on: true);
		_status.Text = I18n.L("思考中…");
		if (echo)
		{
			Add(new Bubble("user", (_pendingImages.Count > 0) ? (text + "\n" + I18n.L("［附图 {0} 张］", _pendingImages.Count)) : text, RetryFrom)
			{
				OrigText = text
			});
		}
		_stream.Clear();
		_roundBubble = OpenRound();
		Dictionary<string, object> launch = new Dictionary<string, object>
		{
			["instance"] = PageBase.Win?.Prefs.CatalogInstance ?? "",
			["username"] = PageBase.Win?.Prefs.LastUser ?? ""
		};
		List<string> images = (echo ? _pendingImages.ToList() : new List<string>());
		_pendingImages.Clear();
		SyncImageChip();
		OpResult opResult = await PageBase.Api.TryCallAsync<OpResult>("ai_send", new
		{
			text = text,
			chat_id = _store.ActiveId,
			launch = launch,
			images = images
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
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_stop");
		_status.Text = I18n.L("已停止");
		if (!BackendBusy(r))
		{
			ResetRunState();
			_status.Text = I18n.L("已停止");
		}
	}

	private async Task RewindAsync()
	{
		JsonElement r = await PageBase.Api.TryCallAsync<JsonElement>("ai_rewind", new
		{
			chat_id = (_store.ActiveId ?? "")
		});
		_store = (await PageBase.Api.TryCallAsync("ai_list_chats", null, _store)) ?? _store;
		RenderMessages();
		JsonElement value;
		bool flag = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("ok", out value) && value.GetBoolean();
		JsonElement value2;
		bool flag2 = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("disk_changed", out value2) && value2.GetBoolean();
		if (!flag && flag2)
		{
			_status.Text = I18n.L("对话已回退，但磁盘改动没能全部还原，请手动检查相关文件");
			return;
		}
		if (!flag)
		{
			JsonElement value3;
			string text = ((r.ValueKind == JsonValueKind.Object && r.TryGetProperty("message", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
			_status.Text = ((text.Length > 0) ? text : I18n.L("撤回失败"));
			return;
		}
		int num = 0;
		if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("restored_files", out var value4) && value4.TryGetInt32(out var value5))
		{
			num = value5;
		}
		JsonElement value6;
		if (flag2)
		{
			_status.Text = string.Format(I18n.L("已撤回：{0} 个文件的改动已还原"), num);
		}
		else if (r.ValueKind == JsonValueKind.Object && r.TryGetProperty("rollbackable", out value6) && value6.ValueKind == JsonValueKind.False)
		{
			_status.Text = I18n.L("已撤回：只回退了对话，这一轮的磁盘改动无法自动还原");
		}
		else
		{
			_status.Text = I18n.L("已撤回：只回退了对话，这一轮没有可还原的磁盘改动");
		}
	}

	private void SetBusy(bool on)
	{
		bool flag = _busy != on;
		_busy = on;
		_modelButton.IsEnabled = !on && !_switchingModel;
		SyncConversationLayout();
		_send.IsEnabled = !on;
		_stop.IsEnabled = on;
		_sendStop.Content = (on ? _stop : _send);
		_rewind.IsEnabled = !on && LastUserText().Length > 0;
		_retry.IsEnabled = !on && LastUserText().Length > 0;
		if (!on)
		{
			foreach (StepGroup item in _messages.Children.OfType<StepGroup>())
			{
				item.Complete();
			}
			_group = null;
			ClearDock();
		}
		SyncPlaceholder();
		SyncTurnActions();
		if (flag)
		{
			RenderChats();
		}
	}

	private void ClearDock()
	{
		if (_pendingConfirm != null)
		{
			_ = _pendingConfirm.Answered;
		}
		_pendingConfirm = null;
		_dock.Children.Clear();
	}

	private ToolLine Line(string name, string text, ToolState state)
	{
		if (_toolLines.TryGetValue(name, out ToolLine value))
		{
			value.SetText(text);
			value.SetState(state);
			FindGroup(value)?.Refresh();
			return value;
		}
		value = new ToolLine(text, name);
		value.SetState(state);
		_toolLines[name] = value;
		if (_group == null || _group.Parent != _messages)
		{
			_group = new StepGroup();
			Add(_group);
		}
		_group.AddLine(value);
		ScrollDown();
		return value;
	}

	private static StepGroup? FindGroup(DependencyObject el)
	{
		for (DependencyObject dependencyObject = el; dependencyObject != null; dependencyObject = LogicalTreeHelper.GetParent(dependencyObject))
		{
			if (dependencyObject is StepGroup result)
			{
				return result;
			}
		}
		return null;
	}

	public override void OnEvent(BridgeEvent ev)
	{
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
				if (text == "ai.delta" && RunIsDisplayed(ev))
				{
					_stream.Append(ev.Text);
					Bubble bubble = OpenRound();
					_round.Append(ev.Text);
					bubble.SetText(_round.ToString(), live: true);
					ScrollDown();
				}
				break;
			case 'p':
			{
				if (text == "progress" && _taskCards.TryGetValue(ev.TaskId, out TaskCard value2))
				{
					value2.SetProgress(ev.Current, ev.Total, ev.Message);
				}
				break;
			}
			case 'f':
				if (text == "finished")
				{
					if (_taskCards.TryGetValue(ev.TaskId, out TaskCard value))
					{
						value.Finish(ev.Success, ev.Message);
					}
					ReportPendingTask(ev);
				}
				break;
			}
			break;
		case 7:
			switch (text[3])
			{
			case 'd':
			{
				if (!(text == "ai.done"))
				{
					break;
				}
				string text4 = ChatIdOf(ev);
				if (_busy && _runChatId.Length > 0 && text4.Length > 0 && text4 != _runChatId)
				{
					break;
				}
				bool quiet = _abandoned;
				bool mine = RunIsDisplayed(ev);
				_status.Text = "";
				if (!quiet && mine)
				{
					NotifyStop(ev);
				}
				if (mine)
				{
					ShowUsage(ev);
				}
				string text5 = StopNote(ev);
				if (mine && (_round.Length > 0 || text5.Length > 0))
				{
					string text6 = _round.ToString();
					if (text5.Length > 0 && !text6.Contains(text5))
					{
						text6 = ((text6.Length > 0) ? (text6 + "\n\n" + text5) : text5);
					}
					OpenRound().SetText(text6);
				}
				if (mine)
				{
					foreach (var (key, item) in PendingTasksOf(ev))
					{
						_aiPendingTasks[key] = (item, ChatIdOf(ev));
					}
				}
				ResetRunState();
				string unsent3 = UnsentOf(ev);
				PageBase.Run(async delegate
				{
					AiStoreDto aiStoreDto = await PageBase.Api.TryCallAsync("ai_list_chats", null, _store);
					_store = aiStoreDto ?? _store;
					RenderChats();
					if (!mine)
					{
						RenderMessages();
					}
					if (mine && !quiet && unsent3.Length > 0)
					{
						await SendTextAsync(unsent3, echo: false);
					}
				});
				break;
			}
			case 'f':
			{
				if (!(text == "ai.fail"))
				{
					break;
				}
				string text2 = ChatIdOf(ev);
				if (_busy && _runChatId.Length > 0 && text2.Length > 0 && text2 != _runChatId)
				{
					break;
				}
				bool flag = RunIsDisplayed(ev);
				bool abandoned = _abandoned;
				_status.Text = (ev.Stopped ? "" : ev.Text);
				if (flag && _roundBubble != null && _round.Length > 0)
				{
					_roundBubble.SetText(_round.ToString());
				}
				if (ev.Stopped)
				{
					if (flag && _roundBubble != null && _round.Length == 0)
					{
						_roundBubble.SetText(I18n.L("已停止"));
					}
					if (!abandoned)
					{
						PageBase.Toast(I18n.L("已停止"), I18n.L("可以继续说下一句"));
					}
				}
				else if (flag)
				{
					if (_roundBubble != null && _round.Length == 0)
					{
						_roundBubble.SetText(ev.Text);
					}
					else
					{
						Add(new Bubble("error", ev.Text, RetryFrom));
					}
				}
				else
				{
					PageBase.Toast(I18n.L("助手出错"), ev.Text, ToastKind.Error);
				}
				string text3 = _status.Text;
				ResetRunState();
				_status.Text = text3;
				string unsent2 = UnsentOf(ev);
				if (flag && !abandoned && unsent2.Length > 0)
				{
					PageBase.Run(() => SendTextAsync(unsent2, echo: false));
				}
				break;
			}
			}
			break;
		case 9:
			if (text == "ai.status" && RunIsDisplayed(ev))
			{
				OnStatus(ev);
			}
			break;
		case 10:
			if (text == "ai.confirm" && RunIsDisplayed(ev))
			{
				ShowConfirm(ev);
			}
			break;
		case 6:
			if (text == "ai.ask" && RunIsDisplayed(ev))
			{
				ShowAsk(ev);
			}
			break;
		}
	}

	internal static List<(string TaskId, string Name)> PendingTasksOf(BridgeEvent ev)
	{
		List<(string, string)> list = new List<(string, string)>();
		if (StopReasonOf(ev) != "pending_task" || !ev.Payload.TryGetProperty("pending_tasks", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return list;
		}
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.Object)
			{
				JsonElement value2;
				string text = ((item.TryGetProperty("task_id", out value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? "") : "");
				if (text.Length != 0)
				{
					JsonElement value3;
					string text2 = ((item.TryGetProperty("name", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
					list.Add((text, (text2.Length > 0) ? text2 : I18n.L("任务")));
				}
			}
		}
		return list;
	}

	internal static string PendingTaskReport(string name, BridgeEvent finished)
	{
		return "[后台任务回报] " + name + " " + (finished.Success ? I18n.L("成功") : I18n.L("失败")) + "：" + finished.Message;
	}

	private void ReportPendingTask(BridgeEvent ev)
	{
		if (!_aiPendingTasks.TryGetValue(ev.TaskId, out var task))
		{
			return;
		}
		_aiPendingTasks.Remove(ev.TaskId);
		if (task.ChatId.Length <= 0 || !(task.ChatId != _store.ActiveId))
		{
			if (!_busy)
			{
				PageBase.Toast(I18n.L("后台任务完成"), I18n.L("助手回来汇报结果"));
			}
			PageBase.Run(() => SendTextAsync(PendingTaskReport(task.Name, ev), echo: false));
		}
	}

	internal static string UnsentOf(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("unsent", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return "";
		}
		List<string> list = new List<string>();
		foreach (JsonElement item in value.EnumerateArray())
		{
			string text = ((item.ValueKind == JsonValueKind.String) ? (item.GetString() ?? "").Trim() : "");
			if (text.Length > 0)
			{
				list.Add(text);
			}
		}
		return string.Join("\n\n", list);
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
			"pending_task" => I18n.L("下载/安装还在后台跑，可以在「下载任务」里看进度。"), 
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

	private void NotifyStop(BridgeEvent ev)
	{
		string text = StopReasonOf(ev);
		if (!(text == "no_tool_call"))
		{
			if (text == "pending_task")
			{
				PageBase.Toast(I18n.L("任务还在后台跑"), I18n.L("可以在「下载任务」里查进度。"));
			}
		}
		else
		{
			PageBase.Toast(I18n.L("它没有真的开始执行"), I18n.L("模型只回了文字，没有调用任何工具。"), ToastKind.Warning);
		}
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
		case "compact":
			Add(Divider(I18n.L("更早的对话已压缩成摘要")));
			break;
		case "checkpoint_warn":
		{
			JsonElement value2;
			string text3 = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("message", out value2) && value2.ValueKind == JsonValueKind.String) ? (value2.GetString() ?? "") : "");
			PageBase.Toast(I18n.L("无法一键撤回"), (text3.Length > 0) ? text3 : I18n.L("检查点不可用，本轮改动无法自动撤回"), ToastKind.Warning);
			break;
		}
		case "model_fallback":
		{
			JsonElement value3;
			string text4 = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("model", out value3) && value3.ValueKind == JsonValueKind.String) ? (value3.GetString() ?? "") : "");
			PageBase.Toast(I18n.L("已切换备用模型"), text4, ToastKind.Warning);
			Add(Note((text4.Length > 0) ? I18n.L("主模型暂时不可用，本轮已切换到 {0}", text4) : I18n.L("主模型暂时不可用，本轮已切换到备用模型")));
			break;
		}
		case "thinking":
			_status.Text = I18n.L("思考中…");
			OpenRound();
			break;
		case "think":
		{
			JsonElement value;
			bool flag = ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("after_tools", out value) && value.ValueKind == JsonValueKind.True;
			string thinking = (flag ? I18n.L("搜完了，正在整理…") : I18n.L("正在想…"));
			_status.Text = (flag ? I18n.L("搜完了，正在整理…") : I18n.L("思考中…"));
			OpenRound(thinking);
			break;
		}
		case "tool":
			if (IsMemoryTool(ev.Name))
			{
				CloseRound();
			}
			else if (ev.Name == "ask_user")
			{
				if (_roundBubble == null)
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
			if (IsMemoryTool(ev.Name))
			{
				break;
			}
			Line(name, I18n.L("执行中：") + text, ToolState.Running);
			_status.Text = I18n.L("正在执行操作…");
			break;
		case "tool_done":
		{
			CloseRound();
			if (IsMemoryTool(ev.Name) && AddMemoryTag(ev))
			{
				break;
			}
			Line(name, I18n.L("完成：") + text, ToolState.Done);
			string text2 = TaskIdOf(ev);
			if (text2.Length > 0 && !_taskCards.ContainsKey(text2))
			{
				AddTaskCard(text2, ev, text);
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

	private static bool IsMemoryTool(string? name)
	{
		return name == "remember" || name == "forget";
	}

	private static string PayloadStr(BridgeEvent ev, string prop)
	{
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty(prop, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	/// <summary>remember / forget 的结果显示成小标签；「已记住」带「撤销」。认不出结果时返回 false，按普通步骤显示。</summary>
	private bool AddMemoryTag(BridgeEvent ev)
	{
		string result = PayloadStr(ev, "result");
		string text;
		bool undoable = false;
		string remembered = null;
		if (ev.Name == "remember")
		{
			remembered = FileMemory.RememberedOf(result);
			if (remembered != null)
			{
				text = I18n.L("已记住：") + remembered;
				undoable = true;
			}
			else if (result.StartsWith("已经记过了", StringComparison.Ordinal))
			{
				int i = result.IndexOfAny(new char[2] { ':', '：' });
				text = I18n.L("已记住：") + ((i >= 0) ? result.Substring(i + 1).Trim() : result) + I18n.L("（之前就记过）");
			}
			else
			{
				return false;
			}
		}
		else
		{
			string forgotten = AiChatOps.ForgottenOf(result);
			if (forgotten == null)
			{
				return false;
			}
			text = I18n.L("已忘记：") + forgotten;
		}
		TextBlock label = Ui.Txt(text, 12.0, bold: false, "B.AccentText");
		label.TextTrimming = TextTrimming.CharacterEllipsis;
		label.MaxWidth = 560.0;
		label.ToolTip = text;
		SPanel row = Ui.H(6.0, AiIcons.Icon(ev.Name == "remember" ? "M19 21l-7-4-7 4V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z" : "M3 6h18 M8 6V4h8v2 M19 6l-1 14H6L5 6", 12.0, "B.AccentText").VCenter(), label.VCenter());
		Border tag = new Border
		{
			CornerRadius = new CornerRadius(999.0),
			Padding = new Thickness(10.0, 3.0, 6.0, 3.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			Child = row
		};
		tag.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		if (undoable)
		{
			Button undo = null;
			undo = AiIcons.Flat("M3 7v6h6 M21 17a9 9 0 0 0-15-6.7L3 13", I18n.L("撤销"), I18n.L("删掉刚记下的这条记忆"), delegate
			{
				bool ok = FileMemory.Undo(remembered);
				row.Children.Remove(undo);
				label.Text = (ok ? I18n.L("已撤销：") : I18n.L("没找到这条记忆：")) + remembered;
				label.SetResourceReference(TextBlock.ForegroundProperty, "B.InkMuted");
			}, "B.InkMuted", 11.0, pill: true);
			row.Children.Add(undo.VCenter());
		}
		Add(tag);
		_group = null;
		_status.Text = I18n.L("操作完成");
		return true;
	}

	private void AddTaskCard(string taskId, BridgeEvent ev, string label)
	{
		string tool = ev.Name ?? "";
		string instance = PayloadStr(ev, "instance");
		TaskCard card = new TaskCard(taskId, label, tool, instance);
		if (instance.Length > 0 && tool.StartsWith("install_", StringComparison.Ordinal))
		{
			if (tool == "install_game")
			{
				card.Launch = delegate
				{
					string version = AiChatOps.InstalledVersionOf(card.FinishMessage);
					PageBase.Run(async delegate
					{
						PageBase.Win?.Navigate("launch");
						if (PageBase.Win?.GetPage("launch") is LaunchPage launchPage)
						{
							await launchPage.LaunchNowAsync(instance, (version.Length > 0) ? version : null);
						}
					}, I18n.L("启动失败"));
				};
			}
			card.OpenFolder = delegate
			{
				PageBase.Run(async delegate
				{
					if (tool == "install_mod")
					{
						await PageBase.Api.CallAsync("open_mods_folder", new { instance });
					}
					else
					{
						await PageBase.Api.CallAsync("open_instance_folder", new
						{
							name = instance
						});
					}
				}, I18n.L("打开文件夹失败"));
			};
		}
		card.Retry = delegate(string reason)
		{
			PageBase.Run(() => SendTextAsync(I18n.L("刚才的后台任务「{0}」失败了（{1}），请重试", card.TitleText, reason)));
		};
		_taskCards[taskId] = card;
		Add(card);
		_group = null;
		PageBase.Run(async delegate
		{
			string title = await PageBase.Api.TryCallAsync("task_title", new
			{
				task_id = taskId
			}, "");
			if (!string.IsNullOrWhiteSpace(title) && title != taskId)
			{
				card.SetTitle(title);
			}
		});
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

	private void ShowUsage(BridgeEvent ev)
	{
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
		{
			long num = GetNum("input_estimated");
			long num2 = GetNum("input") + num;
			long num3 = GetNum("output");
			if (num2 != 0L || num3 != 0L)
			{
				JsonElement value;
				long value2;
				string arg = ((u.TryGetProperty("real_requests", out value) && value.TryGetInt64(out value2) && value2 > 0 && num == 0L) ? "" : I18n.L("（估算）"));
				long requests = GetNum("requests");
				double num4 = AiUiText.ContextRatio(num2, requests, _contextWindow);
				_ringHost.Content = AiIcons.Ring(num4);
				_usageLabel.Text = $"{num4 * 100.0:0}% · {AiUiText.Tokens(num2)}";
				string toolTip = string.Format(I18n.L("本会话：输入 {0} / 输出 {1} tokens{2}"), Fmt(num2), Fmt(num3), arg) + "\n" + string.Format(I18n.L("上下文窗口 {0}，圆环为每轮平均占用的估算"), AiUiText.Tokens(_contextWindow));
				_usageLabel.ToolTip = toolTip;
				_ringHost.ToolTip = toolTip;
			}
		}
		static string Fmt(long n)
		{
			if (n < 1000)
			{
				return n.ToString();
			}
			return $"{(double)n / 1000.0:0.0}k";
		}
		long GetNum(string prop)
		{
			if (!u.TryGetProperty(prop, out var value3) || !value3.TryGetInt64(out var value4))
			{
				return 0L;
			}
			return value4;
		}
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
		string reason = ((ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("reason", out value) && value.ValueKind == JsonValueKind.String) ? (value.GetString() ?? "") : "") ?? "";
		List<string> list = new List<string>();
		if (ev.Payload.ValueKind == JsonValueKind.Object && ev.Payload.TryGetProperty("preview", out var value2) && value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("lines", out var value3) && value3.ValueKind == JsonValueKind.Array)
		{
			if (value2.TryGetProperty("head", out var value4) && value4.ValueKind == JsonValueKind.String)
			{
				string text = value4.GetString() ?? "";
				if (text.Length > 0)
				{
					list.Add(text.StartsWith("@@", StringComparison.Ordinal) ? text : ("@@ " + text));
				}
			}
			foreach (JsonElement item in value3.EnumerateArray())
			{
				if (item.ValueKind == JsonValueKind.String)
				{
					string text2 = item.GetString() ?? "";
					if (text2.Length > 0)
					{
						list.Add(text2);
					}
				}
			}
		}
		ClearDock();
		ConfirmCard card = null;
		card = new ConfirmCard(ev.Name ?? "", label, reason, list, ConfirmCard.AllowAlways(ev), delegate(bool ok, bool always, string scope)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_confirm", new { ok, always, scope });
			});
		});
		card.Resolved += delegate(bool ok)
		{
			if (_pendingConfirm == card)
			{
				_pendingConfirm = null;
				_dock.Children.Remove(card);
			}
			TextBlock textBlock = Ui.Txt((ok ? I18n.L("已允许：") : I18n.L("已拒绝：")) + label, 12.0, bold: false, ok ? "B.InkMuted" : "B.Danger");
			textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
			SPanel sPanel = Ui.H(6.0, AiIcons.Icon(ok ? "M20 6L9 17l-5-5" : "M18 6L6 18 M6 6l12 12", 12.0, ok ? "B.Accent" : "B.Danger").VCenter(), textBlock.VCenter());
			sPanel.Margin = new Thickness(6.0, 0.0, 0.0, 0.0);
			if (_group != null && _group.Parent == _messages)
			{
				_messages.Children.Insert(_messages.Children.IndexOf(_group) + 1, sPanel);
			}
			else
			{
				_messages.Children.Add(sPanel);
			}
			_status.Text = (ok ? I18n.L("正在执行操作…") : I18n.L("已拒绝"));
			SyncPlaceholder();
			ScrollDown();
		};
		_pendingConfirm = card;
		_dock.Children.Add(card);
		SyncDockHeight();
		Motion.FadeIn(card, 200.0, 8.0);
		_status.Text = I18n.L("等你确认");
		SyncPlaceholder();
		ScrollDown();
	}

	private void ShowAsk(BridgeEvent ev)
	{
		CloseRound();
		if (ev.Payload.ValueKind != JsonValueKind.Object || !ev.Payload.TryGetProperty("questions", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.TryCallAsync<object>("ai_answer", new
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
				await PageBase.Api.TryCallAsync<object>("ai_answer", new { result });
			});
		});
		Add(el);
		_status.Text = I18n.L("等你回答");
	}

	internal void SelfTestLoad(AiStoreDto store)
	{
		_store = store;
		RenderChats();
		RenderMessages();
	}

	internal void SelfTestUser(string text)
	{
		_runChatId = _store.ActiveId;
		SetBusy(on: true);
		Add(new Bubble("user", text, RetryFrom));
	}

	internal void SelfTestBusy(bool on)
	{
		SetBusy(on);
	}

	internal void SelfTestScrollEnd()
	{
		_scroll.ScrollToEnd();
	}

	public override void OnShown()
	{
		_input.Focus();
	}
}
