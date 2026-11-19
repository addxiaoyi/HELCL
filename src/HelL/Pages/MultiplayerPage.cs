using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class MultiplayerPage : PageBase
{
	private const string ProjectHome = "https://github.com/burningtnt/Terracotta";

	private readonly TextBlock _state = new TextBlock
	{
		FontSize = 18.0,
		FontWeight = FontWeights.SemiBold
	};

	private readonly TextBlock _detail = Ui.Muted("");

	private readonly TextBlock _lan = Ui.Small("");

	private readonly TextBlock _nodes = Ui.Small("");

	private readonly TextBlock _room = new TextBlock
	{
		FontSize = 21.0,
		FontWeight = FontWeights.Bold
	};

	private readonly TextBlock _roomHint = Ui.Small("");

	private readonly Border _roomCard;

	private readonly SPanel _actions = Ui.V(10.0);

	private readonly SPanel _players = Ui.V(8.0);

	private readonly TextBlock _playersTitle = Ui.Txt(I18n.L("房间成员"), 13.0, bold: true);

	private readonly Border _dot = new Border
	{
		Width = 10.0,
		Height = 10.0,
		CornerRadius = new CornerRadius(5.0)
	};

	private readonly DispatcherTimer _timer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(1200.0)
	};

	private bool _busy;

	private bool _polling;

	private bool _autoStarted;

	private string _uiState = "";

	private string _playerSig = "";

	private string _lastState = "";

	private string _prepareTask = "";

	public override string Title => I18n.L("联机");

	public MultiplayerPage()
	{
		Button button = Ui.Btn(I18n.L("允许访问"), BtnKind.Primary, delegate
		{
			PageBase.Run(AllowFirewallAsync);
		}, "\uea18");
		Button button2 = Ui.Btn(I18n.L("打开设置"), BtnKind.Chip, delegate
		{
			PageBase.Run(OpenFirewallAsync);
		});
		Button button3 = Ui.Btn(I18n.L("关闭内核"), BtnKind.Ghost, delegate
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.CallAsync("terracotta_shutdown");
				await ReloadAsync();
			});
		});
		SPanel border = Ui.V(10.0, Ui.H(10.0, _dot.VCenter(), _state), _detail.Wrap(), Ui.H(8.0, button, button2, button3), Ui.Sep());
		Button el = Ui.Btn(I18n.L("复制邀请码"), BtnKind.Chip, delegate
		{
			Copy(_room.Text);
		}, "\ue8c8");
		_room.SetResourceReference(TextBlock.ForegroundProperty, "B.AccentDeep");
		_roomCard = Ui.Card(Ui.V(6.0, Ui.Small(I18n.L("邀请码")), Ui.H(10.0, _room.VCenter(), el.VCenter()), _roomHint.Wrap()));
		_roomCard.Visibility = Visibility.Collapsed;
		_playersTitle.Visibility = Visibility.Collapsed;
		SPanel sPanel = Ui.H(10.0, Ui.Btn(I18n.L("Terracotta 项目主页"), BtnKind.Ghost, delegate
		{
			Ui.OpenUrl("https://github.com/burningtnt/Terracotta");
		}, "\ue71b"), Ui.Small(I18n.L("Terracotta | 陶瓦联机  © burningtnt  ·  基于 EasyTier")).VCenter());
		base.Content = PageBase.ScrollBody(Ui.Section(I18n.L("陶瓦联机"), I18n.L("输入邀请码加入，P2P 打洞直连；与 HMCL 共用会合节点，局域网地址见下方")), _lan.Wrap(), border, _roomCard, _actions, _playersTitle, _players, _nodes.Wrap(), sPanel);
		_timer.Tick += delegate
		{
			PageBase.Run(ReloadAsync);
		};
	}

	protected override async Task LoadAsync()
	{
		await LoadLanAsync();
		await ReloadAsync();
	}

	public override void OnShown()
	{
		_timer.Start();
		PageBase.Run(MaybePrepareAsync);
	}

	public override void OnHidden()
	{
		_timer.Stop();
	}

	private async Task LoadLanAsync()
	{
		string hint = (await PageBase.Api.TryCallAsync("lan_hint", new
		{
			port = 25565
		}, "")) ?? "";
		List<string> list = (await PageBase.Api.TryCallAsync("local_ips", null, new List<string>())) ?? new List<string>();
		var formattedIps = MultiplayerAndDialogUtils.FormatLanAddresses(list, 25565);
		_lan.Text = ((formattedIps.Count > 0) ? I18n.L("本机局域网地址：{0}", string.Join(I18n.L("、"), formattedIps)) : hint);
		_lan.ToolTip = (string.IsNullOrWhiteSpace(hint) ? null : hint);
	}

	private async Task ReloadAsync()
	{
		if (_polling)
		{
			return;
		}
		_polling = true;
		try
		{
			TerracottaSnap terracottaSnap = await PageBase.Api.TryCallAsync<TerracottaSnap>("terracotta_snapshot");
			if (terracottaSnap != null)
			{
				Render(terracottaSnap);
			}
		}
		finally
		{
			_polling = false;
		}
	}

	private async Task MaybePrepareAsync()
	{
		if (!_autoStarted && !_busy)
		{
			TerracottaSnap terracottaSnap = await PageBase.Api.TryCallAsync<TerracottaSnap>("terracotta_snapshot");
			if (terracottaSnap != null && terracottaSnap.Supported && terracottaSnap.Installed && !terracottaSnap.Running)
			{
				_autoStarted = true;
				await PrepareAsync(null);
			}
		}
	}

	private void Render(TerracottaSnap s)
	{
		string text = (string.IsNullOrEmpty(s.State) ? "missing" : s.State);
		bool flag = _busy;
		if (flag)
		{
			flag = ((text == "missing" || text == "idle") ? true : false);
		}
		if (flag)
		{
			text = "installing";
		}
		else
		{
			switch (text)
			{
			case "waiting":
			case "host-ok":
			case "guest-ok":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag || text == "idle")
			{
				_busy = false;
			}
		}
		_state.Text = ((text == "exception" || text == "fatal") ? I18n.L("加入失败") : (string.IsNullOrWhiteSpace(s.Label) ? text : s.Label));
		_dot.SetResourceReference(Border.BackgroundProperty, DotBrush(text));
		string text2 = ((!string.IsNullOrWhiteSpace(s.Error)) ? s.Error : s.Label);
		if (!string.IsNullOrWhiteSpace(s.ErrorHint))
		{
			text2 = (s.Error + "\n" + s.ErrorHint).Trim();
		}
		else if (!string.IsNullOrWhiteSpace(s.DifficultyHint))
		{
			text2 = (s.Label + "\n" + s.DifficultyHint).Trim();
		}
		List<string> list = new List<string>();
		if (!s.Supported)
		{
			list.Add(I18n.L("当前系统架构不支持"));
		}
		list.Add(s.Installed ? I18n.L("内核已安装") : I18n.L("内核未安装"));
		list.Add(s.Running ? I18n.L("内核运行中") : I18n.L("内核未运行"));
		if (!string.IsNullOrWhiteSpace(s.Player))
		{
			list.Add(I18n.L("玩家 ") + s.Player);
		}
		if (s.GameRunning)
		{
			list.Add(I18n.L("游戏运行中"));
		}
		_detail.Text = string.Join(" · ", list) + ((string.IsNullOrWhiteSpace(text2) || string.Equals(text2, _state.Text)) ? "" : ("\n" + text2));
		bool flag2 = !string.IsNullOrWhiteSpace(s.Room) || !string.IsNullOrWhiteSpace(s.Url);
		_roomCard.Show(flag2);
		if (flag2)
		{
			_room.Text = (string.IsNullOrWhiteSpace(s.Room) ? I18n.L("陶瓦联机大厅") : s.Room);
			_roomHint.Text = (string.IsNullOrWhiteSpace(s.Url) ? I18n.L("让好友在联机页选「我想当房客」，填这串邀请码。") : I18n.L("启动游戏 → 多人游戏 → 双击「陶瓦联机大厅」。"));
		}
		_nodes.Text = ((s.Nodes.Count == 0) ? "" : I18n.L("会合节点 {0} 个：{1}", s.Nodes.Count, string.Join("  ", s.Nodes.Take(3))));
		if (text != _uiState)
		{
			_uiState = text;
			FillActions(text, s);
		}
		RenderPlayers(s.Profiles);
		if (!string.IsNullOrWhiteSpace(s.Room) && text == "host-ok" && _lastState != "host-ok")
		{
			Copy(s.Room, I18n.L("已把邀请码复制到剪贴板"));
		}
		_lastState = text;
	}

	private static string DotBrush(string state)
	{
		switch (state)
		{
		case "host-ok":
		case "waiting":
		case "guest-ok":
			return "B.Ok";
		case "idle":
			return "B.Accent";
		case "exception":
		case "fatal":
		case "unsupported":
			return "B.Danger";
		case "missing":
			return "B.Warn";
		default:
			return "B.InkFaint";
		}
	}

	private void RenderPlayers(List<TerracottaProfile> profiles)
	{
		string text = string.Join("|", profiles.Select((TerracottaProfile p) => p.Name + "/" + p.Kind + "/" + p.Vendor));
		if (text == _playerSig)
		{
			return;
		}
		_playerSig = text;
		_players.Children.Clear();
		_playersTitle.Show(profiles.Count > 0);
		foreach (TerracottaProfile profile in profiles)
		{
			bool flag = string.Equals(profile.Kind, "HOST", StringComparison.OrdinalIgnoreCase);
			Grid grid = Ui.G(null, "Auto,*,Auto");
			grid.Add(new ThumbTile(profile.Name, 36.0, 18.0).M(0.0, 0.0, 12.0), 0);
			grid.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(profile.Name) ? I18n.L("玩家") : profile.Name, 13.0, bold: true).Trim(), Ui.Small(string.IsNullOrWhiteSpace(profile.Vendor) ? (flag ? I18n.L("房主") : I18n.L("成员")) : profile.Vendor)).VCenter(), 0, 1);
			grid.Add(Ui.Tag(flag ? I18n.L("房主") : I18n.L("成员"), "B.OnAccentStrong", "B.AccentStrong").VCenter(), 0, 2);
			_players.Children.Add(Ui.Card(grid, 12.0));
		}
		Motion.Stagger(_players, 18.0, 190.0, 8.0);
	}

	private void FillActions(string state, TerracottaSnap s)
	{
		_actions.Children.Clear();
		if (state == null)
		{
			return;
		}
		switch (state.Length)
		{
		default:
			return;
		case 7:
			switch (state[0])
			{
			default:
				return;
			case 'm':
				if (state == "missing")
				{
					Action(I18n.L("瓦"), I18n.L("下载陶瓦联机内核"), I18n.L("首次使用需要下载约 8 MB 的官方内核，之后可直接开房。"), I18n.L("下载"), () => PrepareAsync(_actions), primary: true);
				}
				return;
			case 'w':
				if (state == "waiting")
				{
					Action(I18n.L("房"), I18n.L("我想当房主"), I18n.L("创建房间并生成邀请码，与好友一起畅玩。"), I18n.L("创建"), HostAsync, primary: true);
					Action(I18n.L("客"), I18n.L("我想当房客"), I18n.L("输入房主提供的邀请码加入游戏世界。"), I18n.L("加入"), JoinAsync);
				}
				return;
			case 'h':
				if (state == "host-ok")
				{
					Action(I18n.L("复"), I18n.L("复制邀请码"), I18n.L("好友在联机页选「我想当房客」并填这串邀请码即可加入。"), I18n.L("复制"), delegate
					{
						Copy(_room.Text, I18n.L("已把邀请码复制到剪贴板"));
						return Task.CompletedTask;
					}, primary: true);
					Action(I18n.L("返"), I18n.L("退出"), I18n.L("这会彻底关闭房间，其他房客将一并退出。"), I18n.L("退出"), BackAsync);
				}
				return;
			case 'u':
				break;
			}
			if (!(state == "unknown"))
			{
				return;
			}
			goto IL_0437;
		case 9:
		{
			char c = state[0];
			if (c != 'e')
			{
				if (c != 'l' || !(state == "launching"))
				{
					return;
				}
				goto IL_0437;
			}
			if (!(state == "exception"))
			{
				return;
			}
			break;
		}
		case 13:
			switch (state[6])
			{
			default:
				return;
			case 'c':
				if (!(state == "host-scanning"))
				{
					return;
				}
				break;
			case 't':
				if (!(state == "host-starting"))
				{
					return;
				}
				break;
			}
			Action(I18n.L("扫"), I18n.L("正在扫描局域网世界"), I18n.L("请启动游戏，进入单人世界，按 ESC，选择对局域网开放。"), I18n.L("退出"), BackAsync);
			return;
		case 11:
			if (state == "unsupported")
			{
				Action("!", I18n.L("当前系统不支持"), I18n.L("陶瓦联机暂未提供此架构的官方内核。"), I18n.L("了解"), delegate
				{
					Ui.OpenUrl("https://github.com/burningtnt/Terracotta");
					return Task.CompletedTask;
				});
			}
			return;
		case 4:
			if (state == "idle")
			{
				Action("▶", I18n.L("启动联机内核"), I18n.L("内核已安装，点一下即可开始联机。"), I18n.L("启动"), () => PrepareAsync(_actions), primary: true);
			}
			return;
		case 10:
			if (!(state == "installing"))
			{
				return;
			}
			goto IL_0437;
		case 16:
			if (!(state == "guest-connecting"))
			{
				return;
			}
			goto IL_03e6;
		case 14:
			if (!(state == "guest-starting"))
			{
				return;
			}
			goto IL_03e6;
		case 8:
			if (state == "guest-ok")
			{
				Action(I18n.L("进"), I18n.L("进入世界"), I18n.L("启动游戏后到多人游戏双击「陶瓦联机大厅」，或点这里直接进入。"), I18n.L("进入"), EnterWorldAsync, primary: true);
				Action(I18n.L("返"), I18n.L("退出"), I18n.L("这不影响其他房客加入当前房间。"), I18n.L("退出"), BackAsync);
			}
			return;
		case 5:
			if (!(state == "fatal"))
			{
				return;
			}
			break;
		case 6:
		case 12:
		case 15:
			return;
			IL_03e6:
			Action(I18n.L("连"), I18n.L("正在加入房间"), string.IsNullOrWhiteSpace(s.DifficultyHint) ? I18n.L("正在与房主建立连接。") : s.DifficultyHint, I18n.L("退出"), BackAsync);
			return;
			IL_0437:
			Action("…", I18n.L("请稍候"), string.IsNullOrWhiteSpace(s.Label) ? I18n.L("正在准备联机内核。") : s.Label, I18n.L("刷新"), ReloadAsync);
			return;
		}
		Action("!", I18n.L("联机失败"), (!string.IsNullOrWhiteSpace(s.ErrorHint)) ? s.ErrorHint : (string.IsNullOrWhiteSpace(s.Error) ? I18n.L("请返回后重试，或检查网络。") : s.Error), I18n.L("返回"), BackAsync);
		Action(I18n.L("直"), I18n.L("朋友是公网就直连"), I18n.L("让他把单人世界对局域网开放，并在路由映射该端口，然后填他的公网 IP:端口。"), I18n.L("直连"), DirectConnectAsync);
		Action(I18n.L("启"), I18n.L("重新启动内核"), I18n.L("若内核已退出，点此重新拉起。"), I18n.L("重启"), () => PrepareAsync(_actions));
	}

	private void Action(string letter, string title, string desc, string btnText, Func<Task> act, bool primary = false)
	{
		Button button = Ui.Btn(btnText, (!primary) ? BtnKind.Normal : BtnKind.Primary, delegate
		{
			PageBase.Run(act);
		});
		button.MinWidth = 96.0;
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(new ThumbTile(letter, 46.0, 12.0).M(0.0, 0.0, 14.0), 0);
		grid.Add(Ui.V(2.0, Ui.Txt(title, 13.5, bold: true), Ui.Small(desc).Wrap()).VCenter(), 0, 1);
		grid.Add(button.VCenter().M(14.0, 0.0, 0.0, 0.0), 0, 2);
		Border border = Ui.RowCard(grid, padding: 14.0);
		_actions.Children.Add(border);
	}

	private async Task PrepareAsync(FrameworkElement? anchor)
	{
		if (!_busy)
		{
			_busy = true;
			_prepareTask = await PageBase.Api.StartTaskAsync("terracotta_prepare");
			if (anchor != null && SettingsPage.FlyEnabled)
			{
				PageBase.Win?.FlyToTasks(anchor, I18n.L("联"));
			}
			PageBase.Toast(I18n.L("正在准备联机内核"), I18n.L("进度看下载任务"));
			await ReloadAsync();
		}
	}

	private async Task HostAsync()
	{
		string player = await PageBase.Api.TryCallAsync("terracotta_player", null, "Player");
		TerracottaSnap terracottaSnap = await PageBase.Api.TryCallAsync<TerracottaSnap>("terracotta_snapshot");
		if (terracottaSnap != null && !terracottaSnap.GameRunning)
		{
			if (!(await Dlg.Confirm(I18n.L("您似乎忘记启动游戏了"), I18n.L("将以「{0}」的身份开房。\n请先启动游戏，进入单人世界，按 ESC，选择「对局域网开放」。", player), I18n.L("游戏已启动"), I18n.L("取消"))))
			{
				return;
			}
		}
		else
		{
			PageBase.Toast(I18n.L("正在开房"), I18n.L("以「{0}」的身份创建房间", player));
		}
		await PageBase.Api.CallAsync("terracotta_host");
		await ReloadAsync();
	}

	private async Task JoinAsync()
	{
		string text = await Dlg.Prompt(I18n.L("我想当房客"), I18n.L("请输入房主提供的邀请码"), "", "U/XXXX-XXXX-XXXX-XXXX");
		if (!string.IsNullOrWhiteSpace(text))
		{
			string code = MultiplayerAndDialogUtils.NormalizeRoomCode(text);
			await PageBase.Api.CallAsync("terracotta_join", new
			{
				room = code
			});
			await ReloadAsync();
		}
	}

	private async Task DirectConnectAsync()
	{
		string text = await Dlg.Prompt(I18n.L("公网直连"), I18n.L("朋友需先对局域网开放世界，并在路由器映射该端口，然后填他的公网地址。"), "", I18n.L("例如 1.2.3.4:25565"));
		if (!string.IsNullOrWhiteSpace(text))
		{
			var (host, port) = LaunchUtils.ParseServerAddress(text);
			string addr = host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";
			string text2 = await PageBase.Api.CallAsync<string>("terracotta_direct_connect", new
			{
				address = addr
			});
			PageBase.Toast(I18n.L("正在直连"), string.IsNullOrWhiteSpace(text2) ? I18n.L("启动后会进入该服务器。") : text2, ToastKind.Success);
			await ReloadAsync();
		}
	}

	private async Task EnterWorldAsync()
	{
		string text = (await PageBase.Api.CallAsync<string>("terracotta_enter_world")) ?? "";
		if (text.StartsWith("task-", StringComparison.Ordinal))
		{
			if (SettingsPage.FlyEnabled)
			{
				PageBase.Win?.FlyToTasks(_actions, I18n.L("进"));
			}
			PageBase.Toast(I18n.L("正在启动游戏"), I18n.L("启动后会直接进入陶瓦联机大厅"), ToastKind.Success);
		}
		else
		{
			PageBase.Toast(I18n.L("已加入房间"), string.IsNullOrWhiteSpace(text) ? I18n.L("请到多人游戏双击「陶瓦联机大厅」。") : text, ToastKind.Success);
		}
		await ReloadAsync();
	}

	private async Task BackAsync()
	{
		await PageBase.Api.CallAsync("terracotta_idle");
		await ReloadAsync();
	}

	private async Task AllowFirewallAsync()
	{
		string text = await PageBase.Api.CallAsync<string>("terracotta_allow_firewall");
		PageBase.Toast(I18n.L("防火墙"), string.IsNullOrWhiteSpace(text) ? I18n.L("已提交规则") : text, ToastKind.Success);
	}

	private async Task OpenFirewallAsync()
	{
		try
		{
			await PageBase.Api.CallAsync("terracotta_open_firewall_settings");
		}
		catch (BridgeCallException)
		{
			Ui.OpenUrl("ms-settings:windowsdefender");
		}
	}

	private void Copy(string text, string? title = null)
	{
		string trimmed = (text ?? "").Trim();
		if (trimmed.Length == 0)
		{
			return;
		}
		Ui.Copy(trimmed, title, trimmed);
	}

	public override void OnEvent(BridgeEvent ev)
	{
		if (ev.Event != "finished")
		{
			return;
		}
		PageBase.Run(async delegate
		{
			string text = TaskStore.Get(ev.TaskId)?.Title;
			if (text == null)
			{
				text = (await PageBase.Api.TryCallAsync("task_title", new
				{
					task_id = ev.TaskId
				}, "")) ?? "";
			}
			string text2 = text;
			if (!(ev.TaskId != _prepareTask) || text2.Contains("陶瓦", StringComparison.Ordinal))
			{
				_busy = false;
				if (ev.TaskId == _prepareTask)
				{
					_prepareTask = "";
				}
				if (!ev.Success)
				{
					PageBase.Toast(string.IsNullOrWhiteSpace(text2) ? I18n.L("联机内核") : text2, ev.Message, ToastKind.Error);
				}
				await ReloadAsync();
			}
		});
	}
}
