using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public static class VersionSetupDialog
{
	private static readonly string[] IsoLabels = new string[4]
	{
		I18n.L("跟随全局"),
		I18n.L("关闭（共用实例目录）"),
		I18n.L("隔离存档"),
		I18n.L("隔离全部")
	};

	private static readonly string[] IsoValues = new string[4] { "", "none", "saves", "all" };

	private static readonly string[] GcLabels = new string[5]
	{
		I18n.L("自动"),
		I18n.L("G1（通用）"),
		I18n.L("ZGC（大内存）"),
		I18n.L("Serial（老电脑）"),
		I18n.L("不指定")
	};

	private static readonly string[] GcValues = new string[5] { "auto", "g1", "zgc", "serial", "none" };

	public static async Task ShowAsync(string instance, string version)
	{
		BridgeClient api = AppServices.Client;
		VersionSettingsDto data = (await api.TryCallAsync("get_version_settings", new { instance, version }, new VersionSettingsDto())) ?? new VersionSettingsDto();
		List<string> list = (await api.TryCallAsync("get_accounts", null, new List<string>())) ?? new List<string>();
		list.Insert(0, I18n.L("（不绑定）"));
		var globalCfg = await api.TryCallAsync("get_settings", null, new Dictionary<string, System.Text.Json.JsonElement>());
		string globalMemHint = "";
		if (globalCfg != null && globalCfg.TryGetValue("default_memory_mb", out var memEl) && memEl.TryGetInt32(out int gMem) && gMem > 0)
		{
			globalMemHint = $"（当前全局: {gMem} MB）";
		}
		ComboBox iso = Ui.Combo(IsoLabels, IsoLabels[Math.Max(0, Array.IndexOf<string>(IsoValues, data.Isolation ?? ""))]);
		TextBox mem = Ui.Input(I18n.L("留空则跟随全局默认设置 " + globalMemHint).Trim(), data.MemoryMb?.ToString() ?? "");
		TextBox jvm = Ui.Input("-XX:+UseG1GC …", data.JvmArgs ?? "");
		ComboBox gc = Ui.Combo(GcLabels, GcLabels[Math.Max(0, Array.IndexOf<string>(GcValues, string.IsNullOrEmpty(data.Gc) ? "auto" : data.Gc))]);
		TextBox server = Ui.Input(I18n.L("进游戏直连的服务器"), data.Server ?? "");
		TextBox port = Ui.Input("25565", data.Port ?? "");
		TextBox pre = Ui.Input(I18n.L("启动前执行的命令"), data.PreLaunch ?? "");
		CheckBox preWait = Ui.Check(I18n.L("等待启动前命令结束"), data.PreLaunchWait);
		TextBox post = Ui.Input(I18n.L("退出后执行的命令"), data.PostLaunch ?? "");
		TextBox title = Ui.Input(I18n.L("自定义游戏窗口标题"), data.WindowTitle ?? "");
		ComboBox winMode = Ui.Combo(new string[3]
		{
			I18n.L("窗口"),
			I18n.L("全屏"),
			I18n.L("最大化")
		}, (data.WindowMode == "fullscreen") ? I18n.L("全屏") : ((data.WindowMode == "maximized") ? I18n.L("最大化") : I18n.L("窗口")));
		ComboBox acc = Ui.Combo(list, string.IsNullOrEmpty(data.LoginAccount) ? I18n.L("（不绑定）") : data.LoginAccount);
		TextBox nide8 = Ui.Input(I18n.L("统一通行证 ServerId（可空）"), data.Nide8Id ?? "");
		CheckBox hidden = Ui.Check(I18n.L("在版本列表中隐藏"), data.Hidden);
		SmoothScroll smoothScroll = Ui.Scroll(Ui.V(4.0, Ui.Field(I18n.L("版本隔离"), iso, I18n.L("存档/配置是否与实例其它版本分开")), Ui.Field(I18n.L("独占内存"), mem, I18n.L("单位 MB，留空继承全局配置 " + globalMemHint).Trim()), Ui.Field(I18n.L("JVM 参数"), jvm), Ui.Field(I18n.L("垃圾回收器"), gc), Ui.Field(I18n.L("直连服务器"), server), Ui.Field(I18n.L("端口"), port), Ui.Field(I18n.L("启动前命令"), pre, I18n.L("支持 {game_directory} / {version_id} 变量")), Ui.Field("", preWait), Ui.Field(I18n.L("退出后命令"), post, I18n.L("支持 {game_directory} / {version_id} 变量")), Ui.Field(I18n.L("窗口标题"), title), Ui.Field(I18n.L("窗口模式"), winMode), Ui.Field(I18n.L("绑定账号"), acc, I18n.L("启动这个版本时强制用该账号")), Ui.Field(I18n.L("统一通行证"), nide8), Ui.Field("", hidden)));
		smoothScroll.MaxHeight = 460.0;
		bool saved = await Dlg.EditAsync(I18n.L("版本设置 · ") + version, smoothScroll, async () =>
		{
		string memoryText = mem.Text?.Trim() ?? "";
		Dlg.Require(memoryText.Length == 0 || (int.TryParse(memoryText, out int memory) && memory > 0), mem, I18n.L("内存应为正整数，留空则继承全局设置"));
		string portText = port.Text?.Trim() ?? "";
		Dlg.Require(portText.Length == 0 || (int.TryParse(portText, out int serverPort) && serverPort >= 1 && serverPort <= 65535), port, I18n.L("端口应为 1–65535 之间的整数"));
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["isolation"] = IsoValues[Math.Max(0, iso.SelectedIndex)];
		dictionary["jvm_args"] = jvm.Text ?? "";
		dictionary["gc"] = GcValues[Math.Max(0, gc.SelectedIndex)];
		dictionary["server"] = server.Text ?? "";
		dictionary["port"] = port.Text ?? "";
		dictionary["pre_launch"] = pre.Text ?? "";
		dictionary["pre_launch_wait"] = preWait.IsChecked == true;
		dictionary["post_launch"] = post.Text ?? "";
		dictionary["window_title"] = title.Text ?? "";
		Dictionary<string, object> dictionary2 = dictionary;
		dictionary2["window_mode"] = winMode.SelectedIndex switch
		{
			1 => "fullscreen", 
			2 => "maximized", 
			_ => "window", 
		};
		dictionary["login_account"] = ((acc.SelectedIndex <= 0) ? "" : acc.Str());
		dictionary["nide8_id"] = nide8.Text ?? "";
		dictionary["hidden"] = hidden.IsChecked == true;
		Dictionary<string, object> dictionary3 = dictionary;
		if (int.TryParse(mem.Text?.Trim(), out var result) && result > 0)
		{
			dictionary3["memory_mb"] = result;
		}
		else
		{
			dictionary3["memory_mb"] = null;
		}
			await api.CallAsync("save_version_settings", new
			{
				instance = instance,
				version = version,
				data = dictionary3
			});
		}, width: 680);
		if (saved) AppServices.Toast(I18n.L("已保存"), I18n.L("版本设置已写入"), ToastKind.Success);
	}
}
