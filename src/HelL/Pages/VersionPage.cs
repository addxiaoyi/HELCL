using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class VersionPage : PageBase
{
	private sealed class Installed
	{
		public string Id = "";

		public bool Hidden;

		public bool Isolated;

		public int Mods;

		public string Loader = "";

		public string Mc = "";

		public string IsolationLabel = "";
	}

	private sealed class Row
	{
		public string Version { get; init; } = "";

		public string Type { get; init; } = "";

		public string Date { get; init; } = "";

		public string TypeLabel => Type switch
		{
			"release" => I18n.L("正式版"), 
			"snapshot" => I18n.L("快照"), 
			"old_beta" => I18n.L("旧测试"), 
			"old_alpha" => I18n.L("旧阿尔法"), 
			_ => string.IsNullOrEmpty(Type) ? I18n.L("未知") : Type, 
		};
	}

	private static readonly string[] Loaders = new string[5]
	{
		I18n.L("无"),
		"Fabric",
		"Forge",
		"Quilt",
		"NeoForge"
	};

	private readonly TextBox _search = Ui.Input(I18n.L("过滤版本号，例如 1.20"));

	private readonly ListBox _list = new ListBox
	{
		Style = Ui.S("PlainList"),
		ItemTemplate = (DataTemplate)Application.Current.FindResource("Tpl.Version")
	};

	private readonly ComboBox _inst = Ui.Combo();

	private readonly ComboBox _loader = Ui.Combo(Loaders);

	private readonly ComboBox _loaderVer = Ui.Combo(new string[1] { I18n.L("最新") });

	private readonly CheckBox _release = Ui.Check(I18n.L("正式版"), value: true);

	private readonly CheckBox _snapshot = Ui.Check(I18n.L("快照"));

	private readonly CheckBox _oldBeta = Ui.Check(I18n.L("旧测试"));

	private readonly CheckBox _oldAlpha = Ui.Check(I18n.L("旧阿尔法"));

	private readonly CheckBox _optifine = Ui.Check("OptiFine");

	private readonly CheckBox _lite = Ui.Check("LiteLoader");

	private readonly Button _install;

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBlock _instCount = Ui.Muted("");

	private readonly SPanel _cards = Ui.V(0);

	private readonly TextBox _mineSearch = Ui.Input(I18n.L("过滤已安装版本"), "", 170.0);

	private readonly CheckBox _showHidden = Ui.Switch();

	private readonly SPanel _installedEmpty = Ui.Empty("\ue7b8", I18n.L("这个实例还没有版本"), I18n.L("在左边挑一个装上"));

	private string _activeCategory = "全部";

	private VersionSortMode _sortMode = VersionSortMode.VersionDescending;

	private readonly SPanel _categoryBar = Ui.H(6.0);

	private string _installedError;

	private string _instancesError;

	private List<Row> _all = new List<Row>();

	private List<Installed> _mine = new List<Installed>();

	public static int FetchTimeoutMs = 9000;

	private CancellationTokenSource? _fetchCts;

	private bool _fetchInFlight;

	private bool _fetchInterrupted;

	private readonly TextBlock _fetchErrorText = Ui.Muted("");

	private readonly Border _fetchError;

	public override string Title => I18n.L("安装 Minecraft");

	private static Row Map(VersionRow r)
	{
		return new Row
		{
			Version = r.Version,
			Type = r.Type,
			Date = r.Date
		};
	}

	public VersionPage()
	{
		_install = Ui.Btn(I18n.L("安装所选版本"), BtnKind.Primary, null, "\ue896");
		_install.Click += delegate
		{
			PageBase.RunOnce(_install, InstallAsync, I18n.L("安装失败"));
		};
		_install.IsEnabled = false;
		Button button = Ui.Btn(I18n.L("刷新清单"), BtnKind.Chip, delegate
		{
			PageBase.Run(FetchAsync);
		}, "\ue72c");
		Grid grid = Ui.Section(I18n.L("安装 Minecraft"), I18n.L("先选择游戏版本，再确认安装位置。首次游玩建议使用最新正式版。"), button);
		_search.TextChanged += delegate
		{
			ApplyFilter();
		};
		CheckBox[] array = new CheckBox[4] { _release, _snapshot, _oldBeta, _oldAlpha };
		foreach (CheckBox obj in array)
		{
			obj.Checked += delegate
			{
				ApplyFilter();
			};
			obj.Unchecked += delegate
			{
				ApplyFilter();
			};
		}
		_list.SelectionChanged += delegate
		{
			SyncInstallSummary();
			if (_loader.Str() != I18n.L("无"))
			{
				PageBase.Run(LoadLoaderVersionsAsync);
			}
		};
        _list.MouseDoubleClick += (_, e) => { SyncInstallSummary(); _install.BringIntoView(); _install.Focus(); e.Handled = true; };
		_loader.SelectionChanged += delegate
		{
			string text = _loader.Str();
			_loaderVer.IsEnabled = text != I18n.L("无");
			_optifine.IsEnabled = text == I18n.L("无") || text == "Forge";
			if (!_optifine.IsEnabled)
			{
				_optifine.IsChecked = false;
			}
			PageBase.Run(LoadLoaderVersionsAsync);
			SyncInstallSummary();
		};
		SPanel el = Ui.H(14.0, _release, _snapshot, _oldBeta, _oldAlpha);
		el.Wrap = true;
		Button el2 = Ui.Btn(I18n.L("重试"), BtnKind.Soft, delegate
		{
			PageBase.Run(FetchAsync, I18n.L("版本清单刷新失败"));
		}, "\ue72c");
		Grid grid2 = Ui.G(null, "Auto,*,Auto");
		grid2.Add(Ui.Glyph("\ue7ba", 14.0, "B.Warn").VCenter().M(0.0, 0.0, 8.0), 0);
		grid2.Add(_fetchErrorText.Wrap().VCenter(), 0, 1);
		grid2.Add(el2.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 2);
		_fetchError = Ui.RowCard(grid2, null, 10.0);
		_fetchError.Visibility = Visibility.Collapsed;
		SPanel border = Ui.V(12.0, new PageHeader(Ui.H3("选择版本"), Ui.Btn("最新正式版", BtnKind.Ghost, (_, _) => SelectRecommended())), _search, el, _fetchError, _list, _count);
		_list.Height = 290;
        SizeChanged += (_, _) => _list.Height = Math.Max(220, Math.Min(620, ActualHeight - 260));
		SPanel border2 = BuildInstallPanel();
		_mineSearch.TextChanged += delegate
		{
			RenderInstalled();
		};
		_showHidden.Checked += delegate
		{
			PageBase.Run(ReloadInstalledAsync);
		};
		_showHidden.Unchecked += delegate
		{
			PageBase.Run(ReloadInstalledAsync);
		};
		Button button2 = Ui.Btn(I18n.L("打开游戏目录"), BtnKind.Chip, delegate
		{
			PageBase.Run(async delegate
			{
				await PageBase.Api.CallAsync("open_version_folder", new
				{
					instance = _inst.Str(),
					version = "",
					which = "game"
				});
			}, I18n.L("打开失败"));
		}, "\ue8b7");
		SPanel installedTools = Ui.H(10.0, _instCount.VCenter(), _mineSearch.VCenter(), Ui.H(6.0, Ui.Txt(I18n.L("显示隐藏"), 12.0, bold: false, "B.InkMuted").VCenter(), _showHidden).VCenter(), button2.VCenter());
		installedTools.Wrap = true;
		Grid grid3 = Ui.Section(I18n.L("已安装"), I18n.L("当前实例已装版本；右侧可筛选或显示隐藏项"), installedTools);
		string[] categories = new[] { "全部", "Fabric", "Forge", "NeoForge", "原版", "1.20+" };
		foreach (var cat in categories)
		{
			string currentCat = cat;
			Button chip = Ui.Btn(I18n.L(cat), BtnKind.Chip, delegate
			{
				_activeCategory = currentCat;
				UpdateCategoryBarHighlight();
				RenderInstalled();
			});
			chip.Tag = currentCat;
			_categoryBar.Children.Add(chip);
		}
		UpdateCategoryBarHighlight();

		Button button3 = Ui.Btn(I18n.L("去找版本"), BtnKind.Soft, delegate
		{
			_search.BringIntoView();
			_search.Focus();
			_search.SelectAll();
		});
		button3.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		button3.HorizontalAlignment = HorizontalAlignment.Center;
		_installedEmpty.Children.Add(button3);
		SPanel border3 = Ui.V(10.0, grid3, _categoryBar, _cards, _installedEmpty);
		Grid grid4 = new AdaptiveColumns(border, border2, 280, 620, gap: 24);
        _installedSection.Content = border3;
		base.Content = PageBase.ScrollBody(grid, grid4, Ui.Sep().M(0, 12, 0, 12), _installedSection);
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = _inst.Str(),
			PreferKind = "modpack"
		});
	}

	protected override async Task LoadAsync()
	{
		(List<InstanceInfo> rows2, string instErr) = await PageBase.Api.TryCallAsyncOrError("get_instances", null, new List<InstanceInfo>());
		List<InstanceInfo> source = rows2 ?? new List<InstanceInfo>();
		_instancesError = instErr;
		_inst.Fill(source.Select((InstanceInfo i) => i.Name));
		List<VersionRow> list = (await PageBase.Api.TryCallAsync("get_version_list", null, new List<VersionRow>())) ?? new List<VersionRow>();
		if (list.Count > 0)
		{
			_all = list.Select(Map).ToList();
			ApplyFilter();
		}
		await ReloadInstalledAsync();
		PageBase.Run(FetchAsync, I18n.L("版本清单刷新失败"));
	}

	private async Task FetchAsync()
	{
		CancelFetch();
		CancellationTokenSource cts = (_fetchCts = new CancellationTokenSource(FetchTimeoutMs));
		_fetchInterrupted = false;
		_fetchInFlight = true;
		_fetchError.Visibility = Visibility.Collapsed;
		_count.Text = I18n.L("正在拉取版本清单…");
		try
		{
			List<VersionRow> list = (await PageBase.Api.CallAsync<List<VersionRow>>("fetch_version_list", null, cts.Token)) ?? new List<VersionRow>();
			if (cts == _fetchCts)
			{
				if (list.Count > 0)
				{
					_all = list.Select(Map).ToList();
					ApplyFilter();
				}
				else
				{
					ShowFetchFailure(I18n.L("版本清单刷新失败"), I18n.L("拉取失败，检查网络后点刷新"));
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (cts == _fetchCts && !_fetchInterrupted)
			{
				ShowFetchFailure(I18n.L("拉取超时"), I18n.L("{0} 秒内没等到版本清单。检查网络后重试。", Math.Max(1, (FetchTimeoutMs + 999) / 1000)));
			}
		}
		catch (Exception ex2)
		{
			if (cts == _fetchCts)
			{
				ShowFetchFailure(I18n.L("版本清单刷新失败"), ex2.Message);
			}
		}
		finally
		{
			if (cts == _fetchCts)
			{
				_fetchInFlight = false;
			}
		}
	}

	private void CancelFetch()
	{
		_fetchInFlight = false;
		CancellationTokenSource fetchCts = _fetchCts;
		_fetchCts = null;
		if (fetchCts != null)
		{
			try
			{
				fetchCts.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
			fetchCts.Dispose();
		}
	}

	private void ShowFetchFailure(string title, string detail)
	{
		_fetchErrorText.Text = title + " · " + detail;
		_fetchError.Visibility = Visibility.Visible;
		if (_all.Count == 0)
		{
			_count.Text = I18n.L("拉取失败，检查网络后点刷新");
			return;
		}
		ApplyFilter();
		PageBase.Toast(I18n.L("版本清单刷新失败"), I18n.L("沿用已缓存的清单"), ToastKind.Warning);
	}

	private void ApplyFilter()
	{
		string q = _search.Text?.Trim() ?? "";
		HashSet<string> want = new HashSet<string>();
		if (_release.IsChecked == true)
		{
			want.Add("release");
		}
		if (_snapshot.IsChecked == true)
		{
			want.Add("snapshot");
		}
		if (_oldBeta.IsChecked == true)
		{
			want.Add("old_beta");
		}
		if (_oldAlpha.IsChecked == true)
		{
			want.Add("old_alpha");
		}
		IEnumerable<Row> source = _all.Where((Row r) => want.Contains(r.Type));
		if (q.Length > 0)
		{
			source = source.Where((Row r) => r.Version.Contains(q, StringComparison.OrdinalIgnoreCase) || InstanceUtils.MatchesPinyinOrSubstr(r.Version, q));
		}
		List<Row> list = source.ToList();
        string previous = (_list.SelectedItem as Row)?.Version ?? "";
		_list.ItemsSource = list;
		_list.SelectedItem = list.FirstOrDefault(row => row.Version == previous);
		_count.Text = I18n.L("{0} / {1} 个版本", list.Count, _all.Count);
		if (list.Count > 0 && _list.SelectedIndex < 0)
		{
			_list.SelectedIndex = 0;
		}
        SyncInstallSummary();
	}

	private async Task ReloadInstalledAsync()
	{
		string value = _inst.Str();
		bool showHidden = _showHidden.IsChecked == true;
		if (string.IsNullOrEmpty(value))
		{
			_mine = new List<Installed>();
			_installedError = _instancesError;
			RenderInstalled();
			return;
		}
		(List<VersionRowDto> rows, string err) = await PageBase.Api.TryCallAsyncOrError("get_version_rows", new
		{
			include_hidden = true
		}, new List<VersionRowDto>());
		List<VersionRowDto> source = rows ?? new List<VersionRowDto>();
		_installedError = err;
		_mine = (from r in source
			where showHidden || !r.Hidden
			select new Installed
			{
				Id = r.Id,
				Hidden = r.Hidden,
				Isolated = r.Isolated,
				Mods = r.Mods,
				Loader = (string.IsNullOrEmpty(r.Loader) ? I18n.L("原版") : r.Loader),
				Mc = r.Mc,
				IsolationLabel = r.IsolationLabel
			}).ToList();
		RenderInstalled();
        _installedSection.Header = "已安装版本 · " + _mine.Count;
	}

	private void UpdateCategoryBarHighlight()
	{
		foreach (UIElement child in _categoryBar.Children)
		{
			if (child is Button btn && btn.Tag is string cat)
			{
				bool isSelected = string.Equals(cat, _activeCategory, StringComparison.OrdinalIgnoreCase);
				btn.SetResourceReference(Button.BackgroundProperty, isSelected ? "B.AccentSoft" : "B.Paper");
				btn.SetResourceReference(Button.ForegroundProperty, isSelected ? "B.AccentText" : "B.InkMuted");
			}
		}
	}

	private void RenderInstalled()
	{
		string q = _mineSearch.Text?.Trim() ?? "";
		var filterItems = _mine.Select(m => new VersionFilterItem(m.Id, m.Loader, m.Mc, m.Mods)).ToList();
		var filtered = ModernVersionCatalogUtils.FilterVersions(filterItems, _activeCategory, q);
		var sorted = ModernVersionCatalogUtils.SortVersions(filtered, _sortMode);
		var map = _mine.ToDictionary(m => m.Id, m => m, StringComparer.OrdinalIgnoreCase);
		List<Installed> list = sorted.Where(s => map.ContainsKey(s.Id)).Select(s => map[s.Id]).ToList();

		_instCount.Text = ((q.Length == 0 && (_activeCategory == "全部" || string.IsNullOrEmpty(_activeCategory)))
			? I18n.L("{0} 个", _mine.Count)
			: I18n.L("{0} / {1} 个", list.Count, _mine.Count));
		_cards.Children.Clear();
		bool failed = _installedError != null && _mine.Count == 0;
		_installedEmpty.Show(list.Count == 0 && !failed);
		if (failed)
		{
			_cards.Children.Add(Ui.EmptyError(I18n.L("读取版本列表失败"), _installedError, delegate
			{
				PageBase.Run(ReloadInstalledAsync);
			}));
		}
		foreach (Installed item in list)
		{
			_cards.Children.Add(VersionCard(item));
		}
		Motion.StaggerItems(_cards.Children.Cast<UIElement>(), 26.0, 210.0);
	}

	private UIElement VersionCard(Installed row)
	{
		string inst = _inst.Str();
		string id = row.Id;
		string group = InstanceUtils.ExtractVersionGroup(!string.IsNullOrEmpty(row.Mc) ? row.Mc : row.Id);
		string details = string.Join(" · ", new[] { row.Loader, group, I18n.L("模组 {0}", row.Mods), row.IsolationLabel, row.Hidden ? I18n.L("已隐藏") : "" }.Where(label => !string.IsNullOrEmpty(label)));
		CheckBox checkBox = Ui.Switch(row.Isolated);
		checkBox.Checked += delegate
		{
			PageBase.Run(() => ToggleIsolationAsync(id, isolated: true), I18n.L("切换失败"));
		};
		checkBox.Unchecked += delegate
		{
			PageBase.Run(() => ToggleIsolationAsync(id, isolated: false), I18n.L("切换失败"));
		};
		SPanel sPanel2 = Ui.H(8.0, Ui.Txt(I18n.L("独立模组"), 12.0, bold: false, "B.InkMuted").VCenter(), checkBox);
		System.Windows.Automation.AutomationProperties.SetName(checkBox, I18n.L("独立模组"));
		sPanel2.ToolTip = I18n.L("开：这个版本用自己的 mods / config；关：与实例里其它版本共用那一份");
		Button button = Ui.Btn(I18n.L("启动"), BtnKind.Soft, delegate
		{
			PageBase.Run(() => LaunchAsync(id));
		}, "\ue768");
		Button button2 = Ui.IconBtn("\uea86", I18n.L("管理这个版本的模组"), delegate
		{
			PageBase.Run(async delegate
			{
				await ModsDialog.ShowAsync(inst, id);
				await ReloadInstalledAsync();
			});
		});
		Button button3 = Ui.IconBtn("\ue713", I18n.L("版本设置"), delegate
		{
			PageBase.Run(async delegate
			{
				await VersionSetupDialog.ShowAsync(inst, id);
				await ReloadInstalledAsync();
			});
		});
		Button button4 = Ui.IconBtn("\ue712", I18n.L("更多"), delegate(object s, RoutedEventArgs _)
		{
			MoreMenu((FrameworkElement)s, id, row);
		});
		SPanel identity = Ui.V(5, Ui.Txt(id, 14, bold: true).Trim(), Ui.Small(details).Trim());
		SPanel actions = Ui.H(8, sPanel2.VCenter(), button, button2, button3, button4);
		actions.Wrap = true;
		return Ui.RowCard(new PageHeader(identity.VCenter(), actions), padding: 16);
	}

	private async Task ToggleIsolationAsync(string version, bool isolated)
	{
		bool seed = false;
		if (isolated)
		{
			int num = await Dlg.Choose(I18n.L("转为独立模组"), Ui.Muted(I18n.L("「{0}」将拥有自己的 mods / config 目录。\n\n", version) + I18n.L("要把游戏目录里现有的共享模组复制一份过去吗？\n") + I18n.L("选「复制一份」保持现在能玩的样子；选「留空」从零开始装。")).Wrap().MinW(380.0), new string[3]
			{
				I18n.L("取消"),
				I18n.L("留空"),
				I18n.L("复制一份")
			}, 540.0);
			if (num <= 0)
			{
				await ReloadInstalledAsync();
				return;
			}
			seed = num == 2;
		}
		await PageBase.Api.CallAsync("toggle_version_isolation", new { version, isolated, seed });
		PageBase.Toast(isolated ? I18n.L("已转为独立模组") : I18n.L("已改回共用"), isolated ? (I18n.L("「{0}」以后用自己的 mods / config", version) + (seed ? I18n.L("，已复制一份现有模组") : "")) : I18n.L("「{0}」与实例里其它版本共用 mods", version), ToastKind.Success);
		await ReloadInstalledAsync();
	}

	private async Task LaunchAsync(string version)
	{
		string inst = _inst.Str();
		await InstancePage.SetDefaultAsync(inst);
		if (PageBase.Win?.GetPage("launch") is LaunchPage launchPage)
		{
			launchPage.PreferInstance(inst, version);
		}
		PageBase.Win?.Navigate("launch");
	}

	private void MoreMenu(FrameworkElement anchor, string version, Installed row)
	{
		string inst = _inst.Str();
		ContextMenu menu = new ContextMenu();
		Item(I18n.L("打开版本文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_version_folder", new
			{
				instance = inst,
				version = version,
				which = "root"
			});
		});
		Item(I18n.L("打开游戏文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_version_folder", new
			{
				instance = inst,
				version = version,
				which = "game"
			});
		});
		Item(I18n.L("存档管理…"), async delegate
		{
			await SavesDialog.ShowAsync(inst, version);
		});
		Item(I18n.L("隔离细分…"), () => IsolationDetailAsync(version));
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名"), async delegate
		{
			string text = await Dlg.Prompt(I18n.L("重命名版本"), I18n.L("新版本 ID"), version);
			if (!string.IsNullOrWhiteSpace(text) && !(text == version))
			{
				await PageBase.Api.CallAsync("rename_version", new
				{
					instance = inst,
					version = version,
					new_id = text
				});
				await ReloadInstalledAsync();
			}
		});
		Item(I18n.L("复制一份"), async delegate
		{
			string text = await Dlg.Prompt(I18n.L("复制版本"), I18n.L("新版本 ID"), version + "-copy");
			if (!string.IsNullOrWhiteSpace(text))
			{
				await PageBase.Api.CallAsync("copy_version", new
				{
					instance = inst,
					version = version,
					new_id = text
				});
				await ReloadInstalledAsync();
			}
		});
		Item(row.Hidden ? I18n.L("取消隐藏") : I18n.L("在列表中隐藏"), async delegate
		{
			await PageBase.Api.CallAsync("hide_version", new
			{
				instance = inst,
				version = version,
				hidden = !row.Hidden
			});
			await ReloadInstalledAsync();
			PageBase.Toast(row.Hidden ? I18n.L("已取消隐藏") : I18n.L("已隐藏"), version, ToastKind.Success);
		});
		Item(I18n.L("创建桌面快捷方式"), async delegate
		{
			string text = await PageBase.Api.CallAsync<string>("create_desktop_shortcut", new
			{
				instance = inst,
				version = version
			});
			await Dlg.Alert(I18n.L("已创建"), I18n.L("桌面快捷方式：\n{0}\n\n双击即可直接启动该版本。", text));
		});
		Item(I18n.L("导出启动脚本"), async delegate
		{
			string text = Dlg.SaveFile(I18n.L("批处理 (*.bat)|*.bat"), "launch-" + inst + "-" + version + ".bat", I18n.L("导出启动脚本"));
			if (text != null)
			{
				await PageBase.Api.StartTaskAsync("export_launch_script", new
				{
					instance = inst,
					version = version,
					dest = text
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		Item(I18n.L("修复（补全缺失文件）"), async delegate
		{
			await PageBase.Api.StartTaskAsync("repair_version", new
			{
				instance = inst,
				version = version
			});
			PageBase.Win?.FlyToTasks(anchor, I18n.L("修复中"));
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("卸载这个版本"), async delegate
		{
			if (await Dlg.Confirm(I18n.L("卸载版本"), I18n.L("确定卸载「{0}」？该版本目录下的独立模组与存档会一并删除。", version), I18n.L("卸载"), I18n.L("取消"), danger: true))
			{
				await PageBase.Api.CallAsync("uninstall_version", new
				{
					spec = inst + " / " + version
				});
				await ReloadInstalledAsync();
				PageBase.Toast(I18n.L("已卸载"), version, ToastKind.Success);
			}
		});
		menu.PlacementTarget = anchor;
		menu.IsOpen = true;
		void Item(string head, Func<Task> act)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = head
			};
			menuItem.Click += delegate
			{
				PageBase.Run(act);
			};
			menu.Items.Add(menuItem);
		}
	}

	private async Task IsolationDetailAsync(string version)
	{
		string[] labels = new string[4]
		{
			I18n.L("关闭（共用游戏目录）"),
			I18n.L("隔离存档"),
			I18n.L("隔离模组与配置"),
			I18n.L("隔离全部")
		};
		string[] values = new string[4] { "none", "saves", "mods", "all" };
		string value = (await PageBase.Api.TryCallAsync("get_version_isolation", new { version }, "none")) ?? "none";
		int num = Math.Max(0, Array.IndexOf<string>(values, value));
		ComboBox pick = Ui.Combo(labels, labels[num]);
		CheckBox seed = Ui.Check(I18n.L("把共享池里的模组复制一份过去"));
		if (await Dlg.Ask(I18n.L("隔离细分 · {0}", version), Ui.V(8.0, Ui.Muted(I18n.L("决定这个版本的存档 / 模组是自己一份，还是与游戏目录里其它版本共用")), pick, seed), I18n.L("保存")))
		{
			await PageBase.Api.CallAsync("set_version_isolation", new
			{
				version = version,
				mode = values[Math.Max(0, pick.SelectedIndex)],
				seed = (seed.IsChecked == true)
			});
			await ReloadInstalledAsync();
		}
	}

	private async Task LoadLoaderVersionsAsync()
	{
        int generation = ++_loaderGeneration; string loader = _loader.Str();
        _loaderError = ""; _loaderVer.Tag = null;
        if (loader == I18n.L("无") || !(_list.SelectedItem is Row row))
        {
            _loaderLoading = false; _loaderVer.Fill(new[] { I18n.L("最新") }); SyncInstallSummary(); return;
        }
        _loaderLoading = true; _loaderVer.Fill(new[] { "正在查询兼容版本…" }); SyncInstallSummary();
        try
        {
            var versions = await Api.CallAsync<List<LoaderVer>>("list_loader_versions", new { mc_version = row.Version, loader = loader.ToLowerInvariant() }) ?? new();
            if (generation != _loaderGeneration) return;
            _loaderVer.Fill(new[] { "最新" }.Concat(versions.Select(version => string.IsNullOrEmpty(version.Label) ? version.Id : version.Label)), "最新");
            _loaderVer.Tag = versions;
            if (versions.Count == 0) _loaderError = "此游戏版本暂无兼容的 " + loader + "，请选择其他版本或原版。";
        }
        catch (Exception ex) { if (generation == _loaderGeneration) _loaderError = "兼容版本读取失败：" + ex.Message; Smoke.Note("install-loader", ex.ToString()); }
        finally { if (generation == _loaderGeneration) { _loaderLoading = false; SyncInstallSummary(); } }
	}

	private async Task InstallAsync()
	{
        if (_installStarting || (_installTask.Length > 0 && TaskStore.Get(_installTask)?.Finished != true)) return;
        if (_loaderLoading || _loaderError.Length > 0) throw new InvalidOperationException(_loaderError.Length > 0 ? _loaderError : "正在读取加载器版本，请稍候。");
        if (_inst.Str().Length == 0) throw new InvalidOperationException("请先选择安装实例。");
		object selectedItem = _list.SelectedItem;
		if (!(selectedItem is Row row))
		{
			PageBase.Toast(I18n.L("先选一个版本"), "", ToastKind.Warning);
			return;
		}
		string loader = _loader.Str();
		bool none = loader == I18n.L("无");
		string loader_version = "";
		if (!none && _loaderVer.SelectedIndex > 0 && _loaderVer.Tag is List<LoaderVer> list)
		{
			int num = _loaderVer.SelectedIndex - 1;
			if (num >= 0 && num < list.Count)
			{
				loader_version = list[num].Id;
			}
		}
		Dictionary<string, object> extra = new Dictionary<string, object>
		{
			["optifine"] = _optifine.IsChecked == true,
			["liteloader"] = _lite.IsChecked == true
		};
        _installStarting = true;
        SyncInstallSummary();
        try
        {
        string target = _inst.Str();
        string summary;
        bool insufficient = false;
        try
        {
            using var estimate = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            var plan = await Task.Run(() => HelL.Core.Launcher.InstallationPlan.ForVersion(new HelL.Core.Launcher.Instance(target), row.Version, none ? "" : loader, estimate.Token));
            summary = plan.Summary; insufficient = plan.SpaceKnownInsufficient;
        }
        catch (Exception error)
        {
            summary = "目标实例：" + target + "\n版本：" + row.Version + "\n下载量暂不可用：" + error.Message
                + "\n安装将校验并复用已有文件，不删除存档。可取消并检查网络后重试，也可明确选择继续。";
        }
        if (insufficient) { await Dlg.Alert("磁盘空间不足", summary); return; }
        if (!await Dlg.Confirm("确认安装 Minecraft " + row.Version, summary, "确认安装", "取消")) return;
		string task = await PageBase.Api.StartTaskAsync("install_game", new
		{
			version = row.Version,
			loader = (none ? "" : loader),
			loader_version = loader_version,
			instance = _inst.Str(),
			extra = extra
		});
        TrackInstall(task, target, none ? row.Version : "");
        }
        finally { _installStarting = false; SyncInstallSummary(); }
	}

	public override async Task RefreshAsync()
	{
		await ReloadInstalledAsync();
	}

	public override void OnShown()
	{
        TaskStore.Added -= InstallChanged; TaskStore.Updated -= InstallChanged;
        TaskStore.Added += InstallChanged; TaskStore.Updated += InstallChanged;
        SyncInstallProgress();
		_inst.SelectionChanged -= InstChanged;
		_inst.SelectionChanged += InstChanged;
		if (_fetchInterrupted)
		{
			PageBase.Run(FetchAsync, I18n.L("版本清单刷新失败"));
		}
	}

	public override void OnHidden()
	{
        TaskStore.Added -= InstallChanged; TaskStore.Updated -= InstallChanged;
		if (_fetchInFlight)
		{
			_fetchInterrupted = true;
			CancelFetch();
		}
	}

	private void InstChanged(object? s, SelectionChangedEventArgs e)
	{
        SyncInstallSummary();
		PageBase.Run(ReloadInstalledAsync);
	}
}
