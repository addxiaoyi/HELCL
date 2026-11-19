using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

public static class FirstRunWizard
{
	private static readonly (string Title, string Body)[] Highlights = new(string, string)[6]
	{
		(I18n.L("把文件直接拖进窗口"), I18n.L("整合包、模组、资源包、光影、存档、皮肤、壁纸都认；认不准会问你一句，不用先找对页面。")),
		(I18n.L("第三方启动器一键迁移"), I18n.L("支持自动识别并无缝继承 PCL2、HMCL、Mojang 官方启动器的已有游戏版本、存档与模组。")),
		(I18n.L("侧栏是可以排的"), I18n.L("「游戏」「更多」里的子页拖到侧栏就固定成一级项，从侧栏拖回去就还原；排法和显隐在设置里。")),
		(I18n.L("换个背景"), I18n.L("设置页能设静态壁纸、mp4 动态壁纸，或者指一个文件夹轮播；模糊和遮罩也在那儿调。")),
		(I18n.L("AI 助手在侧栏「通用」组里"), I18n.L("崩溃日志看不懂、模组冲突理不清，可以直接问它。")),
		(I18n.L("这个向导随时能再看"), I18n.L("「设置 → 启动向导」点「重新运行」，目录、下载源、内存、隔离会再问一遍。"))
	};

	private static readonly (string Key, string Label)[] SrcOpts = new(string, string)[5]
	{
		("auto", I18n.L("自动（按当前网络选择）")),
		("official", I18n.L("仅官方")),
		("bmclapi", I18n.L("BMCLAPI 优先")),
		("mc_mirror", I18n.L("MC Mirror 优先")),
		("mc_mirror_west", I18n.L("MC Mirror 美西优先"))
	};

	private static readonly (string Key, string Label)[] IsoOpts = new(string, string)[4]
	{
		("all", I18n.L("完全独立（推荐）")),
		("mods", I18n.L("隔离 Mod 与配置")),
		("saves", I18n.L("隔离存档")),
		("none", I18n.L("不隔离"))
	};

	private static readonly string[] Hints = new string[4]
	{
		I18n.L("先把游戏目录和下载源定下来。"),
		I18n.L("版本都会装在这个目录里；下载源决定从哪儿拉文件。"),
		I18n.L("这两项只影响以后新建的版本，已经装好的不受影响。"),
		I18n.L("最后几句，都是容易错过的地方。")
	};

	public static async Task MaybeShowAsync()
	{
		if (await AppServices.Client.TryCallAsync("get_setting", new
		{
			key = "first_run",
			@default = false
		}, fallback: false))
		{
			await ShowAsync();
		}
	}

	public static async Task ShowAsync()
	{
		BridgeClient api = AppServices.Client;
		Dictionary<string, JsonElement> settings = (await api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		TextBox gameDir = Ui.Input(I18n.L("游戏目录"), (Str("game_dir").Length > 0) ? Str("game_dir") : Str("root"));
		Button el = Ui.Btn(I18n.L("浏览…"), BtnKind.Chip, delegate
		{
			string text4 = Dlg.PickFolder(I18n.L("选择游戏目录"));
			if (text4 != null)
			{
				gameDir.Text = text4;
			}
		}, "\ue8b7");
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(gameDir, 0);
		grid.Add(el.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		var candidates = MultiplayerAndDialogUtils.FilterAvailableLaunchers(MultiplayerAndDialogUtils.GetKnownLauncherCandidatePaths(), System.IO.Directory.Exists);
		SPanel candPanel = Ui.H(6.0);
		if (candidates.Count > 0)
		{
			candPanel.Children.Add(Ui.Small(I18n.L("快捷继承：")).VCenter());
			foreach (var c in candidates)
			{
				var cur = c;
				string label = cur.Name.Contains("PCL") ? "PCL2 目录" : (cur.Name.Contains("HMCL") ? "HMCL 目录" : "官方启动器");
				candPanel.Children.Add(Ui.Btn(label, BtnKind.Ghost, delegate
				{
					gameDir.Text = cur.Path;
				}));
			}
		}
		ComboBox src = Ui.Combo(SrcOpts.Select(((string Key, string Label) o) => o.Label));
		src.SelectedIndex = Math.Max(0, Array.FindIndex(SrcOpts, delegate((string Key, string Label) o)
		{
			string item = o.Key;
			string text4 = Str("download_source");
			return item == ((text4 != null && text4.Length > 0) ? text4 : "auto");
		}));
		Slider mem = Ui.Sld(512.0, 32768.0, Clamp.Of(Int("default_memory_mb", 4096), 512, 32768), 256.0);
		TextBlock memLbl = Ui.Txt($"{(int)mem.Value} MB", 12.0, bold: true);
		mem.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			memLbl.Text = $"{(int)e.NewValue} MB";
		};
		Grid grid2 = Ui.G(null, "*,Auto");
		grid2.Add(mem.VCenter(), 0);
		grid2.Add(memLbl.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		ComboBox iso = Ui.Combo(IsoOpts.Select(((string Key, string Label) o) => o.Label));
		iso.SelectedIndex = Math.Max(0, Array.FindIndex(IsoOpts, delegate((string Key, string Label) o)
		{
			string item = o.Key;
			string text4 = Str("default_isolation");
			return item == ((text4 != null && text4.Length > 0) ? text4 : "all");
		}));
		SPanel sPanel = Ui.V(6.0, Ui.Txt(I18n.L("三步就能开始玩"), 14.0, bold: true), Ui.Muted(I18n.L("1. 选游戏目录和下载源")), Ui.Muted(I18n.L("2. 定默认内存和版本隔离")), Ui.Muted(I18n.L("3. 看一眼几个容易错过的功能")), Ui.Small(I18n.L("这些以后都能在设置里改；不想现在弄就点「跳过向导」。")).Wrap());
		SPanel sPanel2 = Ui.V(8.0, Ui.Txt(I18n.L("游戏目录（版本都装在这里）")), grid, (candidates.Count > 0) ? candPanel : null, Ui.Txt(I18n.L("文件下载源")), src, Ui.Small(I18n.L("国内网络建议保持「自动」：官方慢的时候会自己切到 BMCLAPI 镜像。")).Wrap());
		SPanel sPanel3 = Ui.V(8.0, Ui.Txt(I18n.L("默认内存")), grid2, Ui.Txt(I18n.L("新版本默认隔离")), iso, Ui.Small(I18n.L("「完全独立」= 每个版本有自己的 mods / 配置 / 存档，换版本不会把上一个版本的模组拖进去。")).Wrap());
		SPanel sPanel4 = Ui.V(9.0);
		(string, string)[] highlights = Highlights;
		for (int num = 0; num < highlights.Length; num++)
		{
			var (text, text2) = highlights[num];
			sPanel4.Children.Add(Ui.Txt(text, 13.0, bold: true));
			sPanel4.Children.Add(Ui.Small(text2).Wrap());
		}
		UIElement[] pages = new UIElement[4] { sPanel, sPanel2, sPanel3, sPanel4 };
		TextBlock hint = Ui.Muted("");
		hint.TextWrapping = TextWrapping.Wrap;
		TextBlock step = Ui.Small("");
		Grid grid3 = new Grid
		{
			MinWidth = 470.0,
			MinHeight = 250.0
		};
		UIElement[] array = pages;
		foreach (UIElement uIElement in array)
		{
			uIElement.Visibility = Visibility.Collapsed;
			grid3.Children.Add(uIElement);
		}
		Button back = Ui.Btn(I18n.L("上一步"));
		Button next = Ui.Btn(I18n.L("下一步"), BtnKind.Primary);
		Button button = Ui.Btn(I18n.L("跳过向导"), BtnKind.Ghost);
		int index = 0;
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
		SPanel body = Ui.V(12.0, hint, grid3, Ui.Sep(), Ui.G(null, "Auto,*,Auto,Auto").Add(step.VCenter(), 0).Add(button.Right().VCenter(), 0, 1)
			.Add(back.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2)
			.Add(next.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 3));
		Dlg.Layer layer = null;
		layer = Dlg.Panel(I18n.L("欢迎使用 HelL"), body, 560.0, null, dismissable: true, delegate
		{
			tcs.TrySetResult(result: false);
		});
		back.Click += delegate
		{
			Show(index - 1);
		};
		next.Click += delegate
		{
			if (index < pages.Length - 1)
			{
				Show(index + 1);
			}
			else
			{
				tcs.TrySetResult(result: true);
				layer.Close();
			}
		};
		button.Click += delegate
		{
			tcs.TrySetResult(result: false);
			layer.Close();
		};
		Show(0);
		if (!(await tcs.Task))
		{
			await api.TryCallAsync<object>("save_settings", new
			{
				data = new
				{
					first_run = false
				}
			});
			return;
		}
		string typed = gameDir.Text?.Trim() ?? "";
		string text3 = ((Str("game_dir").Length > 0) ? Str("game_dir") : Str("root"));
		if (typed.Length > 0 && typed != text3)
		{
			try
			{
				await api.CallAsync<string>("set_game_dir", new
				{
					path = typed
				});
			}
			catch (Exception ex)
			{
				await Dlg.Alert(I18n.L("游戏目录没能设置"), I18n.L("{0}\n\n{1}\n\n已保留原来的目录，可到「设置」里重新选择。", typed, ex.Message));
			}
		}
		await api.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["download_source"] = SrcOpts[Math.Max(0, src.SelectedIndex)].Key,
				["default_memory_mb"] = (int)mem.Value,
				["default_isolation"] = IsoOpts[Math.Max(0, iso.SelectedIndex)].Key,
				["first_run"] = false
			}
		});
		AppServices.Toast(I18n.L("向导已完成"), I18n.L("目录、下载源、内存、隔离已按你的选择写入"), ToastKind.Success);
		if (AppServices.Window?.GetPage("settings") is SettingsPage settingsPage)
		{
			await settingsPage.RefreshAsync();
		}
		int Int(string k, int def)
		{
			if (!settings.TryGetValue(k, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
			{
				return def;
			}
			return value2;
		}
		void Show(int i)
		{
			index = Clamp.Of(i, 0, pages.Length - 1);
			for (int j = 0; j < pages.Length; j++)
			{
				pages[j].Visibility = ((j != index) ? Visibility.Collapsed : Visibility.Visible);
			}
			hint.Text = Hints[index];
			step.Text = I18n.L("第 {0} / {1} 步", index + 1, pages.Length);
			back.IsEnabled = index > 0;
			next.Content = ((index == pages.Length - 1) ? I18n.L("开始使用") : I18n.L("下一步"));
			Motion.FadeIn(pages[index], 200.0, 8.0);
		}
		string Str(string k)
		{
			if (!settings.TryGetValue(k, out var value) || value.ValueKind != JsonValueKind.String)
			{
				return "";
			}
			return value.GetString() ?? "";
		}
	}
}
