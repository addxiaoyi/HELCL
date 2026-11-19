using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public static class GlobalModsDialog
{
	public static async Task ShowAsync()
	{
		BridgeClient api = AppServices.Client;
		SPanel list = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(list);
		smoothScroll.MaxHeight = 380.0;
		smoothScroll.MinWidth = 520.0;
		List<GlobalModRow> allMods = new List<GlobalModRow>();

		TextBox searchBox = Ui.Input(I18n.L("搜索全局模组…"), "", 180.0);
		searchBox.TextChanged += delegate
		{
			RenderFiltered();
		};

		Button importBtn = Ui.Btn(I18n.L("导入 jar"), BtnKind.Soft, delegate
		{
			PageBase.Run(async delegate
			{
				string[] files = Dlg.PickFiles(I18n.L("模组 (*.jar)|*.jar"), I18n.L("导入全局模组"));
				if (files != null && files.Length > 0)
				{
					foreach (string path in files)
					{
						string fn = Path.GetFileName(path);
						await api.StartTaskAsync("install_mod", new
						{
							name = fn,
							instance = "",
							extra = new
							{
								path = path,
								name = fn,
								is_global = true
							}
						});
					}
					AppServices.Toast(I18n.L("已加入队列"), I18n.L("正在导入 {0} 个全局模组", files.Length), ToastKind.Success);
					await Reload();
				}
			}, I18n.L("导入失败"));
		}, "\ue8b5");

		Button button = Ui.Btn(I18n.L("打开目录"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => api.CallAsync("open_global_mods"));
		}, "\ue8b7");

		Button button2 = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(Reload, I18n.L("读取失败"));
		}, "\ue72c");

		Grid topBar = Ui.G(null, "*,Auto");
		topBar.Add(Ui.H(8.0, importBtn, button, button2).VCenter(), 0);
		topBar.Add(searchBox.VCenter(), 0, 1);

		Dlg.Panel(I18n.L("全局模组"), Ui.V(12.0, topBar, smoothScroll), 620.0);
		await Reload();

		async Task Reload()
		{
			(List<GlobalModRow> rows, string err) = await api.TryCallAsyncOrError("list_global_mods", null, new List<GlobalModRow>());
			allMods = rows ?? new List<GlobalModRow>();
			if (allMods.Count == 0 && err != null)
			{
				list.Children.Clear();
				list.Children.Add(Ui.EmptyError(I18n.L("读取全局模组失败"), err, delegate
				{
					PageBase.Run(Reload);
				}));
				return;
			}
			RenderFiltered();
		}

		void RenderFiltered()
		{
			list.Children.Clear();
			string q = searchBox.Text?.Trim() ?? "";
			var filtered = (q.Length == 0)
				? allMods
				: allMods.Where(m => m.Filename.Contains(q, StringComparison.OrdinalIgnoreCase) || InstanceUtils.MatchesPinyinOrSubstr(m.Filename, q)).ToList();

			if (filtered.Count == 0)
			{
				list.Children.Add(Ui.Empty("\ue80a", I18n.L("没有匹配的全局模组"), I18n.L("放进去的模组会自动注入每个实例")));
				return;
			}

			foreach (GlobalModRow item in filtered)
			{
				string file = item.Filename;
				CheckBox checkBox = Ui.Switch(item.Enabled);
				checkBox.Checked += delegate
				{
					PageBase.Run(() => api.TryCallAsync<object>("set_global_mod_enabled", new
					{
						filename = file,
						enabled = true
					}));
				};
				checkBox.Unchecked += delegate
				{
					PageBase.Run(() => api.TryCallAsync<object>("set_global_mod_enabled", new
					{
						filename = file,
						enabled = false
					}));
				};
				Grid grid = Ui.G(null, "Auto,*,Auto");
				grid.Add(checkBox.M(0.0, 0.0, 12.0).VCenter(), 0);
				grid.Add(Ui.Txt(file, 12.5).Trim().VCenter(), 0, 1);
				grid.Add(Ui.Small(item.Size).VCenter(), 0, 2);
				list.Children.Add(Ui.RowCard(grid, null, 9.0));
			}
		}
	}
}
