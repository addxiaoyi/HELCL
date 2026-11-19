using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class JavaPage : PageBase
{
	private static readonly (string Major, string Note)[] Majors = new(string, string)[5]
	{
		("8", I18n.L("1.16 及以下旧版本")),
		("11", I18n.L("部分旧模组环境")),
		("17", I18n.L("1.18 – 1.20.4 推荐")),
		("21", I18n.L("1.20.5 – 1.21.11")),
		("25", I18n.L("26.1+ 新版本"))
	};

	private readonly SPanel _list = Ui.V(8.0);

	private readonly TextBlock _count = Ui.Muted("");

	private readonly ComboBox _inst = Ui.Combo(null, null, 170.0);

	private readonly ComboBox _java = Ui.Combo();

	private readonly ComboBox _vendor = Ui.Combo(null, null, 170.0);

	private readonly TextBlock _instJava = Ui.Small("");

	private readonly TextBox _manual = Ui.Input(I18n.L("手动填 java.exe 路径（可空）"));

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private List<JavaOption> _javaOpts = new List<JavaOption>();

	private List<string> _vendors = new List<string>();

	private bool _sync;

	public override string Title => "Java";

	public JavaPage()
	{
		Button button = Ui.Btn(I18n.L("扫描系统 Java"), BtnKind.Soft, null, "\ue721");
		button.Click += delegate
		{
			PageBase.RunOnce(button, () => ReloadListAsync(scan: true), I18n.L("扫描失败"));
		};
		Button button2 = Ui.IconBtn("\ue72c", I18n.L("刷新"), delegate
		{
			PageBase.Run(LoadAsync);
		});
		Grid grid = Ui.Section("Java", I18n.L("游戏按版本挑 Java：26.1+ 要 25，1.20.5+ 要 21，1.18+ 要 17，老版本用 8"), Ui.H(8.0, _count.VCenter(), button2, button));
		SPanel downloads = Ui.V(0);
		(string, string)[] majors = Majors;
		for (int num = 0; num < majors.Length; num++)
		{
			(string, string) tuple = majors[num];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			string i = item;
			Button btn = Ui.Btn(I18n.L("下载"), BtnKind.Soft, null, "\ue896");
			btn.Click += delegate
			{
				PageBase.RunOnce(btn, () => DownloadAsync(i, btn), I18n.L("下载失败"));
			};
			Grid runtime = Ui.G(null, "100,*,Auto");
			runtime.Add(Ui.Txt("Java " + item, 14.0, bold: true).VCenter(), 0);
			runtime.Add(Ui.Small(item2).Wrap().VCenter(), 0, 1);
			runtime.Add(btn.VCenter(), 0, 2);
			downloads.Children.Add(Ui.RowCard(runtime, padding: 10));
		}
		Grid grid2 = Ui.G(null, "*,Auto,Auto");
		grid2.Add(Ui.Txt(I18n.L("下载新运行时"), 13.0, bold: true).VCenter(), 0);
		grid2.Add(Ui.Txt(I18n.L("发行版"), 12.0, bold: false, "B.InkMuted").M(0.0, 0.0, 8.0).VCenter(), 0, 1);
		grid2.Add(_vendor.VCenter(), 0, 2);
		FrameworkElement border2 = Ui.Fields(grid2, downloads);
		_inst.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(ReloadJavaOptionsAsync);
			}
		};
		_java.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(SaveJavaAsync, I18n.L("保存失败"));
			}
		};
		Button browseBtn = Ui.Btn(I18n.L("浏览…"), BtnKind.Chip, delegate
		{
			string path = Dlg.PickFile(I18n.L("Java 可执行文件 (javaw.exe;java.exe)|javaw.exe;java.exe|所有文件 (*.*)|*.*"), I18n.L("选择 Java"));
			if (!string.IsNullOrEmpty(path))
			{
				_manual.Text = path;
			}
		}, "\ue8e5");
		Button el = Ui.Btn(I18n.L("用这个路径"), BtnKind.Chip, delegate
		{
			PageBase.Run(ApplyManualAsync, I18n.L("保存失败"));
		}, "\ue73e");
		Grid grid3 = Ui.G(null, "*,Auto,Auto");
		grid3.Add(_manual, 0);
		grid3.Add(browseBtn.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		grid3.Add(el.M(6.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
		FrameworkElement border3 = Ui.Fields(Ui.Section(I18n.L("关联"), I18n.L("每个实例用哪个 Java；「自动选择」按游戏版本挑")), Ui.Field(I18n.L("实例"), _inst), Ui.Field(I18n.L("默认 Java"), _java, I18n.L("「自动选择」由启动器按游戏版本匹配")), Ui.Field(I18n.L("自定义路径"), grid3, I18n.L("填绝对路径后点右边按钮，会先做一次规范化")), Ui.Field("", _instJava));
		base.Content = PageBase.ScrollBody(grid, border2, border3, _list);
	}

	protected override async Task LoadAsync()
	{
		(List<InstanceInfo> rows, string instErr) = await PageBase.Api.TryCallAsyncOrError("get_instances", null, new List<InstanceInfo>());
		_instances = rows ?? new List<InstanceInfo>();
		_sync = true;
		_inst.Fill(_instances.Select((InstanceInfo i) => i.Name));
		_sync = false;
		if (instErr != null && _instances.Count == 0)
		{
			_instJava.Text = I18n.L("读取实例失败：{0}", instErr);
		}
		Task task = ReloadVendorsAsync();
		Task list = ReloadListAsync(scan: false);
		await task;
		await ReloadJavaOptionsAsync();
		await list;
	}

	private async Task ReloadVendorsAsync()
	{
		_vendors = (await PageBase.Api.TryCallAsync("java_vendor_list", null, new List<string>())) ?? new List<string>();
		if (_vendors.Count == 0)
		{
			_vendors = new List<string> { "adoptium" };
		}
		List<string> list = (await Task.WhenAll(_vendors.Select((string v) => PageBase.Api.TryCallAsync("java_vendor_label", new
		{
			vendor = v
		}, v)))).ToList();
		for (int num = 0; num < list.Count; num++)
		{
			if (string.IsNullOrEmpty(list[num]))
			{
				list[num] = _vendors[num];
			}
		}
		_sync = true;
		_vendor.Items.Clear();
		foreach (string item in list)
		{
			_vendor.Items.Add(item);
		}
		int num2 = _vendors.IndexOf("adoptium");
		_vendor.SelectedIndex = ((num2 >= 0) ? num2 : 0);
		_sync = false;
	}

	private string SelectedVendor()
	{
		int selectedIndex = _vendor.SelectedIndex;
		if (selectedIndex < 0 || selectedIndex >= _vendors.Count)
		{
			return "adoptium";
		}
		return _vendors[selectedIndex];
	}

	private async Task ReloadJavaOptionsAsync()
	{
		string inst = _inst.Str();
		if (string.IsNullOrEmpty(inst))
		{
			return;
		}
		List<JavaOption> opts = (await PageBase.Api.TryCallAsync("java_combo_options", new
		{
			instance = inst,
			scan_system = false
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (opts.Count != 0)
		{
			string keep = await PageBase.Api.TryCallAsync("java_combo_label_for", new
			{
				instance = inst,
				options = opts
			}, "自动选择");
			_sync = true;
			_javaOpts = opts;
			_java.Fill(opts.Select((JavaOption o) => o.Label), keep);
			_sync = false;
			await ShowInstanceJavaAsync(inst);
		}
	}

	private async Task ShowInstanceJavaAsync(string inst)
	{
		string stored = (await PageBase.Api.TryCallAsync("get_instance_java", new
		{
			name = inst
		}, "")) ?? "";
		string text = (await PageBase.Api.TryCallAsync("instance_java_label", new
		{
			name = inst
		}, "")) ?? "";
		_instJava.Text = (string.IsNullOrEmpty(stored) ? "" : (I18n.L("当前记录：{0}", text) + ((stored == text) ? "" : I18n.L("（{0}）", stored))));
	}

	private async Task SaveJavaAsync()
	{
		string inst = _inst.Str();
		if (!string.IsNullOrEmpty(inst))
		{
			string label = _java.Str();
			string java = _javaOpts.FirstOrDefault((JavaOption o) => o.Label == label)?.Value ?? (string.IsNullOrEmpty(label) ? "自动选择" : label);
			await PageBase.Api.CallAsync<object>("set_instance_java", new
			{
				name = inst,
				java = java
			});
			await ShowInstanceJavaAsync(inst);
			PageBase.Toast(I18n.L("已保存"), I18n.L("「{0}」默认 Java：{1}", inst, label), ToastKind.Success);
		}
	}

	private async Task ApplyManualAsync()
	{
		string inst = _inst.Str();
		string raw = _manual.Text?.Trim() ?? "";
		if (string.IsNullOrEmpty(inst) || raw.Length == 0)
		{
			PageBase.Toast(I18n.L("先填路径"), I18n.L("填一个 java.exe 的绝对路径"), ToastKind.Warning);
			Motion.Shake(_manual);
			return;
		}
		string normalized = (await PageBase.Api.CallAsync<string>("normalize_java_pref", new
		{
			java = raw
		})) ?? raw;
		if (normalized != raw)
		{
			PageBase.Toast(I18n.L("已规范化"), raw + "\n→ " + normalized);
		}
		await PageBase.Api.CallAsync<object>("set_instance_java", new
		{
			name = inst,
			java = normalized
		});
		_manual.Clear();
		await ReloadJavaOptionsAsync();
		PageBase.Toast(I18n.L("已保存"), I18n.L("「{0}」默认 Java：{1}", inst, normalized), ToastKind.Success);
	}

	private async Task ReloadListAsync(bool scan)
	{
		_list.Children.Clear();
		_list.Children.Add(Ui.Skeleton(52.0));
		_list.Children.Add(Ui.Skeleton(52.0));
		List<JavaInfo> list;
		string err;
		if (scan)
		{
			using (Dlg.Busy(I18n.L("正在扫描系统 Java…")))
			{
				(list, err) = await PageBase.Api.TryCallAsyncOrError("get_java_list", new
				{
					scan_system = true
				}, new List<JavaInfo>());
			}
		}
		else
		{
			(list, err) = await PageBase.Api.TryCallAsyncOrError("get_java_list", new
			{
				scan_system = false
			}, new List<JavaInfo>());
		}
		list = list ?? new List<JavaInfo>();
		_list.Children.Clear();
		if (err != null)
		{
			_count.Text = "";
			_list.Children.Add(Ui.EmptyError(I18n.L("读取 Java 列表失败"), err, delegate
			{
				PageBase.Run(() => ReloadListAsync(scan));
			}));
			return;
		}
		_count.Text = I18n.L("共 {0} 个", list.Count);
		if (list.Count == 0)
		{
			SPanel empty = Ui.Empty("\uec32", I18n.L("没有找到 Java"), I18n.L("点上面「下载」装一个，或扫描系统已有安装"));
			Button scanBtn = Ui.Btn(I18n.L("扫描系统 Java"), BtnKind.Primary);
			scanBtn.Click += delegate
			{
				PageBase.RunOnce(scanBtn, () => ReloadListAsync(true), I18n.L("扫描失败"));
			};
			scanBtn.HorizontalAlignment = HorizontalAlignment.Center;
			scanBtn.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
			empty.Children.Add(scanBtn);
			_list.Children.Add(empty);
			return;
		}
		foreach (JavaInfo item in list)
		{
			_list.Children.Add(Row(item));
		}
		Motion.Stagger(_list, 18.0, 190.0, 8.0);
	}

	private UIElement Row(JavaInfo j)
	{
		Border border = new Border
		{
			Width = 42.0,
			Height = 42.0,
			CornerRadius = new CornerRadius(10.0),
			Child = Ui.Txt(j.Major, 14.0, bold: true, "B.AccentText").Center()
		};
		border.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		border.VCenter();
		Button button = Ui.Btn(I18n.L("设为默认"), BtnKind.Chip, delegate
		{
			if (!string.IsNullOrEmpty(j.Path) && !File.Exists(j.Path))
			{
				PageBase.Toast(I18n.L("文件不存在"), I18n.L("该 Java 路径无效或已被移除：{0}", j.Path), ToastKind.Warning);
				return;
			}
			PageBase.Run(async delegate
			{
				await PageBase.Api.CallAsync<object>("save_settings", new
				{
					data = new
					{
						default_java = j.Path
					}
				});
				PageBase.Toast(I18n.L("已设为默认"), j.Name, ToastKind.Success);
			}, I18n.L("保存失败"));
		});
		Button button2 = Ui.Btn(I18n.L("给本实例"), BtnKind.Chip, delegate
		{
			if (!string.IsNullOrEmpty(j.Path) && !File.Exists(j.Path))
			{
				PageBase.Toast(I18n.L("文件不存在"), I18n.L("该 Java 路径无效或已被移除：{0}", j.Path), ToastKind.Warning);
				return;
			}
			PageBase.Run(async delegate
			{
				string inst = _inst.Str();
				if (!string.IsNullOrEmpty(inst))
				{
					await PageBase.Api.CallAsync<object>("set_instance_java", new
					{
						name = inst,
						java = j.Path
					});
					await ReloadJavaOptionsAsync();
					PageBase.Toast(I18n.L("已保存"), I18n.L("「{0}」默认 Java：{1}", inst, j.Name), ToastKind.Success);
				}
			}, I18n.L("保存失败"));
		});
		Button button3 = Ui.IconBtn("\ue8b7", I18n.L("打开所在目录"), delegate
		{
			PageBase.Run(() => OpenFolderAsync(j.Path), I18n.L("打开失败"));
		});
		bool is32 = (j.Path != null && j.Path.IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0)
			|| (j.Name != null && j.Name.IndexOf("32-bit", StringComparison.OrdinalIgnoreCase) >= 0);
		var (archLabel, isWarning) = AccountAndJavaUtils.FormatJavaArchitectureBadge(!is32);
		TextBlock archTag = Ui.Txt(I18n.L(archLabel), 11.0, bold: isWarning, isWarning ? "B.Warn" : "B.InkMuted");
		Grid grid = Ui.G(null, "Auto,*,Auto");
		grid.Add(border.M(0.0, 0.0, 12.0), 0);
		grid.Add(Ui.V(2.0, Ui.H(6.0, Ui.Txt(j.Name, 13.0, bold: true).Trim(), archTag.VCenter()), Ui.Mono(j.Path).Trim()).VCenter(), 0, 1);
		grid.Add(Ui.H(6.0, button, button2, button3).VCenter().M(12.0, 0.0, 0.0, 0.0), 0, 2);
		return Ui.RowCard(grid, null, 11.0);
	}

	private async Task OpenFolderAsync(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		string path2 = (File.Exists(path) ? (Path.GetDirectoryName(path) ?? path) : path);
		if (await PageBase.Api.TryCallAsync("open_media", new
		{
			path = path2
		}, fallback: false))
		{
			return;
		}
		try
		{
			if (File.Exists(path))
			{
				Process.Start("explorer.exe", "/select,\"" + path + "\"");
			}
			else if (Directory.Exists(path))
			{
				Process.Start("explorer.exe", "\"" + path + "\"");
			}
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("打开失败"), ex.Message, ToastKind.Warning);
		}
	}

	private async Task DownloadAsync(string major, FrameworkElement anchor)
	{
		string vendor = SelectedVendor();
		if (!(vendor == "adoptium"))
		{
			await PageBase.Api.StartTaskAsync("install_java", new
			{
				major = int.Parse(major),
				vendor = vendor
			});
		}
		else
		{
			await PageBase.Api.StartTaskAsync("download_java", new { major, vendor });
		}
		if (SettingsPage.FlyEnabled)
		{
			PageBase.Win?.FlyToTasks(anchor, "Java " + major);
		}
		PageBase.Toast(I18n.L("已加入队列"), I18n.L("正在下载 {0} Java {1}，进度看下载任务", vendor, major));
	}

	public override void OnEvent(BridgeEvent ev)
	{
		if (!(ev.Event == "finished") || !ev.Success)
		{
			return;
		}
		TaskRow taskRow = TaskStore.Get(ev.TaskId);
		if (taskRow != null && taskRow.Title.Contains("Java", StringComparison.OrdinalIgnoreCase))
		{
			PageBase.Run(async delegate
			{
				await ReloadListAsync(scan: false);
				await ReloadJavaOptionsAsync();
			});
		}
	}
}
