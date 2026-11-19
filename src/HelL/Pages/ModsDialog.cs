using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public static class ModsDialog
{
	public static async Task ShowAsync(string instance, string version = "")
	{
		BridgeClient api = AppServices.Client;
		SPanel list = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(list);
		smoothScroll.MaxHeight = 400.0;
		smoothScroll.MinWidth = 620.0;
		TextBlock count = Ui.Muted("");
		ComboBox target = Ui.Combo(null, null, 230.0);
		List<ModsTarget> targets = new List<ModsTarget>();
		List<ModEntry> installedMods = new List<ModEntry>();
		bool sync = false;
		target.SelectionChanged += delegate
		{
			if (!sync)
			{
				PageBase.Run(Reload, I18n.L("读取失败"));
			}
		};
		SPanel sPanel = Ui.H(8.0, Ui.Btn(I18n.L("导入 jar"), BtnKind.Soft, delegate
		{
			PageBase.Run(async delegate
			{
				string[] files = Dlg.PickFiles(I18n.L("模组 (*.jar)|*.jar"), I18n.L("导入模组"));
				if (files != null && files.Length > 0)
				{
					var (newerCount, sameCount, olderCount) = MultiplayerAndDialogUtils.BatchEvaluateImport(installedMods.Select(m => m.Filename), files);
					string[] array = files;
					foreach (string path in array)
					{
						string fn = Path.GetFileName(path);
						await api.StartTaskAsync("install_mod", new
						{
							name = fn,
							instance = instance,
							extra = new
							{
								path = path,
								instance = instance,
								name = fn
							}
						});
					}
					int freshCount = files.Length - (newerCount + sameCount + olderCount);
					string summary = MultiplayerAndDialogUtils.FormatImportSummary(newerCount, sameCount, olderCount, Math.Max(0, freshCount));
					AppServices.Toast(I18n.L("已加入队列"), summary);
				}
			}, I18n.L("导入失败"));
		}, "\ue8b5"), Ui.Btn(I18n.L("检查更新"), BtnKind.Chip, delegate
		{
			PageBase.Run(async delegate
			{
				await CheckUpdatesAsync(instance);
				await Reload();
			}, I18n.L("检查更新失败"));
		}, "\ue72c"), Ui.Btn(I18n.L("打开目录"), BtnKind.Chip, delegate
		{
			PageBase.Run(async delegate
			{
				string text = await api.CallAsync<string>("open_mods_folder", new
				{
					instance = instance,
					version = CurrentVersion()
				});
				AppServices.Toast(I18n.L("已打开"), text ?? "", ToastKind.Success);
			});
		}, "\ue8b7"), Ui.Btn(I18n.L("全局模组"), BtnKind.Chip, delegate
		{
			PageBase.Run(GlobalModsDialog.ShowAsync);
		}, "\ue80a"), Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			PageBase.Run(Reload, I18n.L("读取失败"));
		}, "\ue72c"));
		Grid grid = Ui.G(null, "Auto,Auto,*");
		grid.Add(Ui.Txt(I18n.L("目标"), 12.0, bold: false, "B.InkMuted").VCenter(), 0);
		grid.Add(target.M(8.0, 0.0, 12.0).VCenter(), 0, 1);
		grid.Add(count.VCenter(), 0, 2);
		Dlg.Panel(I18n.L("模组管理 · {0}", instance), Ui.V(12.0, grid, sPanel, smoothScroll), 800.0);
		await ReloadTargets();
		string CurrentVersion()
		{
			int selectedIndex = target.SelectedIndex;
			if (selectedIndex < 0 || selectedIndex >= targets.Count)
			{
				return version;
			}
			return targets[selectedIndex].Value;
		}
		async Task Reload()
		{
			string ver = CurrentVersion();
			list.Children.Clear();
			(List<ModEntry> rows, string err) = await api.TryCallAsyncOrError("get_installed_mod_entries", new
			{
				instance = instance,
				version = ver
			}, new List<ModEntry>());
			List<ModEntry> list2 = rows ?? new List<ModEntry>();
			installedMods = list2;
			count.Text = I18n.L("共 {0} 个模组", list2.Count);
			if (list2.Count == 0)
			{
				if (err != null)
				{
					list.Children.Add(Ui.EmptyError(I18n.L("读取模组失败"), err, delegate
					{
						PageBase.Run(Reload);
					}));
				}
				else
				{
					list.Children.Add(Ui.Empty("\uea86", I18n.L("还没有模组"), I18n.L("到 Mod 页搜索安装，或直接导入 jar")));
				}
			}
			foreach (ModEntry item in list2)
			{
				string file = item.Filename;
				CheckBox checkBox = Ui.Switch(item.Enabled);
				checkBox.Checked += delegate
				{
					PageBase.Run(() => api.TryCallAsync<object>("enable_mod", new
					{
						instance = instance,
						filename = file,
						version = ver
					}));
				};
				checkBox.Unchecked += delegate
				{
					PageBase.Run(() => api.TryCallAsync<object>("disable_mod", new
					{
						instance = instance,
						filename = file,
						version = ver
					}));
				};
				Grid grid2 = Ui.G(null, "Auto,*,Auto");
				grid2.Add(checkBox.M(0.0, 0.0, 12.0).VCenter(), 0);
				grid2.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(item.Name) ? file : item.Name).Trim(), Ui.Small(file)).VCenter(), 0, 1);
				grid2.Add(Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
				{
					PageBase.Run(async delegate
					{
						if (await Dlg.Confirm(I18n.L("删除模组"), I18n.L("「{0}」会从「{1}」的 mods 目录里移除，需要时可重新下载。", file, instance), I18n.L("删除"), I18n.L("取消"), danger: true))
						{
							await api.CallAsync("delete_mod", new
							{
								instance = instance,
								filename = file,
								version = ver
							});
							await Reload();
						}
					}, I18n.L("删除失败"));
				}).VCenter(), 0, 2);
				list.Children.Add(Ui.RowCard(grid2, null, 10.0));
			}
			Motion.Stagger(list, 16.0);
		}
		async Task ReloadTargets()
		{
			targets = (await api.TryCallAsync("get_mods_targets", new { instance }, new List<ModsTarget>())) ?? new List<ModsTarget>();
			if (targets.Count == 0)
			{
				targets.Add(new ModsTarget
				{
					Label = I18n.L("实例共享 mods 目录"),
					Value = ""
				});
			}
			sync = true;
			int selectedIndex = Math.Max(0, targets.FindIndex((ModsTarget t) => t.Value == version));
			target.Items.Clear();
			foreach (ModsTarget item2 in targets)
			{
				target.Items.Add(item2.Label);
			}
			target.SelectedIndex = selectedIndex;
			sync = false;
			await Reload();
		}
	}

	public static async Task CheckUpdatesAsync(string instance)
	{
		BridgeClient api = AppServices.Client;
		List<ModUpdateRow> list;
		string err;
		using (Dlg.Busy(I18n.L("正在比对模组版本…")))
		{
			(List<ModUpdateRow> rows, string text) = await api.TryCallAsyncOrError("check_mod_updates", new { instance }, new List<ModUpdateRow>());
			list = rows ?? new List<ModUpdateRow>();
			err = text;
		}
		if (err != null)
		{
			AppServices.Toast(I18n.L("检查更新失败"), err, ToastKind.Warning);
			return;
		}
		if (list.Count == 0)
		{
			AppServices.Toast(I18n.L("都是最新的"), I18n.L("没有找到可更新的模组"), ToastKind.Success);
			return;
		}
		SPanel sPanel = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(sPanel);
		smoothScroll.MaxHeight = 360.0;
		smoothScroll.MinWidth = 600.0;
		List<(CheckBox Box, ModUpdateRow Row)> picks = new List<(CheckBox, ModUpdateRow)>();
		foreach (ModUpdateRow item in list)
		{
			CheckBox checkBox = Ui.Check("", value: true);
			Grid grid = Ui.G(null, "Auto,*,Auto");
			grid.Add(checkBox.M(0.0, 0.0, 10.0).VCenter(), 0);
			grid.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(item.Name) ? item.Filename : item.Name, 13.0, bold: true).Trim(), Ui.Small(item.Current + " → " + item.Latest + "  ·  " + item.Source + "  ·  " + Fmt.Size(item.Size)).Wrap()).VCenter(), 0, 1);
			grid.Add(Ui.Tag(string.IsNullOrWhiteSpace(item.McVersion) ? "—" : item.McVersion).VCenter(), 0, 2);
			sPanel.Children.Add(Ui.RowCard(grid, null, 10.0));
			picks.Add((checkBox, item));
		}
		if (!(await Dlg.Ask(I18n.L("{0} 个模组有更新", list.Count), Ui.V(10.0, Ui.Muted(I18n.L("勾上要更新的，旧文件会在新包校验通过后才删除。")), smoothScroll), I18n.L("更新所选"))))
		{
			return;
		}
		List<ModUpdateRow> list2 = (from p in picks
			where p.Box.IsChecked == true
			select p.Row).ToList();
		if (list2.Count == 0)
		{
			return;
		}
		int done = 0;
		List<string> failed = new List<string>();
		using (Dlg.Busy(I18n.L("正在更新 {0} 个模组…", list2.Count)))
		{
			foreach (ModUpdateRow row in list2)
			{
				try
				{
					await api.CallAsync<string>("apply_mod_update", new { instance, row });
					done++;
				}
				catch (Exception ex)
				{
					failed.Add(I18n.L("{0}：{1}", row.Filename, ex.Message));
				}
			}
		}
		if (failed.Count == 0)
		{
			AppServices.Toast(I18n.L("已更新"), I18n.L("{0} 个模组", done), ToastKind.Success);
		}
		else
		{
			await Dlg.Alert(I18n.L("部分模组没能更新"), I18n.L("成功 {0} 个，失败 {1} 个：\n\n", done, failed.Count) + string.Join("\n", failed));
		}
	}
}
