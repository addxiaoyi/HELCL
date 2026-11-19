using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class ServersPage : PageBase
{
	private readonly ComboBox _inst = Ui.Combo();

	private readonly SPanel _list = Ui.V(9.0);

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBlock _lan = Ui.Small("");

	public override string Title => I18n.L("服务器");

	public ServersPage()
	{
		Button button = Ui.Btn(I18n.L("添加服务器"), BtnKind.Primary, delegate
		{
			PageBase.Run(() => EditAsync(null, -1));
		}, "\ue710");
		Button button2 = Ui.Btn(I18n.L("导入"), BtnKind.Chip, delegate
		{
			PageBase.Run(ImportAsync);
		}, "\ue8b5");
		Button button3 = Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
		{
			PageBase.Run(ExportAsync);
		}, "\uede1");
		Grid grid = Ui.Section(I18n.L("服务器"), I18n.L("直接写进实例的 servers.dat，进游戏就能看到"), Ui.H(8.0, _count.VCenter(), button2, button3, button));
		_inst.SelectionChanged += delegate
		{
			PageBase.Run(ReloadAsync);
		};
		SPanel filters = Ui.H(10.0, Ui.Txt(I18n.L("实例"), 12.0, bold: false, "B.InkMuted").VCenter(), _inst.W(200.0), _lan.VCenter());
		filters.Wrap = true;
		base.Content = PageBase.ScrollBody(grid, filters, Ui.Sep(), _list);
	}

	protected override async Task LoadAsync()
	{
		List<InstanceInfo> source = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_inst.Fill(source.Select((InstanceInfo i) => i.Name));
		TextBlock lan = _lan;
		TextBlock textBlock = lan;
		textBlock.Text = (await PageBase.Api.TryCallAsync("lan_hint", new
		{
			port = 25565
		}, "")) ?? "";
		await ReloadAsync();
	}

	private async Task ReloadAsync()
	{
		string inst = _inst.Str();
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}
		(List<ServerRow> list, string err) = await PageBase.Api.TryCallAsyncOrError("list_servers", new
		{
			instance = inst
		}, new List<ServerRow>());
		list = list ?? new List<ServerRow>();
		_list.Children.Clear();
		_count.Text = I18n.L("{0} 个", list.Count);
		if (list.Count == 0)
		{
			if (err != null)
			{
				_list.Children.Add(Ui.EmptyError(I18n.L("读取服务器列表失败"), err, delegate
				{
					PageBase.Run(ReloadAsync);
				}));
				return;
			}
			SPanel empty = Ui.Empty("\ue968", I18n.L("还没有服务器"), I18n.L("点右上角添加，或从 servers.txt 导入"));
			_list.Children.Add(empty);
			return;
		}
		for (int i = 0; i < list.Count; i++)
		{
			int idx = i;
			ServerRow r = list[i];
			Grid grid = Ui.G(null, "Auto,*,Auto");
			grid.Add(Ui.Glyph("\ue968", 16.0, "B.Accent").M(0.0, 0.0, 12.0).VCenter(), 0);
			string displayName = MinecraftColorParser.StripColorCodes(string.IsNullOrWhiteSpace(r.Name) ? r.Ip : r.Name);
			string desc = string.IsNullOrWhiteSpace(r.Description) ? "" : ("  ·  " + MinecraftColorParser.StripColorCodes(r.Description));
			grid.Add(Ui.V(3.0, Ui.Txt(displayName, 13.5, bold: true).Trim(), Ui.Small($"{r.Ip}:{r.Port}" + desc)).VCenter(), 0, 1);
			grid.Add(Ui.H(6.0, Ui.Btn(I18n.L("进入"), BtnKind.Soft, delegate
			{
				PageBase.Run(() => JoinAsync(r));
			}, "\ue768"), Ui.Btn(I18n.L("编辑"), BtnKind.Chip, delegate
			{
				PageBase.Run(() => EditAsync(r, idx));
			}), Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
			{
				PageBase.Run(async delegate
				{
					if (await Dlg.Confirm(I18n.L("删除服务器"), I18n.L("从启动器列表里移除「{0}」的记录，不会影响服务器本身。", string.IsNullOrWhiteSpace(r.Name) ? r.Ip : r.Name), I18n.L("删除"), I18n.L("取消"), danger: true))
					{
						await PageBase.Api.CallAsync("delete_server", new
						{
							instance = inst,
							index = idx
						});
						await ReloadAsync();
					}
				});
			})).VCenter(), 0, 2);
			Border border = Ui.Card(grid, 13.0);
			border.ToolTip = I18n.L("双击直接进入服务器");
			border.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
			{
				if (e.ClickCount == 2 && !Ui.FromButton(e.OriginalSource as DependencyObject))
				{
					e.Handled = true;
					PageBase.Run(() => JoinAsync(r));
				}
			};
			_list.Children.Add(border);
		}
		Motion.Stagger(_list, 24.0);
	}

	private async Task JoinAsync(ServerRow row)
	{
		string inst = _inst.Str();
		List<string> list = (await PageBase.Api.TryCallAsync("get_installed_versions", new
		{
			instance = inst
		}, new List<string>())) ?? new List<string>();
		if (list.Count == 0)
		{
			PageBase.Toast(I18n.L("这个实例没有版本"), I18n.L("先去下载页装一个"), ToastKind.Warning);
			return;
		}
		ComboBox pick = Ui.Combo(list);
		if (await Dlg.Ask(I18n.L("进入 {0}", row.Name), Ui.V(8.0, Ui.Muted($"{row.Ip}:{row.Port}"), pick), I18n.L("启动并直连")))
		{
			string text = await AccountGate.EnsureAsync(PageBase.Win?.Prefs.LastUser);
			if (text != null)
			{
				BridgeClient api = PageBase.Api;
				string version = pick.Str();
				int memMb = 4096;
				int width = 854;
				int height = 480;
				string java = "自动选择";
				try
				{
					Dictionary<string, System.Text.Json.JsonElement> cfg = await api.TryCallAsync("get_config", null, new Dictionary<string, System.Text.Json.JsonElement>());
					if (cfg != null)
					{
						if (cfg.TryGetValue("memory_mb", out var mVal) && mVal.TryGetInt32(out var m) && m > 0) memMb = m;
						if (cfg.TryGetValue("window_width", out var wVal) && wVal.TryGetInt32(out var w) && w > 0) width = w;
						if (cfg.TryGetValue("window_height", out var hVal) && hVal.TryGetInt32(out var h) && h > 0) height = h;
						if (cfg.TryGetValue("default_java", out var jVal) && !string.IsNullOrWhiteSpace(jVal.GetString())) java = jVal.GetString();
					}
				}
				catch { }
				await api.StartTaskAsync("launch_game", new
				{
					instance = inst,
					version = version,
					account = text,
					username = text,
					memory_mb = memMb,
					width = width,
					height = height,
					java = java,
					extra_game_args = new string[4]
					{
						"--server",
						row.Ip,
						"--port",
						row.Port.ToString()
					}
				});
				PageBase.Win?.Navigate("launch");
			}
		}
	}

	private async Task EditAsync(ServerRow? row, int index)
	{
		string inst = _inst.Str();
		TextBox name = Ui.Input(I18n.L("显示名"), row?.Name ?? "");
		TextBox ip = Ui.Input("mc.example.com", row?.Ip ?? "");
		TextBox port = Ui.Input("25565", (row?.Port ?? 25565).ToString());
		TextBox desc = Ui.Input(I18n.L("备注（可空）"), row?.Description ?? "");
		SPanel body = Ui.V(8, Ui.Field(I18n.L("名称"), name, labelWidth: 86), Ui.Field(I18n.L("地址"), ip, labelWidth: 86), Ui.Field(I18n.L("端口"), port, labelWidth: 86), Ui.Field(I18n.L("备注"), desc, labelWidth: 86));
		bool saved = await Dlg.EditAsync((row == null) ? I18n.L("添加服务器") : I18n.L("编辑服务器"), body, async () =>
		{
			Dlg.Require(!string.IsNullOrWhiteSpace(inst), name, I18n.L("请先选择目标实例"));
			Dlg.Require(!string.IsNullOrWhiteSpace(name.Text), name, I18n.L("请填写服务器名称"));
			string finalIp = ip.Text?.Trim() ?? "";
			Dlg.Require(finalIp.Length > 0, ip, I18n.L("请填写服务器地址"));
			int port2 = 25565;
			Dlg.Require(string.IsNullOrWhiteSpace(port.Text) || (int.TryParse(port.Text.Trim(), out port2) && port2 >= 1 && port2 <= 65535), port, I18n.L("端口应为 1–65535 之间的整数"));
			if (finalIp.Contains(":") && (port2 == 25565 || string.IsNullOrWhiteSpace(port.Text)))
			{
				var (parsedHost, parsedPort) = LaunchUtils.ParseServerAddress(finalIp);
				finalIp = parsedHost;
				if (int.TryParse(parsedPort, out int parsedP))
				{
					port2 = parsedP;
				}
			}
			Dlg.Require(port2 >= 1 && port2 <= 65535, ip, I18n.L("地址中的端口应为 1–65535 之间的整数"));
			if (row != null)
			{
				await PageBase.Api.CallAsync("update_server", new
				{
					instance = inst,
					index = index,
					name = (name.Text?.Trim() ?? ""),
					ip = finalIp,
					port = port2,
					description = (desc.Text?.Trim() ?? "")
				});
			}
			else
			{
				await PageBase.Api.CallAsync("add_server", new
				{
					instance = inst,
					name = (name.Text?.Trim() ?? ""),
					ip = finalIp,
					port = port2,
					description = (desc.Text?.Trim() ?? "")
				});
			}
		});
		if (saved) await ReloadAsync();
	}

	private async Task ImportAsync()
	{
		string file = Dlg.PickFile(I18n.L("服务器列表 (*.txt)|*.txt|全部文件|*.*"), I18n.L("导入服务器"));
		if (file != null)
		{
			string text = await Task.Run(() => File.ReadAllText(file));
			int num = await PageBase.Api.CallAsync<int>("import_servers", new
			{
				instance = _inst.Str(),
				text = text
			});
			PageBase.Toast(I18n.L("已导入"), I18n.L("{0} 个服务器", num), ToastKind.Success);
			await ReloadAsync();
		}
	}

	private async Task ExportAsync()
	{
		string text = (await PageBase.Api.CallAsync<string>("export_servers", new
		{
			instance = _inst.Str()
		})) ?? "";
		string dest = Dlg.SaveFile(I18n.L("服务器列表 (*.txt)|*.txt"), "servers.txt", I18n.L("导出服务器"));
		if (dest != null)
		{
			await Task.Run(delegate
			{
				File.WriteAllText(dest, text);
			});
			PageBase.Toast(I18n.L("已导出"), dest, ToastKind.Success);
		}
	}

	public override async Task RefreshAsync()
	{
		await ReloadAsync();
	}
}
