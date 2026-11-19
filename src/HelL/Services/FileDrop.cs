using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Pages;

namespace HelL.Services;

public static class FileDrop
{
	public sealed class DropContext
	{
		public static readonly DropContext None = new DropContext();

		public string Instance { get; init; } = "";

		public string Version { get; init; } = "";

		public string PreferKind { get; init; } = "";
	}

	private static readonly Dictionary<string, string> Installers = new Dictionary<string, string>
	{
		["mod"] = "install_mod",
		["resourcepack"] = "install_resourcepack",
		["shaderpack"] = "install_shader",
		["datapack"] = "install_datapack",
		["world"] = "install_world"
	};

	public static async Task HandleAsync(MainWindow window, IReadOnlyList<string> paths, DropContext? context = null)
	{
		DropContext ctx = context ?? DropContext.None;
		if (paths.Count == 0)
		{
			return;
		}
		List<FileKinds.Verdict> list = await Task.Run(() => paths.Where((string p) => !string.IsNullOrWhiteSpace(p)).Select(FileKinds.Identify).ToList());
		if (list.Count == 0)
		{
			return;
		}
		Dictionary<string, List<string>> buckets = new Dictionary<string, List<string>>();
		List<FileKinds.Verdict> unsure = new List<FileKinds.Verdict>();
		List<string> value2;
		foreach (FileKinds.Verdict item in list)
		{
			string text = ((item.Sure && item.Kinds.Count == 1) ? item.Kinds[0] : ((ctx.PreferKind.Length > 0 && item.Kinds.Contains(ctx.PreferKind)) ? ctx.PreferKind : ""));
			if (text.Length > 0)
			{
				if (!buckets.TryGetValue(text, out List<string> value))
				{
					List<string> list2 = (buckets[text] = new List<string>());
					value2 = list2;
					value = value2;
				}
				value.Add(item.Path);
			}
			else
			{
				unsure.Add(item);
			}
		}
		if (unsure.Count > 0)
		{
			FileKinds.Verdict head = unsure[0];
			List<FileKinds.Verdict> same = unsure.Where((FileKinds.Verdict i) => i.KindSig == head.KindSig).ToList();
			string text2 = await AskKindAsync(head, same.Count - 1);
			if (!string.IsNullOrEmpty(text2))
			{
				if (!buckets.TryGetValue(text2, out List<string> value3))
				{
					List<string> list2 = (buckets[text2] = new List<string>());
					value2 = list2;
					value3 = value2;
				}
				value3.AddRange(same.Select((FileKinds.Verdict i) => i.Path));
			}
			List<FileKinds.Verdict> list5 = unsure.Except<FileKinds.Verdict>(same).ToList();
			if (list5.Count > 0)
			{
				AppServices.Toast(I18n.L("有文件没处理"), I18n.L("这些认不出来，可以单独拖进来再选：") + string.Join(I18n.L("、"), from i in list5.Take(3)
					select i.Name));
			}
		}
		foreach (KeyValuePair<string, List<string>> item2 in buckets)
		{
			item2.Deconstruct(out var key, out value2);
			string kind = key;
			List<string> group = value2;
			try
			{
				string text3 = await ApplyAsync(window, kind, group, ctx);
				if (!string.IsNullOrEmpty(text3))
				{
					AppServices.Toast(FileKinds.Label(kind), text3, ToastKind.Success);
				}
			}
			catch (Exception ex)
			{
				AppServices.Toast(I18n.L("没能处理"), Path.GetFileName(group[0]) + " — " + ex.Message, ToastKind.Error);
			}
		}
	}

	private static async Task<string> AskKindAsync(FileKinds.Verdict info, int others)
	{
		List<string> list = ((info.Kinds.Count > 0) ? info.Kinds : FileKinds.AllKinds.ToList());
		SPanel sPanel = Ui.V(8.0, Ui.Txt(info.Name, 13.5, bold: true).Wrap(), string.IsNullOrWhiteSpace(info.Detail) ? null : Ui.Muted(info.Detail).Wrap());
		List<(RadioButton Btn, string Kind)> picks = new List<(RadioButton, string)>();
		SPanel sPanel2 = Ui.V(6.0);
		foreach (string item in list)
		{
			RadioButton radioButton = new RadioButton
			{
				Content = FileKinds.Label(item) + " — " + FileKinds.Action(item),
				IsChecked = (picks.Count == 0),
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0)
			};
			picks.Add((radioButton, item));
			sPanel2.Children.Add(radioButton);
		}
		sPanel.Children.Add(sPanel2);
		if (others > 0)
		{
			sPanel.Children.Add(Ui.Small(I18n.L("这一批里还有 {0} 个同样认不准的文件，会照同一个选择处理。", others)).Wrap());
		}
		if (!(await Dlg.Ask(I18n.L("这个文件放哪儿？"), sPanel, I18n.L("就这么放"), I18n.L("算了"), danger: false, 460.0)))
		{
			return "";
		}
		return picks.FirstOrDefault(((RadioButton Btn, string Kind) p) => p.Btn.IsChecked == true).Kind ?? "";
	}

	private static async Task<string> ApplyAsync(MainWindow window, string kind, List<string> paths, DropContext ctx)
	{
		switch (kind)
		{
		case "modpack":
			await ImportModpackAsync(window, paths[0]);
			return "";
		case "wallpaper":
			return await SetWallpaperAsync(paths[0]);
		case "skin":
			return await SetSkinAsync(paths[0]);
		default:
		{
			if (!Installers.TryGetValue(kind, out string method))
			{
				throw new InvalidOperationException(I18n.L("还不支持这种文件"));
			}
			string text = ((ctx.Instance.Length <= 0) ? (await CurrentInstanceAsync()) : ctx.Instance);
			string text2 = text;
			string instance = text2;
			foreach (string path in paths)
			{
				await AppServices.Client.StartTaskAsync(method, new
				{
					name = Path.GetFileName(path),
					instance = instance,
					extra = new
					{
						path = path,
						instance = instance,
						version = ctx.Version,
						source = "本地"
					}
				});
			}
			window.Navigate("tasks");
			string text3 = ((ctx.Instance.Length > 0) ? I18n.L("实例「{0}」", instance) : I18n.L("默认实例"));
			return I18n.L("已加入下载任务，装进{0}的{1}", text3, FileKinds.Label(kind));
		}
		}
	}

	public static async Task ImportModpackAsync(MainWindow window, string path)
	{
		FileKinds.ModpackProbe.Info info = await Task.Run(() => FileKinds.ModpackProbe.Probe(path));
		if (info == null)
		{
			AppServices.Toast(I18n.L("这不是整合包"), Path.GetFileName(path), ToastKind.Warning);
			return;
		}
		TextBox name = Ui.Input(I18n.L("新版本名称"), SuggestName(info));
		SPanel body = Ui.V(8.0, Ui.Txt(string.IsNullOrEmpty(info.Name) ? Path.GetFileName(path) : info.Name, 14.0, bold: true).Wrap(), Ui.Muted(info.Format).Wrap(), Ui.Small(string.Join(" · ", new string[3]
		{
			string.IsNullOrEmpty(info.McVersion) ? "" : ("Minecraft " + info.McVersion),
			FileKinds.ModpackProbe.LoaderLabel(info.Loader, info.LoaderVersion),
			(info.Files > 0) ? I18n.L("{0} 个文件", info.Files) : ""
		}.Where((string x) => !string.IsNullOrEmpty(x)))).Wrap(), Ui.Field(I18n.L("装成版本"), name, I18n.L("会建成一个新版本，原有版本不受影响"), 78.0));
		if (await Dlg.Ask(I18n.L("安装整合包"), body, I18n.L("开始安装")))
		{
			string target = name.Text?.Trim();
			if (string.IsNullOrEmpty(target))
			{
				target = SuggestName(info);
			}
			await AppServices.Client.StartTaskAsync("install_modpack", new
			{
				name = target,
				source = "本地",
				extra = new
				{
					path = path,
					name = target,
					source = "本地",
					kind = "modpack"
				}
			});
			window.Navigate("tasks");
			AppServices.Toast(I18n.L("已加入队列"), I18n.L("正在安装「{0}」", target), ToastKind.Success);
		}
	}

	private static string SuggestName(FileKinds.ModpackProbe.Info info)
	{
		IEnumerable<string> values = new string[2] { info.Name, info.Version }.Where((string x) => !string.IsNullOrWhiteSpace(x));
		string text = string.Join(" ", values).Trim();
		if (text.Length > 0)
		{
			return text;
		}
		if (!string.IsNullOrEmpty(info.McVersion))
		{
			return I18n.L("整合包 ") + info.McVersion;
		}
		return I18n.L("整合包");
	}

	private static async Task<string> SetWallpaperAsync(string path)
	{
		string value = (await AppServices.Client.TryCallAsync("get_setting", new
		{
			key = "ui_background_folder",
			@default = ""
		}, "")) ?? "";
		bool hadFolder = !string.IsNullOrWhiteSpace(value);
		await AppServices.Client.CallAsync<object>("save_settings", new
		{
			data = new
			{
				ui_background = path,
				ui_background_folder = ""
			}
		});
		await Wallpaper.ReloadAsync();
		string text = (FileKinds.IsVideo(path) ? I18n.L("动态壁纸") : I18n.L("背景图"));
		return hadFolder ? I18n.L("已设为{0}，文件夹轮播一并停掉了", text) : I18n.L("已设为{0}", text);
	}

	private static async Task<string> SetSkinAsync(string path)
	{
		List<AccountRow> list = ((await AppServices.Client.TryCallAsync("get_account_rows", null, new List<AccountRow>())) ?? new List<AccountRow>()).Where((AccountRow r) => r.Type == "offline").ToList();
		if (list.Count == 0)
		{
			throw new InvalidOperationException(I18n.L("还没有离线账号；先到「账号」页建一个，再把皮肤拖进来"));
		}
		return await AccountPage.EditSkinExternalAsync(list.FirstOrDefault((AccountRow r) => r.Active) ?? list[0], path);
	}

	private static async Task<string> CurrentInstanceAsync()
	{
		string text = AppServices.Window?.Prefs.CatalogInstance;
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		string text2 = (await AppServices.Client.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = ""
		}, "")) ?? "";
		if (!string.IsNullOrWhiteSpace(text2))
		{
			return text2;
		}
		return ((await AppServices.Client.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>()).FirstOrDefault()?.Name ?? "default";
	}
}
