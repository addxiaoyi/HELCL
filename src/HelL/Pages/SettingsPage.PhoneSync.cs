using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher.Sync;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class SettingsPage
{
	/// <summary>设置页「手机同步」卡片：填手机地址（或扫描 / USB）、输入配对码连接，然后打开同步面板。</summary>
	private FrameworkElement BuildPhoneSyncCard()
	{
		TextBox addr = Ui.Input(I18n.L("手机 IP，如 192.168.1.23"), PhoneLink.LastAddress, 200.0);
		TextBox code = Ui.Input(I18n.L("配对码"), PhoneLink.LastToken, 90.0);
		TextBlock status = Ui.Muted("");
		status.TextWrapping = System.Windows.TextWrapping.Wrap;
		Button open = null;
		void Refresh(string msg = null)
		{
			PhoneClient.Info info = PhoneLink.CurrentInfo;
			status.Text = msg ?? (info != null ? I18n.L("已连接") + " " + info.Device + (string.IsNullOrEmpty(info.Android) ? "" : " · Android " + info.Android) : I18n.L("未连接"));
			open.IsEnabled = PhoneLink.Current != null;
		}
		async Task Connect(Func<PhoneClient.Info> f)
		{
			if (code.Text.Trim().Length == 0)
			{
				Refresh(I18n.L("请输入手机上显示的 6 位配对码"));
				return;
			}
			Refresh(I18n.L("正在连接…"));
			try
			{
				await Task.Run(f);
				Refresh();
			}
			catch (Exception e)
			{
				PhoneLink.Disconnect();
				Refresh(e.Message);
			}
		}
		Button connect = Ui.Btn(I18n.L("连接"), BtnKind.Primary, delegate
		{
			string a = addr.Text;
			string t = code.Text;
			PageBase.Run(() => Connect(() => PhoneLink.Connect(a, t)));
		});
		Button usb = Ui.Btn(I18n.L("USB 自动连接"), BtnKind.Chip, delegate
		{
			string t = code.Text;
			PageBase.Run(() => Connect(() => PhoneLink.ConnectUsb(t)));
		});
		Button scan = Ui.Btn(I18n.L("扫描局域网"), BtnKind.Chip, delegate
		{
			PageBase.Run(async () =>
			{
				Refresh(I18n.L("正在扫描…"));
				List<string> found = await Task.Run(() => PhoneClient.ScanLan());
				if (found.Count > 0)
				{
					addr.Text = found[0];
				}
				Refresh(found.Count == 0 ? I18n.L("没找到手机：确认在同一个 Wi-Fi，且手机上已启动同步服务") : I18n.L("找到") + " " + string.Join("  ", found));
			});
		});
		open = Ui.Btn(I18n.L("打开同步面板"), BtnKind.Normal, delegate
		{
			PageBase.Run(PhoneSyncPanel.ShowAsync, I18n.L("同步失败"));
		});
		Refresh();
		addr.Width = double.NaN;
		Grid connectRow = new AdaptiveColumns(addr, Ui.H(8.0, code.VCenter(), connect.VCenter()),
			fixedWidth: 200, breakpoint: 400, gap: 8);
		SPanel methods = Ui.H(8.0, scan, usb);
		methods.Wrap = true;
		Grid statusRow = Ui.G(null, "Auto,*");
		statusRow.Add(open.VCenter(), 0);
		statusRow.Add(status.M(12.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		return Ui.Fields(Ui.Section(I18n.L("手机同步"), I18n.L("和手机启动器互传存档：同一个 Wi-Fi，或 USB 数据线")), Ui.Field(I18n.L("连接手机"), Ui.V(8.0, connectRow, methods), I18n.L("手机启动器 → 存档管理 → 从 zip 导入 → 启动同步服务，填上显示的地址和配对码；USB 需打开手机的 USB 调试，并把 adb.exe 放在 HelL 同目录")), Ui.Field("", statusRow));
	}
}
