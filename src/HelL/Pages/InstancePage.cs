using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class InstancePage : PageBase
{
	private sealed class Row
	{
		public InstanceInfo Info = new InstanceInfo();

		public bool Default;

		public bool Hover;

		public required Border Root { get; init; }

		public required TextBlock Name { get; init; }

		public required TextBlock Sub { get; init; }

		public required SPanel Chips { get; init; }

		public required string Key { get; init; }
        public required Button Pin { get; init; }
        public required LocalPicture Picture { get; init; }
        public required CheckBox Selection { get; init; }
	}

	private readonly InstanceTiles _list = new InstanceTiles
	{
		Focusable = true,
		FocusVisualStyle = (Style)System.Windows.Application.Current.FindResource(SystemParameters.FocusVisualStyleKey)
	};

	private readonly TextBlock _count = Ui.Muted("");

	private readonly TextBox _search = Ui.Input(I18n.L("过滤实例名"), "", 200.0);

	private readonly SPanel _empty = Ui.Empty("\ue80a", I18n.L("还没有实例"), I18n.L("点右上角「新建实例」建一个"));

	private readonly TextBlock _emptyTitle;

	private readonly TextBlock _emptySub;

	private readonly Button _retry;

	private readonly Button _create;

	private string _loadError;

	private readonly DispatcherTimer _debounce = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(220.0)
	};

	private readonly Dictionary<string, Row> _cards = new Dictionary<string, Row>();

	private readonly List<Row> _order = new List<Row>();

	private string? _selected;

	private List<InstanceInfo> _rows = new List<InstanceInfo>();

	private string _defaultInstance = "";

	public override string Title => I18n.L("我的实例");

	public InstancePage()
	{
		_emptyTitle = _empty.Children.OfType<TextBlock>().Skip(1).First();
		_emptySub = _empty.Children.OfType<TextBlock>().Skip(2).First();
		_retry = Ui.Btn(I18n.L("重试"), BtnKind.Soft, delegate
		{
			PageBase.Run(LoadAsync);
		}, "\ue72c");
		_retry.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		_retry.HorizontalAlignment = HorizontalAlignment.Center;
		_retry.Visibility = Visibility.Collapsed;
		_empty.Children.Add(_retry);
		_create = Ui.Btn(I18n.L("新建实例"), BtnKind.Primary, null, "\ue710");
		_create.Click += delegate
		{
			PageBase.RunOnce(_create, CreateAsync, I18n.L("新建失败"));
		};
		_create.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		_create.HorizontalAlignment = HorizontalAlignment.Center;
		_create.Visibility = Visibility.Collapsed;
		_empty.Children.Add(_create);
		Button create = Ui.Btn(I18n.L("新建实例"), BtnKind.Primary, null, "\ue710");
		create.Click += delegate
		{
			PageBase.RunOnce(create, CreateAsync, I18n.L("新建失败"));
		};
		Button more = Ui.Btn(I18n.L("更多操作"), BtnKind.Ghost, glyph: "\ue712");
		more.ContextMenu = new ContextMenu
		{
			PlacementTarget = more,
			Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
		};
		AddAction(I18n.L("打开游戏目录"), OpenGameDirAsync, I18n.L("打开失败"));
		AddAction(I18n.L("导入官方启动器"), MigrateAsync, I18n.L("导入失败"));
		AddAction(I18n.L("刷新"), LoadAsync, I18n.L("读取实例失败"));
        AddAction("重命名当前分组…", RenameSelectedGroup, "分组重命名失败");
		more.Click += (_, _) => more.ContextMenu.IsOpen = true;
		Grid heading = new PageHeader(Ui.H1("我的实例"), Ui.H(6, ViewSwitch(), DetailsToggle(), BatchToggle(), create, more)) { MinimumHeadingWidth = 130 };
		_search.Width = double.NaN;
		_search.Height = 36;
        var filterActions = Ui.H(10, _groupFilter, _pinnedOnly.VCenter(), _sort, _count.VCenter()); filterActions.Wrap = true;
		Grid filters = new PageHeader(_search, filterActions);
        System.Windows.Automation.AutomationProperties.SetName(_groupFilter, "筛选实例分组");
        _groupFilter.SelectionChanged += (_, _) =>
        {
            if (_loadingGroups) return;
            if (Win != null) { Win.Prefs.LibraryGroup = SelectedGroup; Win.SaveUiPrefs(); }
            Render();
        };
		_sort.SelectedIndex = Math.Max(0, Math.Min(2, Win?.Prefs.LibrarySort ?? 0));
        _pinnedOnly.IsChecked = Win?.Prefs.LibraryPinnedOnly == true;
        _sort.SelectionChanged += (_, _) => { if (Win != null) { Win.Prefs.LibrarySort = _sort.SelectedIndex; Win.SaveUiPrefs(); } _visibleLimit = LibraryPageSize; Render(); };
        _pinnedOnly.Click += (_, _) => { if (Win != null) { Win.Prefs.LibraryPinnedOnly = _pinnedOnly.IsChecked == true; Win.SaveUiPrefs(); } _visibleLimit = LibraryPageSize; Render(); };
		_debounce.Tick += delegate
		{
			_debounce.Stop();
			Render();
		};
		_search.TextChanged += delegate
		{
            _visibleLimit = LibraryPageSize;
			_debounce.Stop();
			_debounce.Start();
		};
		_empty.Visibility = Visibility.Collapsed;
		base.Content = BuildLibrarySurface(heading, filters);
		base.PreviewKeyDown += delegate(object _, KeyEventArgs e)
		{
			if (!_list.IsKeyboardFocused) return;
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { MarkVisible(); e.Handled = true; return; }
			switch (e.Key)
			{
			case Key.Down:
				MoveSelection(_list.Columns);
				e.Handled = true;
				break;
			case Key.Up:
				MoveSelection(-_list.Columns);
				e.Handled = true;
				break;
            case Key.Left:
                MoveSelection(-1); e.Handled = true; break;
            case Key.Right:
                MoveSelection(1); e.Handled = true; break;
			case Key.Return:
				if (_selected != null && _cards.ContainsKey(_selected))
				{
					string inst = _selected;
					PageBase.Run(() => LaunchWithAsync(inst));
					e.Handled = true;
				}
				break;
			}
		};
		void AddAction(string title, Func<Task> work, string error)
		{
			var item = new MenuItem { Header = title };
			item.Click += (_, _) => PageBase.RunOnce(more, work, error);
			more.ContextMenu.Items.Add(item);
		}
	}

	protected override async Task LoadAsync()
	{
		(List<InstanceInfo> list2, string err) = await PageBase.Api.TryCallAsyncOrError("get_instances", null, new List<InstanceInfo>());
		_rows = list2 ?? new List<InstanceInfo>();
        FillGroups();
		_loadError = err;
		await LoadLibraryState();
		string text = await PageBase.Api.TryCallAsync("get_setting", new
		{
			key = "default_instance",
			@default = ""
		}, "");
		if (!string.IsNullOrEmpty(text))
		{
			_defaultInstance = text;
		}
		foreach (var row in _cards.Values) _ = RefreshPicture(row);
		Render();

	}

	public override void OnHidden()
	{
		_debounce.Stop();
	}

	private void Render()
	{
		string q = _search.Text?.Trim() ?? "";
        List<InstanceInfo> matches = FilterLibrary(q);
        List<InstanceInfo> list = matches.Take(_visibleLimit).ToList();
        _loadMore.Visibility = matches.Count > list.Count ? Visibility.Visible : Visibility.Collapsed;
        _loadMore.Content = "继续显示（" + list.Count + " / " + matches.Count + "）";
		_count.Text = ((q.Length == 0 && _pinnedOnly.IsChecked != true && SelectedGroup == null) ? I18n.L("共 {0} 个", _rows.Count) : I18n.L("{0} / {1} 个", list.Count, _rows.Count));
		_empty.Show(list.Count == 0);
		bool failed = _loadError != null && _rows.Count == 0;
		_emptyTitle.Text = failed ? I18n.L("读取实例失败") : ((_rows.Count > 0) ? I18n.L("没有匹配的实例") : I18n.L("还没有实例"));
		_emptySub.Text = failed ? (_loadError ?? "") : _rows.Count > 0 ? "试试其他名称、切换分组，或关闭「只看固定」。" : I18n.L("点右上角「新建实例」建一个");
		_retry.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
		_create.Visibility = (!failed && _rows.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
		HashSet<string> wanted = list.Select((InstanceInfo r) => r.Name).ToHashSet<string>(StringComparer.Ordinal);
		foreach (string item in _cards.Keys.Where((string k) => !wanted.Contains(k)).ToList())
		{
			_list.Children.Remove(_cards[item].Root);
			_cards.Remove(item);
		}
		List<UIElement> list2 = new List<UIElement>();
		_order.Clear();
		for (int num = 0; num < list.Count; num++)
		{
			InstanceInfo instanceInfo = list[num];
			if (!_cards.TryGetValue(instanceInfo.Name, out Row value))
			{
				value = BuildRow(instanceInfo);
				_cards[instanceInfo.Name] = value;
                _ = RefreshPicture(value);
				_list.Children.Add(value.Root);
				list2.Add(value.Root);
			}
			Sync(value, instanceInfo);
			value.Pin.Content = Ui.Glyph(_pins.Contains(instanceInfo.Name) ? "\ue840" : "\ue718", 13, _pins.Contains(instanceInfo.Name) ? "B.Accent" : "B.InkMuted");
			System.Windows.Automation.AutomationProperties.SetName(value.Pin, (_pins.Contains(instanceInfo.Name) ? "取消固定 " : "固定 ") + instanceInfo.Name);
			_order.Add(value);
			int num2 = _list.Children.IndexOf(value.Root);
			if (num2 != num)
			{
				_list.Children.RemoveAt(num2);
				_list.Children.Insert(num, value.Root);
			}
			value.Root.Margin = new Thickness(0);
		}
		_list.Visibility = ((list.Count == 0) ? Visibility.Collapsed : Visibility.Visible);
        SyncBatch();
		if (_selected == null || !wanted.Contains(_selected)) _selected = list.FirstOrDefault()?.Name;
		RefreshInspector();
		foreach (Row item2 in _order)
		{
			Paint(item2);
		}
		if (list2.Count > 0)
		{
			Motion.StaggerItems(list2, 24.0, 200.0, 8.0);
		}
	}

	private void Sync(Row card, InstanceInfo info)
	{
		bool flag = info.Name == _defaultInstance;
		if (!(card.Info.Name == info.Name) || card.Default != flag || card.Info.Versions != info.Versions || card.Info.Group != info.Group || !(card.Info.Pack == info.Pack) || !(card.Info.PackVersion == info.PackVersion) || !(card.Info.McVersion == info.McVersion) || !(card.Info.JavaLabel == info.JavaLabel))
		{
			card.Info = info;
			card.Default = flag;
			card.Name.Text = info.Name;
			card.Sub.Text = (string.IsNullOrEmpty(info.Pack) ? info.Mc : (info.Pack + " " + info.PackVersion).Trim());
			card.Chips.Children.Clear();
			if (flag)
			{
				card.Chips.Children.Add(Ui.Tag(I18n.L("默认"), "B.OnAccentStrong", "B.AccentStrong"));
			}
            string metadata = I18n.L("{0} 个版本", info.Versions);
            if (info.Group.Length > 0) metadata += " · " + info.Group;
            if (info.JavaLabel.Length > 0 && info.JavaLabel != "自动选择") metadata += " · " + info.JavaLabel;
            card.Chips.Children.Add(Ui.Small(metadata).Trim());
		}
	}

	private Row BuildRow(InstanceInfo seed)
	{
		TextBlock textBlock = Ui.Txt("", _coverView ? 16 : 14, bold: true).Trim();
        textBlock.ToolTip = seed.Name;
		TextBlock textBlock2 = Ui.Muted("").Trim();
		textBlock2.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
		SPanel sPanel = Ui.H(6.0);
        sPanel.Wrap = true;
        sPanel.Margin = new Thickness(0, 6, 0, 0);
		string instName = seed.Name;
		Button button = Ui.Btn(I18n.L("打开"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => LaunchWithAsync(instName));
		}, "\ue768");
		Button button2 = Ui.IconBtn("\uea86", I18n.L("管理这个实例的模组"), delegate
		{
			PageBase.Run(async delegate
			{
				await ModsDialog.ShowAsync(instName);
			});
		});
		Button button3 = Ui.IconBtn("\ue74e", I18n.L("存档管理"), delegate
		{
			PageBase.Run(async delegate
			{
				await SavesDialog.ShowAsync(instName, "");
			});
		});
		Button button4 = Ui.IconBtn("\ue712", I18n.L("更多"), delegate(object s, RoutedEventArgs _)
		{
			Menu((FrameworkElement)s, instName);
		});
		var pin = Ui.IconBtn("\ue718", "固定 " + instName, (_, _) => Run(() => TogglePin(instName)), 13);
		var details = Ui.IconBtn("\ue946", "实例详情 " + instName, (_, _) => ShowInstanceDetails(instName), 13);
        var selection = SelectionBox(instName);
		SPanel el = Ui.H(4.0, selection, pin, button, details, button4);
		SPanel sPanel2 = Ui.V(0.0, textBlock, textBlock2, sPanel);
		sPanel2.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);
		Grid grid = Ui.G(null, "Auto,*,Auto");
        var picture = new LocalPicture(_coverView ? double.NaN : 60, _coverView ? 128 : 52, I18n.L("暂无截图"));
        var artwork = Ui.Btn("", BtnKind.Ghost, (_, _) => Run(() => InstanceArtworkDialog.Show(instName, LoadAsync)));
        artwork.Content = picture;
        artwork.Padding = new Thickness(0);
        artwork.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        artwork.VerticalContentAlignment = VerticalAlignment.Stretch;
        artwork.ToolTip = I18n.L("更换封面或浏览截图");
        System.Windows.Automation.AutomationProperties.SetName(artwork, I18n.L("封面与截图 · {0}", instName));
		grid.Add(artwork, 0);
		grid.Add(sPanel2.VCenter(), 0, 1);
		grid.Add(el.VCenter().M(10, 0, 0, 0), 0, 2);
        if (_coverView)
        {
            grid.Children.Clear(); grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sPanel2.Margin = new Thickness(12, 12, 12, 8);
            el.Margin = new Thickness(8, 0, 8, 8);
            grid.Add(artwork, 0); grid.Add(sPanel2, 1); grid.Add(el, 2);
        }
		Border root = new Border
		{
			Padding = _coverView ? new Thickness(0) : new Thickness(8, 8, 8, 8),
			BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_coverView ? 10 : 7),
			Child = grid,
			Cursor = Cursors.Hand,
			SnapsToDevicePixels = true
		};
		Row row = new Row
		{
			Root = root,
			Name = textBlock,
			Sub = textBlock2,
			Chips = sPanel,
			Key = instName,
            Pin = pin,
            Picture = picture,
            Selection = selection
		};
		root.MouseEnter += delegate
		{
			row.Hover = true;
			Paint(row);
		};
		root.MouseLeave += delegate
		{
			row.Hover = false;
			Paint(row);
		};
		root.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
		{
			if (!(e.OriginalSource is DependencyObject d) || FindButton(d) == null)
			{
				Select(instName);
				_list.Focus();
                if (BatchMode) { if (!_marked.Add(instName)) _marked.Remove(instName); SyncBatch(); return; }
				if (e.ClickCount == 2)
				{
					e.Handled = true;
					PageBase.Run(() => LaunchWithAsync(instName));
				}
			}
		};
		root.MouseRightButtonUp += delegate(object _, MouseButtonEventArgs e)
		{
			if (!(e.OriginalSource is DependencyObject d) || FindButton(d) == null)
			{
				Select(instName);
				e.Handled = true;
				Menu(root, instName);
			}
		};
		Paint(row);
		return row;
	}

    private async Task RefreshPicture(Row row)
    {
        try
        {
            var visual = await Api.CallAsync<InstanceVisual>("instance_visual", new { instance = row.Key });
            if (!_cards.TryGetValue(row.Key, out Row current) || !ReferenceEquals(current, row)) return;
            if (visual == null || visual.Path.Length == 0) { await row.Picture.SetPath("", "", I18n.L("暂无截图")); return; }
            string caption = visual.Kind == "custom" ? I18n.L("自定义封面") : visual.Kind == "world" ? I18n.L("存档缩略图") : I18n.L("最近游戏截图");
            await row.Picture.SetPath(visual.Path, Path.GetDirectoryName(visual.Path)!, caption + " · " + row.Key);
        }
        catch (Exception ex) { Smoke.Note("instance-picture", ex.Message); }
    }

	private void Paint(Row row)
	{
		bool flag = row.Key == _selected;
		row.Root.SetResourceReference(Border.BackgroundProperty, flag ? "B.Sel" : row.Hover ? "B.Hover" : "B.Card");
		row.Root.SetResourceReference(Border.BorderBrushProperty, flag ? "B.Accent" : "B.CardBorder");
	}

	private void Select(string? name)
	{
		if (!(_selected == name))
		{
			string selected = _selected;
			_selected = name;
			RefreshInspector();
			if (selected != null && _cards.TryGetValue(selected, out Row value))
			{
				Paint(value);
			}
			if (name != null && _cards.TryGetValue(name, out Row value2))
			{
				Paint(value2);
				value2.Root.BringIntoView();
			}
		}
	}

	private void MoveSelection(int delta)
	{
		if (_order.Count != 0)
		{
			int num = ((_selected == null) ? (-1) : _order.FindIndex((Row r) => r.Key == _selected));
			int index = ((num >= 0) ? Math.Max(0, Math.Min(_order.Count - 1, num + delta)) : ((delta <= 0) ? (_order.Count - 1) : 0));
			Select(_order[index].Key);
		}
	}

	private async Task LaunchWithAsync(string instance)
	{

		if (PageBase.Win?.GetPage("launch") is LaunchPage launchPage)
		{
			launchPage.PreferInstance(instance);
		}
		PageBase.Win?.Navigate("launch");
		await LoadAsync();
	}

	internal static Task SetDefaultAsync(string instance)
	{
		return PageBase.Api.CallAsync<object>("save_settings", new
		{
			data = new
			{
				default_instance = instance
			}
		});
	}

	private static Button? FindButton(DependencyObject d)
	{
		while (d != null)
		{
			if (d is Button result)
			{
				return result;
			}
			d = VisualTreeHelper.GetParent(d);
		}
		return null;
	}

	private static FrameworkElement Icon(InstanceInfo info)
	{
		Border border = new Border();
		border.Width = 40.0;
		border.Height = 40.0;
		border.CornerRadius = new CornerRadius(10.0);
		border.VerticalAlignment = VerticalAlignment.Center;
		border.Child = Ui.Glyph(string.IsNullOrEmpty(info.Pack) ? "\ue80a" : "\ue7b8", 17.0, "B.AccentText").Center();
		border.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		return border;
	}

	private void Menu(FrameworkElement anchor, string instance)
	{
		ContextMenu menu = new ContextMenu();
		Item(_pins.Contains(instance) ? "取消固定实例" : "固定到首页", () => TogglePin(instance));
        Item("设置分组…", () => EditGroup(instance));
		Item(I18n.L("封面与截图"), () => InstanceArtworkDialog.Show(instance, LoadAsync));
		Item(I18n.L("打开文件夹"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_folder", new
			{
				name = instance
			});
		});
		Item(I18n.L("打开模组目录 (mods)"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_subfolder", new
			{
				name = instance,
				subfolder = "mods"
			});
		});
		Item(I18n.L("打开存档目录 (saves)"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_subfolder", new
			{
				name = instance,
				subfolder = "saves"
			});
		});
		Item(I18n.L("打开日志目录 (logs)"), async delegate
		{
			await PageBase.Api.CallAsync("open_instance_subfolder", new
			{
				name = instance,
				subfolder = "logs"
			});
		});
		Item(I18n.L("设为默认实例"), async delegate
		{
			await SetDefaultAsync(instance);
			_defaultInstance = instance;
			await LoadAsync();
			PageBase.Toast(I18n.L("默认实例"), I18n.L("之后的启动将默认使用「{0}」", instance), ToastKind.Success);
		});
		Item(I18n.L("选择 Java…"), () => PickJavaAsync(instance));
		Item(I18n.L("挑着更新模组…"), () => ModsDialog.CheckUpdatesAsync(instance));
		Item(I18n.L("后台检查模组更新"), async delegate
		{
			await PageBase.Api.StartTaskAsync("start_mod_updates", new { instance });
			PageBase.Win?.FlyToTasks(anchor, I18n.L("检查更新"));
		});
		Item(I18n.L("导出为 Modrinth 整合包 (.mrpack)"), async delegate
		{
			string text = Dlg.SaveFile(I18n.L("Modrinth 整合包 (*.mrpack)|*.mrpack"), instance + ".mrpack", I18n.L("导出整合包"));
			if (text != null)
			{
				await PageBase.Api.StartTaskAsync("export_modpack", new
				{
					instance = instance,
					dest = text
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		Item(I18n.L("导出为 CurseForge 整合包 (.zip)"), async delegate
		{
			string text = Dlg.SaveFile(I18n.L("CurseForge 整合包 (*.zip)|*.zip"), instance + "-curseforge.zip", I18n.L("导出整合包"));
			if (text != null)
			{
				await PageBase.Api.StartTaskAsync("export_curseforge_modpack", new
				{
					instance = instance,
					dest = text
				});
				PageBase.Win?.FlyToTasks(anchor, I18n.L("导出中"));
			}
		});
		menu.Items.Add(new Separator());
		Item(I18n.L("重命名…"), async delegate
		{
			string text = await Dlg.Prompt(I18n.L("重命名实例"), I18n.L("新名称"), instance);
			if (!string.IsNullOrWhiteSpace(text) && !(text == instance))
			{
				if (!InstanceUtils.IsValidInstanceName(text))
				{
					PageBase.Toast(I18n.L("名称无效"), I18n.L("名称不能包含非法字符 \\/:*?\"<>| 或系统保留名"), ToastKind.Warning);
					return;
				}
				await PageBase.Api.CallAsync("rename_instance", new
				{
					name = instance,
					new_name = text
				});
				await LoadAsync();
			}
		});
		Item(I18n.L("删除实例"), async delegate
		{
			if (await Dlg.Confirm(I18n.L("删除实例"), I18n.L("「{0}」下的版本、模组、存档都会被删除，且不可恢复。", instance), I18n.L("永久删除"), I18n.L("取消"), danger: true))
			{
				await PageBase.Api.CallAsync("delete_instance", new
				{
					name = instance
				});
				await LoadAsync();
				PageBase.Toast(I18n.L("已删除"), instance, ToastKind.Success);
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

	private async Task PickJavaAsync(string instance)
	{
		List<JavaOption> opts = (await PageBase.Api.TryCallAsync("java_combo_options", new
		{
			instance = instance,
			scan_system = true
		}, new List<JavaOption>())) ?? new List<JavaOption>();
		if (opts.Count == 0)
		{
			PageBase.Toast(I18n.L("没找到 Java"), I18n.L("去 Java 页下载一个"), ToastKind.Warning);
			return;
		}
		string selected = await PageBase.Api.TryCallAsync("java_combo_label_for", new
		{
			instance = instance,
			options = opts
		}, "自动选择");
		ComboBox combo = Ui.Combo(opts.Select((JavaOption o) => o.Label), selected);
		string text = (await PageBase.Api.TryCallAsync("instance_java_label", new
		{
			name = instance
		}, "")) ?? "";
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("留「自动选择」由启动器按版本匹配")), string.IsNullOrEmpty(text) ? null : Ui.Small(I18n.L("当前记录：") + text), combo);
		if (await Dlg.Ask(I18n.L("为「{0}」选择 Java", instance), body, I18n.L("保存")))
		{
			string java = opts.FirstOrDefault((JavaOption o) => o.Label == combo.Str())?.Value ?? "自动选择";
			await PageBase.Api.CallAsync("set_instance_java", new
			{
				name = instance,
				java = java
			});
			await LoadAsync();
		}
	}

	private async Task OpenGameDirAsync()
	{
		string root = (await PageBase.Api.TryCallAsync("game_root_path", null, "")) ?? "";
		string name = (await PageBase.Api.TryCallAsync("game_root_name", null, "")) ?? "";
		bool flag = string.IsNullOrWhiteSpace(root);
		if (!flag)
		{
			flag = !(await PageBase.Api.TryCallAsync("open_media", new
			{
				path = root
			}, fallback: false));
		}
		if (flag)
		{
			PageBase.Toast(I18n.L("打不开游戏目录"), string.IsNullOrWhiteSpace(root) ? I18n.L("后端没给出目录") : root, ToastKind.Warning);
		}
		else
		{
			PageBase.Toast(I18n.L("已打开"), string.IsNullOrEmpty(name) ? root : (name + " · " + root), ToastKind.Success);
		}
	}

	private async Task CreateAsync()
	{
		string name = await Dlg.Prompt(I18n.L("新建实例"), I18n.L("实例名称（会创建独立的 .minecraft）"), "", I18n.L("例如：1.20.1 生存"));
		if (!string.IsNullOrWhiteSpace(name))
		{
			if (!InstanceUtils.IsValidInstanceName(name))
			{
				PageBase.Toast(I18n.L("名称无效"), I18n.L("名称不能包含非法字符 \\/:*?\"<>| 或系统保留名"), ToastKind.Warning);
				return;
			}
			await PageBase.Api.CallAsync("create_instance", new { name });
			await LoadAsync();
			PageBase.Toast(I18n.L("已创建"), name, ToastKind.Success);
		}
	}

	private async Task MigrateAsync()
	{
		if (!(await PageBase.Api.TryCallAsync("detect_official_launcher", null, fallback: false)))
		{
			PageBase.Toast(I18n.L("没找到官方启动器"), I18n.L("未检测到 .minecraft 目录"), ToastKind.Warning);
			return;
		}
		string dir = await PageBase.Api.TryCallAsync("official_launcher_dir", null, "");
		List<string> list = (await PageBase.Api.TryCallAsync("scan_official_versions", null, new List<string>())) ?? new List<string>();
		TextBox target = Ui.Input("default", "default");
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("检测到：{0}", dir)), Ui.Muted(I18n.L("可导入 {0} 个版本：{1}{2}", list.Count, string.Join(I18n.L("、"), list.Take(8)), (list.Count > 8) ? " …" : "")), Ui.Txt(I18n.L("导入到实例"), 12.0, bold: false, "B.InkMuted"), target);
		if (await Dlg.Ask(I18n.L("导入官方启动器"), body, I18n.L("开始导入")))
		{
			await PageBase.Api.StartTaskAsync("migrate_official_launcher", new
			{
				instance = (target.Text?.Trim() ?? "default")
			});
			PageBase.Toast(I18n.L("已开始导入"), I18n.L("进度看下载任务"));
		}
	}
}
