using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public static class SavesDialog
{
	public static async Task ShowAsync(string instance, string version)
	{
		BridgeClient api = AppServices.Client;
		SPanel list = Ui.V(8.0);
		SPanel backups = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(list);
		smoothScroll.MaxHeight = 320.0;
		smoothScroll.MinWidth = 0;
		SmoothScroll smoothScroll2 = Ui.Scroll(backups);
		smoothScroll2.MaxHeight = 150.0;
		SPanel media = Ui.V(6.0);
		SmoothScroll smoothScroll3 = Ui.Scroll(media);
		smoothScroll3.MaxHeight = 170.0;
		ComboBox kind = Ui.Combo(new string[3]
		{
			I18n.L("截图"),
			I18n.L("崩溃报告"),
			I18n.L("日志")
		}, null, 130.0);
		TextBlock mediaCount = Ui.Small("");
		kind.SelectionChanged += delegate
		{
			PageBase.Run(ReloadMedia, I18n.L("读取失败"));
		};
		Grid grid = Ui.G(null, "Auto,Auto,*");
		grid.Add(Ui.Txt(I18n.L("截图 / 日志"), 12.5, bold: true).VCenter(), 0);
		grid.Add(kind.M(10.0, 0.0, 10.0).VCenter(), 0, 1);
		grid.Add(mediaCount.VCenter(), 0, 2);
		SPanel body = Ui.V(12.0, smoothScroll, Ui.Sep(), smoothScroll2, Ui.Sep(), grid, smoothScroll3);
		Dlg.Panel(I18n.L("存档管理 · {0}", instance) + (string.IsNullOrEmpty(version) ? "" : (" / " + version)), body, 780.0);
		await Reload();
		await ReloadMedia();
		async Task Reload()
		{
			list.Children.Clear();
			backups.Children.Clear();
			list.Children.Add(Ui.Skeleton(46.0));
			list.Children.Add(Ui.Skeleton(46.0));
			backups.Children.Add(Ui.Skeleton(46.0));
			backups.Children.Add(Ui.Skeleton(46.0));
			(List<SaveRow> rows, string err) = await api.TryCallAsyncOrError("list_saves", new { instance, version }, new List<SaveRow>());
			list.Children.Clear();
			List<SaveRow> obj = rows ?? new List<SaveRow>();
			if (obj.Count == 0)
			{
				if (err != null)
				{
					list.Children.Add(Ui.EmptyError(I18n.L("读取存档失败"), err, delegate
					{
						PageBase.Run(Reload);
					}));
				}
				else
				{
					list.Children.Add(Ui.Empty("\ue74e", I18n.L("这个版本还没有存档"), I18n.L("进游戏创建世界后会出现在这里")));
				}
			}
			foreach (SaveRow item in obj)
			{
				string name = item.Name;
                var icon = new LocalPicture(56, 56, I18n.L("存档"));
                _ = icon.SetPath(item.Icon, item.Path, I18n.L("存档缩略图") + " · " + name);
                string updated = item.Mtime > 0 && item.Mtime < 253402300799L ? DateTimeOffset.FromUnixTimeSeconds(item.Mtime).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "";
				SPanel el = Ui.V(2.0, Ui.Txt(name, 13.0, bold: true).Wrap(), Ui.Small(string.Join(" · ", new[] { item.Size.Length > 0 ? item.Size : Fmt.Size(item.Bytes), item.LastPlayed.Length > 0 ? item.LastPlayed : updated, item.Mode }.Where(x => !string.IsNullOrWhiteSpace(x)))).Wrap());
				SPanel el2 = Ui.H(6.0, Ui.Btn(I18n.L("备份"), BtnKind.Chip, delegate
				{
					PageBase.Run(async delegate
					{
						await api.StartTaskAsync("backup_save", new { instance, name, version });
						AppServices.Toast(I18n.L("已开始备份"), name);
					}, I18n.L("备份失败"));
				}), Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
				{
					PageBase.Run(async delegate
					{
						string dest = Dlg.SaveFile(I18n.L("压缩包 (*.zip)|*.zip"), name + ".zip", I18n.L("导出存档"));
						if (dest != null)
						{
							await api.CallAsync("export_save", new { instance, name, dest, version });
							AppServices.Toast(I18n.L("已导出"), dest, ToastKind.Success);
						}
					}, I18n.L("导出失败"));
				}), Ui.Btn(I18n.L("打开"), BtnKind.Chip, delegate
				{
					PageBase.Run(() => api.CallAsync("open_save", new { instance, name, version }));
				}), Ui.Btn(I18n.L("装数据包"), BtnKind.Chip, delegate
				{
					PageBase.Run(() => InstallDatapackAsync(instance, version, name), I18n.L("安装失败"));
				}), Ui.Btn(I18n.L("删除"), BtnKind.Danger, delegate
				{
					PageBase.Run(async delegate
					{
						if (await Dlg.Confirm(I18n.L("删除存档"), I18n.L("「{0}」会被永久删除，其中的建筑与进度都无法恢复。", name), I18n.L("永久删除"), I18n.L("取消"), danger: true))
						{
							await api.CallAsync("delete_save", new { instance, name, version });
							await Reload();
						}
					}, I18n.L("删除失败"));
				}));
                el2.Wrap = true;
				Grid grid2 = Ui.G(null, "Auto,*");
				grid2.Add(icon, 0);
				grid2.Add(Ui.V(8, el, el2).M(12, 0, 0, 0), 0, 1);
				list.Children.Add(Ui.RowCard(grid2));
			}
			Motion.Stagger(list);
			(List<BackupRow> backupRows, string backupErr) = await api.TryCallAsyncOrError("list_save_backups", new { instance, version }, new List<BackupRow>());
			backups.Children.Clear();
			List<BackupRow> list2 = backupRows ?? new List<BackupRow>();
			backups.Children.Add(Ui.Txt(I18n.L("备份（{0}）", list2.Count), 12.5, bold: true));
			if (backupErr != null && list2.Count == 0)
			{
				backups.Children.Add(Ui.Muted(I18n.L("读取备份失败：{0}", backupErr)));
			}
			foreach (BackupRow item2 in list2.Take(20))
			{
				string bn = item2.Name;
				Grid grid3 = Ui.G(null, "*,Auto");
				string backupSize = item2.Size.Length > 0 ? item2.Size : Fmt.Size(item2.Bytes);
				string backupDate = item2.Date.Length > 0 ? item2.Date : item2.Mtime > 0 && item2.Mtime < 253402300799L
					? DateTimeOffset.FromUnixTimeSeconds(item2.Mtime).LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "";
				var backupTitle = Ui.Txt(bn, 12).Trim(); backupTitle.ToolTip = bn;
				grid3.Add(Ui.V(2, backupTitle, Ui.Small(string.Join(" · ", new[] { backupSize, backupDate }.Where(value => value.Length > 0)))).VCenter(), 0);
				grid3.Add(Ui.H(6.0, Ui.Btn(I18n.L("还原"), BtnKind.Chip, delegate
				{
					PageBase.Run(async delegate
					{
						if (await Dlg.Confirm(I18n.L("还原备份"), I18n.L("用「{0}」覆盖现有存档？将自动为当前存档创建还原前安全快照。", bn), I18n.L("还原")))
						{
							try
							{
								string targetSave = bn.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? bn.Substring(0, bn.Length - 4) : bn;
								await api.CallAsync("backup_save", new
								{
									instance = instance,
									save_name = targetSave,
									version = version,
									backup_name = InstanceUtils.GeneratePreRestoreBackupName(targetSave)
								});
							}
							catch { }
							await api.CallAsync("restore_save_backup", new
							{
								instance = instance,
								backup_name = bn,
								version = version,
								overwrite = true
							});
							AppServices.Toast(I18n.L("已还原"), bn, ToastKind.Success);
							await Reload();
						}
					}, I18n.L("还原失败"));
				}), Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
				{
					PageBase.Run(async delegate
					{
						if (await Dlg.Confirm(I18n.L("删除备份"), I18n.L("「{0}」会被永久删除，之后无法从这个时间点还原存档。", bn), I18n.L("永久删除"), I18n.L("取消"), danger: true))
						{
							await api.CallAsync("delete_save_backup", new
							{
								instance = instance,
								backup_name = bn,
								version = version
							});
							await Reload();
						}
					}, I18n.L("删除失败"));
				})).VCenter(), 0, 1);
				backups.Children.Add(grid3);
			}
		}
		async Task ReloadMedia()
		{
			media.Children.Clear();
			media.Children.Add(Ui.Skeleton(40.0));
			media.Children.Add(Ui.Skeleton(40.0));
			string text = kind.Str();
			string kind2 = ((text == I18n.L("崩溃报告")) ? "crash-reports" : ((text == I18n.L("日志")) ? "logs" : "screenshots"));
			(List<MediaRow> rows, string err) = await api.TryCallAsyncOrError("list_media", new
			{
				instance = instance,
				kind = kind2,
				version = version
			}, new List<MediaRow>());
			media.Children.Clear();
			List<MediaRow> list2 = rows ?? new List<MediaRow>();
			mediaCount.Text = I18n.L("{0} 个文件", list2.Count);
			if (list2.Count == 0)
			{
				media.Children.Add(Ui.Muted((err != null) ? I18n.L("读取失败：{0}", err) : I18n.L("这个目录还是空的")));
				return;
			}
			foreach (MediaRow i in list2.Take(60))
			{
				string path = i.Path;
                Grid grid2 = Ui.G(null, "Auto,*,Auto,Auto");
                if (kind2 == "screenshots")
                {
                    var picture = new LocalPicture(92, 55, I18n.L("截图"));
                    _ = picture.SetPath(i.Path, Path.GetDirectoryName(i.Path)!, i.Name);
                    grid2.Add(picture.M(0, 0, 10, 0), 0);
                }
				grid2.Add(Ui.V(1.0, Ui.Txt(i.Name, 12.5).Trim(), Ui.Small(Fmt.Size(i.Bytes))).VCenter(), 0, 1);
				grid2.Add(Ui.Btn(I18n.L("打开"), BtnKind.Chip, delegate
				{
					PageBase.Run(() => api.TryCallAsync("open_media", new { path }, fallback: false));
				}).VCenter(), 0, 2);
				grid2.Add(Ui.Btn(I18n.L("导出"), BtnKind.Ghost, delegate
				{
					string text2 = Dlg.SaveFile(I18n.L("全部文件|*.*"), i.Name, I18n.L("导出文件"));
					if (text2 == null)
					{
						return;
					}
					try
					{
						File.Copy(path, text2, overwrite: true);
						AppServices.Toast(I18n.L("已导出"), text2, ToastKind.Success);
					}
					catch (Exception ex)
					{
						AppServices.Toast(I18n.L("导出失败"), ex.Message, ToastKind.Error);
					}
				}).VCenter().M(6.0, 0.0, 0.0, 0.0), 0, 3);
				media.Children.Add(grid2);
			}
		}
	}

	public static async Task<string?> PickSaveTargetAsync(string instance, string title)
	{
		List<ModsTarget> rows = (await AppServices.Client.TryCallAsync("get_saves_targets", new { instance }, new List<ModsTarget>())) ?? new List<ModsTarget>();
		if (rows.Count <= 1)
		{
			return (rows.Count == 1) ? rows[0].Value : "";
		}
		ComboBox pick = Ui.Combo(rows.Select((ModsTarget r) => r.Label));
		if (!(await Dlg.Ask(title, Ui.V(8.0, Ui.Muted(I18n.L("装进哪个版本的存档目录？")), pick), I18n.L("继续"))))
		{
			return null;
		}
		int num = Math.Max(0, pick.SelectedIndex);
		return (num < rows.Count) ? rows[num].Value : "";
	}

	private static async Task InstallDatapackAsync(string instance, string version, string save)
	{
		BridgeClient api = AppServices.Client;
		List<string> list = (await api.TryCallAsync("get_installed_datapacks", new { instance }, new List<string>())) ?? new List<string>();
		if (list.Count == 0)
		{
			await Dlg.Alert(I18n.L("没有数据包"), I18n.L("先到「数据包」页装一个，再回来装进存档。"));
			return;
		}
		ComboBox pick = Ui.Combo(list);
		if (await Dlg.Ask(I18n.L("装进存档 · {0}", save), Ui.V(8.0, Ui.Muted(I18n.L("选择要装进这个世界的数据包")), pick), I18n.L("安装")))
		{
			string text = await api.CallAsync<string>("install_datapack_into_save", new
			{
				instance = instance,
				filename = pick.Str(),
				save_name = save,
				version = version
			});
			AppServices.Toast(I18n.L("已安装"), string.IsNullOrWhiteSpace(text) ? pick.Str() : text, ToastKind.Success);
		}
	}
}
