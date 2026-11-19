using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Core.Launcher.Sync;
using HelL.Services;

namespace HelL.Pages;

/// <summary>同步面板：选本机位置和手机位置，对比两边世界，逐个传或一键同步。</summary>
public static class PhoneSyncPanel
{
	private static string Label(SyncDecision d)
	{
		return d switch
		{
			SyncDecision.Same => I18n.L("一致"),
			SyncDecision.Push => I18n.L("电脑较新"),
			SyncDecision.Pull => I18n.L("手机较新"),
			SyncDecision.Conflict => I18n.L("两边都改过"),
			SyncDecision.OnlyLocal => I18n.L("只在电脑"),
			_ => I18n.L("只在手机"),
		};
	}

	private static string Time(long ms)
	{
		return ms <= 0 ? "-" : DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
	}

	public static async Task ShowAsync()
	{
		if (PhoneLink.Current == null)
		{
			await Dlg.Alert(I18n.L("手机同步"), I18n.L("请先在设置里连接手机"));
			return;
		}
		List<SaveSync.Target> targets = await Task.Run(() => SaveSync.Targets());
		List<PhoneClient.Root> roots = await Task.Run(() => PhoneLink.Roots());
		if (roots.Count == 0)
		{
			roots.Add(new PhoneClient.Root { Id = "global", Label = I18n.L("全局存档") });
		}
		if (targets.Count == 0)
		{
			await Dlg.Alert(I18n.L("手机同步"), I18n.L("电脑上还没有实例"));
			return;
		}
		ComboBox pc = Ui.Combo(targets.Select((SaveSync.Target t) => t.Label), targets[0].Label, 220.0);
		ComboBox phone = Ui.Combo(roots.Select((PhoneClient.Root r) => r.Label.Length > 0 ? r.Label : r.Id), null, 220.0);
		SPanel list = Ui.V(6.0);
		SmoothScroll scroll = Ui.Scroll(list);
		scroll.MaxHeight = 360.0;
		scroll.MinWidth = 640.0;
		TextBox log = Ui.LogBox();
		log.Height = 90.0;
		bool busy = false;
		SaveSync.Target T() => targets[Math.Max(0, pc.SelectedIndex)];
		string R() => phone.SelectedIndex >= 0 && phone.SelectedIndex < roots.Count ? roots[phone.SelectedIndex].Id : "global";
		void Log(string s)
		{
			log.Dispatcher.BeginInvoke((Action)delegate
			{
				log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\n");
				log.ScrollToEnd();
			});
		}
		void PickRoot()
		{
			string id = PhoneLink.DefaultRoot(T(), roots);
			phone.SelectedIndex = Math.Max(0, roots.FindIndex((PhoneClient.Root r) => r.Id == id));
		}
		async Task Work(Func<Task> f)
		{
			if (busy)
			{
				return;
			}
			busy = true;
			try
			{
				await f();
			}
			catch (Exception e)
			{
				Log(e.Message);
				AppServices.Window?.Toast(I18n.L("同步失败"), e.Message, ToastKind.Error);
			}
			finally
			{
				busy = false;
			}
		}
		async Task Reload()
		{
			SaveSync.Target t = T();
			string root = R();
			List<PhoneLink.Row> rows = await Task.Run(() => PhoneLink.Compare(t, root));
			list.Children.Clear();
			if (rows.Count == 0)
			{
				list.Children.Add(Ui.Empty("\ue74e", I18n.L("两边都还没有存档")));
			}
			foreach (PhoneLink.Row r in rows)
			{
				PhoneLink.Row row = r;
				string sub = I18n.L("电脑") + " " + (row.HasLocal ? Time(row.LocalMtime) : "-") + " · " + I18n.L("手机") + " " + (row.HasRemote ? Time(row.Remote.LastModified) : "-") + (row.LocalLocked ? " · " + I18n.L("游戏中") : "");
				Button push = Ui.Btn(I18n.L("传到手机"), BtnKind.Chip, delegate
				{
					PageBase.Run(() => Work(async () =>
					{
						if (row.HasRemote && !await Dlg.Confirm(I18n.L("覆盖手机上的存档？"), I18n.L("手机上的「{0}」会先备份到电脑，再用电脑上的版本覆盖。", row.Name)))
						{
							return;
						}
						await Task.Run(() => PhoneLink.Push(t, root, row.Name, Log));
						Log("✓ " + row.Name + " → " + I18n.L("手机"));
						await Reload();
					}));
				});
				Button pull = Ui.Btn(I18n.L("传到电脑"), BtnKind.Chip, delegate
				{
					PageBase.Run(() => Work(async () =>
					{
						if (row.HasLocal && !await Dlg.Confirm(I18n.L("覆盖电脑上的存档？"), I18n.L("电脑上的「{0}」会先自动备份，再用手机上的版本覆盖。", row.Name)))
						{
							return;
						}
						await Task.Run(() => PhoneLink.Pull(t, root, row.Name, Log));
						Log("✓ " + row.Name + " → " + I18n.L("电脑"));
						await Reload();
					}));
				});
				push.IsEnabled = row.HasLocal;
				pull.IsEnabled = row.HasRemote;
				Grid g = Ui.G(null, "*,Auto,Auto");
				g.Add(Ui.V(2.0, Ui.Txt(row.Name, 13.0, bold: true).Trim(), Ui.Small(sub)), 0);
				var (decisionLabel, isAlert) = MultiplayerAndDialogUtils.GetSyncDecisionBadge(row.Decision);
				Border tagBorder = Ui.Tag(decisionLabel);
				if (isAlert)
				{
					tagBorder.SetResourceReference(Border.BackgroundProperty, "B.DangerSoft");
					tagBorder.SetResourceReference(Border.BorderBrushProperty, "B.Danger");
				}
				g.Add(tagBorder.VCenter().M(8.0, 0.0, 8.0), 0, 1);
				g.Add(Ui.H(6.0, push, pull).VCenter(), 0, 2);
				list.Children.Add(Ui.RowCard(g));
			}
		}
		Button refresh = Ui.Btn(I18n.L("刷新对比"), BtnKind.Normal, delegate
		{
			PageBase.Run(() => Work(Reload));
		});
		Button all = Ui.Btn(I18n.L("一键同步"), BtnKind.Primary, delegate
		{
			PageBase.Run(() => Work(async () =>
			{
				SaveSync.Target t = T();
				string root = R();
				List<PhoneLink.Row> rows = await Task.Run(() => PhoneLink.Compare(t, root));
				Dictionary<string, SyncDecision> pick = new Dictionary<string, SyncDecision>();
				foreach (PhoneLink.Row r in rows.Where((PhoneLink.Row x) => x.Decision == SyncDecision.Conflict))
				{
					int c = await Dlg.Choose(I18n.L("「{0}」两边都改过", r.Name), Ui.Muted(I18n.L("电脑") + " " + Time(r.LocalMtime) + "　·　" + I18n.L("手机") + " " + Time(r.Remote.LastModified) + "\n" + I18n.L("被覆盖的一边会先自动备份。")), new string[3]
					{
						I18n.L("跳过"),
						I18n.L("保留手机的"),
						I18n.L("保留电脑的")
					});
					pick[r.Name] = c switch
					{
						1 => SyncDecision.Pull,
						2 => SyncDecision.Push,
						_ => SyncDecision.Same,
					};
				}
				List<string> res = await Task.Run(() => PhoneLink.SyncAll(t, root, (PhoneLink.Row r) => pick.TryGetValue(r.Name, out SyncDecision d) ? d : SyncDecision.Same, Log));
				Log(res.Count == 0 ? I18n.L("两边已经一致") : string.Join("\n", res));
				await Reload();
			}));
		});
		pc.SelectionChanged += delegate
		{
			int before = phone.SelectedIndex;
			PickRoot();
			if (phone.SelectedIndex == before)
			{
				PageBase.Run(() => Work(Reload));
			}
		};
		phone.SelectionChanged += delegate
		{
			PageBase.Run(() => Work(Reload));
		};
		PhoneClient.Info info = PhoneLink.CurrentInfo;
		Grid top = Ui.G(null, "Auto,Auto,Auto,Auto,*,Auto,Auto");
		top.Add(Ui.Txt(I18n.L("电脑"), 12.5, bold: true).VCenter(), 0);
		top.Add(pc.M(8.0, 0.0, 14.0).VCenter(), 0, 1);
		top.Add(Ui.Txt(I18n.L("手机"), 12.5, bold: true).VCenter(), 0, 2);
		top.Add(phone.M(8.0, 0.0, 0.0).VCenter(), 0, 3);
		top.Add(refresh.M(0.0, 0.0, 6.0).VCenter(), 0, 5);
		top.Add(all.VCenter(), 0, 6);
		SPanel body = Ui.V(12.0, Ui.Small(I18n.L("已连接") + " " + (info?.Device ?? "") + (string.IsNullOrEmpty(info?.Android) ? "" : " · Android " + info.Android) + " · " + I18n.L("两边游戏版本要一致，旧版本打开新存档会损坏")), top, scroll, log);
		Dlg.Panel(I18n.L("手机同步"), body, 860.0);
		PickRoot();
		if (phone.SelectedIndex < 0)
		{
			await Work(Reload);
		}
	}
}
