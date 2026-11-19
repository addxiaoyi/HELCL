using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class CatalogPage : PageBase
{
	private enum Mode
	{
		Search,
		Installed,
		Favorites
	}

	private static readonly string[] GameVersions = new string[21]
	{
		I18n.L("全部"),
		"26.3",
		"26.2",
		"26.1.2",
		"26.1",
		"1.21.11",
		"1.21.10",
		"1.21.8",
		"1.21.5",
		"1.21.4",
		"1.21.1",
		"1.20.6",
		"1.20.4",
		"1.20.1",
		"1.19.4",
		"1.19.2",
		"1.18.2",
		"1.17.1",
		"1.16.5",
		"1.12.2",
		"1.7.10"
	};

	private readonly CatalogKind _kind;

	private readonly TextBox _search = Ui.Input();

	private readonly ComboBox _source;

	private readonly ComboBox _type;

	private readonly ComboBox _version = Ui.Combo(GameVersions, I18n.L("全部"), 140.0, "", editable: true);

	private readonly ComboBox _inst = Ui.Combo(null, null, 150.0, I18n.L("选择实例"));

	private readonly ComboBox _installedVer = Ui.Combo(null, null, 150.0, I18n.L("全部版本"));

	private static string _clipSeen = "";

	private readonly ListBox _results;

	private readonly SPanel _placeholder = Ui.V(10.0);

	private readonly Grid _listHost = new Grid();

	private readonly ToggleButton _modeSearch;

	private readonly ToggleButton _modeInstalled;

	private readonly ToggleButton _modeFav;

	private readonly DispatcherTimer _debounce = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(320.0)
	};

	private List<InstanceInfo> _instances = new List<InstanceInfo>();

	private HashSet<string> _favKeys = new HashSet<string>();

	private bool _sync;

	private Mode _mode;

	private readonly bool _installedFirst;

	private int _searchToken;

	public static int SearchTimeoutMs = 9000;

	private CancellationTokenSource? _searchCts;

	private bool _searchInFlight;

	private bool _searchInterrupted;

	private List<string> _installedVerValues = new List<string>();

	private List<string> _installedNames = new List<string>();

	public override string Title
	{
		get
		{
			if (!_installedFirst)
			{
				return _kind.Key == "mod" ? I18n.L("模组下载") : _kind.Title;
			}
			return I18n.L("模组管理");
		}
	}

	public CatalogPage(CatalogKind kind, bool installedFirst = false)
	{
		_kind = kind;
		_installedFirst = installedFirst && kind.InstalledMethod.Length > 0;
		_mode = (_installedFirst ? Mode.Installed : Mode.Search);
		_search.Tag = I18n.L("中文名、英文名或别名，留空看热门");
		string[] array = new string[3]
		{
			I18n.L("全部"),
			"Modrinth",
			"CurseForge"
		};
		_source = Ui.Combo(array, array.Contains<string>(kind.DefaultSource) ? kind.DefaultSource : I18n.L("全部"), 116.0);
		_type = Ui.Combo(kind.Types.Select((string t) => I18n.L(t)), I18n.L("全部"), 116.0);
		_modeSearch = new ToggleButton
		{
			Content = I18n.L("搜索下载"),
			Style = Ui.S("TabItemBtn"),
			IsChecked = !_installedFirst
		};
		_modeInstalled = new ToggleButton
		{
			Content = I18n.L("已安装"),
			Style = Ui.S("TabItemBtn"),
			IsChecked = _installedFirst
		};
		_modeFav = new ToggleButton
		{
			Content = I18n.L("收藏"),
			Style = Ui.S("TabItemBtn")
		};
		_modeSearch.Click += delegate
		{
			SetMode(Mode.Search);
		};
		_modeInstalled.Click += delegate
		{
			SetMode(Mode.Installed);
		};
		_modeFav.Click += delegate
		{
			SetMode(Mode.Favorites);
		};
		Button el = Ui.Btn(I18n.L("搜索"), BtnKind.Primary, delegate
		{
			PageBase.Run(SearchAsync, I18n.L("搜索失败"));
		}, "\ue721");
		_installedVer.SelectionChanged += delegate
		{
			if (!_sync && _mode == Mode.Installed)
			{
				PageBase.Run(LoadInstalledAsync);
			}
		};
		Button more = Ui.Btn(I18n.L("更多操作"), BtnKind.Ghost, glyph: "\ue712");
		more.ContextMenu = new ContextMenu
		{
			PlacementTarget = more,
			Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
		};
		AddAsyncAction(I18n.L("链接安装"), () => InstallFromLinkAsync(more), I18n.L("链接安装失败"));
		AddAsyncAction(I18n.L("导入本地文件"), () => ImportLocalAsync(more), I18n.L("导入失败"));
		if (_kind.Key == "mod")
		{
			AddAsyncAction(I18n.L("检查更新"), async () =>
			{
				await ModsDialog.CheckUpdatesAsync(CurrentInstance());
				if (_mode == Mode.Installed) await LoadInstalledAsync();
			}, I18n.L("检查更新失败"));
		}
		if (ExportKind().Length > 0)
		{
			AddAsyncAction(I18n.L("全部导出"), () => ExportAllInstalledAsync(_installedNames), I18n.L("导出失败"));
		}
		var reset = new MenuItem { Header = I18n.L("重置条件") };
		reset.Click += (_, _) => ResetFilters();
		more.ContextMenu.Items.Add(reset);
		AddAsyncAction(I18n.L("刷新"), ReloadCurrentAsync, I18n.L("操作失败"));
		AddAsyncAction(I18n.L("中文资源映射"), ShowLanguageIndex, I18n.L("中文映射读取失败"));
		more.Click += (_, _) => more.ContextMenu.IsOpen = true;
		_debounce.Tick += delegate
		{
			_debounce.Stop();
			if (_mode == Mode.Search)
			{
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_search.TextChanged += delegate
		{
			if (!_sync && _mode == Mode.Search)
			{
				_debounce.Stop();
				_debounce.Start();
			}
		};
		_search.KeyDown += delegate(object _, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				_debounce.Stop();
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_source.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_type.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_version.SelectionChanged += delegate
		{
			if (!_sync)
			{
                ManualTarget();
				PageBase.Run(SearchAsync, I18n.L("搜索失败"));
			}
		};
		_inst.SelectionChanged += delegate
		{
			if (!_sync)
			{
				if (PageBase.Win != null)
				{
					PageBase.Win.Prefs.CatalogInstance = CurrentInstance();
					PageBase.Win.SaveUiPrefs();
				}
				PageBase.Run(async () => { await ReloadTarget(); await ReloadCurrentAsync(); });
			}
		};
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(_search, 0);
		grid.Add(el.M(8.0, 0.0, 0.0, 0.0), 0, 1);
		var filters = new CatalogFilters();
		var destination = new CatalogFilters { MaximumColumns = 2, MinimumFieldWidth = 180 };
		WrapPanel fields = filters;
		Labeled(I18n.L("内容来源"), _source);
		Labeled(I18n.L("加载器 / 类型"), _type);
		Labeled(I18n.L("Minecraft 版本"), _version);
        fields = destination;
		Labeled(I18n.L("安装到实例"), _inst);
		if (_kind.InstalledMethod.Length > 0)
		{
			Labeled(I18n.L("已装于版本"), _installedVer);
		}
        var common = Ui.G(null, "*,Auto"); common.Add(filters, 0);
        common.Add(more.VCenter().M(6, 0, 0, 0), 0, 1);
        var targetLabel = Ui.Small("").Trim();
        void UpdateTargetLabel()
        {
            string name = CurrentInstance();
            targetLabel.Text = I18n.L("安装位置") + " · " + (name.Length > 0 ? name : I18n.L("未选择实例"))
                + (InstalledVersion().Length > 0 ? " · " + InstalledVersion() : "");
            targetLabel.ToolTip = targetLabel.Text;
        }
        _inst.SelectionChanged += (_, _) => UpdateTargetLabel();
        _installedVer.SelectionChanged += (_, _) => UpdateTargetLabel();
        UpdateTargetLabel();
		_summaryProgress.Visibility = Visibility.Collapsed;
		SPanel el2 = Ui.V(10.0, grid, _summaryProgress);
		_modeSearch.Padding = _modeInstalled.Padding = _modeFav.Padding = new Thickness(12, 5, 12, 6);
		SPanel rightTabs = Ui.H(2.0, _modeSearch, (_kind.InstalledMethod.Length > 0) ? _modeInstalled : null, _modeFav);
		Border right = new Border
		{
			CornerRadius = new CornerRadius(8),
			BorderThickness = new Thickness(1),
			Padding = new Thickness(2),
			VerticalAlignment = VerticalAlignment.Center,
			Child = rightTabs
		};
		right.SetResourceReference(Border.BackgroundProperty, "B.SurfaceInset");
		right.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
		Grid el3 = new PageHeader(Ui.H1(Title), right);
		_results = Ui.VirtualList<object>(BuildRow, rowSpacing: 0);
		_listHost.Children.Add(_results);
		_listHost.Children.Add(Ui.Scroll(_placeholder));
		Grid grid2 = Ui.G("Auto,Auto,*");
		grid2.Add(el3, 0);
		grid2.Add(el2.M(0.0, 14.0, 0.0, 14.0), 1);
		grid2.Add(new CatalogWorkspace(common, Ui.V(8, destination, MatchingControls()), _listHost, targetLabel, ResultsToolbar()), 2);
		grid2.Margin = new Thickness(26.0, 20.0, 26.0, 20.0);
		base.Content = grid2;
		EnableDrop(() => new FileDrop.DropContext
		{
			Instance = CurrentInstance(),
			PreferKind = PreferredKind()
		});
		void Labeled(string label, FrameworkElement ctl)
		{
			ctl.Width = double.NaN; ctl.HorizontalAlignment = HorizontalAlignment.Stretch;
            string shortLabel = label == I18n.L("内容来源") ? I18n.L("来源") : label == I18n.L("加载器 / 类型") ? I18n.L("类型") : label == I18n.L("Minecraft 版本") ? I18n.L("版本") : label;
            var caption = Ui.Txt(shortLabel, 12, false, "B.InkMuted");
            System.Windows.Automation.AutomationProperties.SetLabeledBy(ctl, caption);
            System.Windows.Automation.AutomationProperties.SetName(ctl, label);
            var field = Ui.G(null, "Auto,*"); field.Add(caption.VCenter().M(0, 0, 6, 0), 0); field.Add(ctl, 0, 1);
			fields.Children.Add(field.M(0, 2, 10, 4));
		}
		void AddAsyncAction(string title, Func<Task> work, string error)
		{
			var item = new MenuItem { Header = title };
			item.Click += (_, _) => PageBase.RunOnce(more, work, error);
			more.ContextMenu.Items.Add(item);
		}
	}

	private string PreferredKind()
	{
		return _kind.Key switch
		{
			"mod" => "mod", 
			"modpack" => "modpack", 
			"datapack" => "datapack", 
			"resourcepack" => "resourcepack", 
			"shader" => "shaderpack", 
			"world" => "world", 
			_ => "", 
		};
	}

	private string SubTitle()
	{
		if (_installedFirst)
		{
			return I18n.L("管理所选实例里已装的模组；要装新的切到「搜索下载」");
		}
		return _kind.Key switch
		{
			"mod" => I18n.L("从 Modrinth / CurseForge 搜模组，装进所选实例"), 
			"modpack" => I18n.L("一键安装整合包到独立实例"), 
			"datapack" => I18n.L("数据包会放进实例 datapacks 目录"), 
			"resourcepack" => I18n.L("资源包会放进实例 resourcepacks 目录"), 
			"shader" => I18n.L("光影包会放进实例 shaderpacks 目录"), 
			_ => I18n.L("世界地图会解压成实例存档"), 
		};
	}

	private string Glyph()
	{
		return _kind.Key switch
		{
			"mod" => "\uea86", 
			"modpack" => "\ue7b8", 
			"datapack" => "\ue8f1", 
			"resourcepack" => "\ueb9f", 
			"shader" => "\ue706", 
			_ => "\ue909", 
		};
	}

	private string CurrentInstance()
	{
		return _inst.Str();
	}

	private string InstalledVersion()
	{
		int selectedIndex = _installedVer.SelectedIndex;
		if (selectedIndex < 0 || selectedIndex >= _installedVerValues.Count)
		{
			return "";
		}
		return _installedVerValues[selectedIndex];
	}

	private void ResetFilters()
	{
        ManualTarget();
		_sync = true;
		_search.Clear();
		_source.SelectedItem = (_source.Items.Contains(_kind.DefaultSource) ? _kind.DefaultSource : I18n.L("全部"));
		_type.SelectedIndex = 0;
		_version.Text = I18n.L("全部");
		_version.SelectedIndex = 0;
		_sync = false;
		PageBase.Run(ReloadCurrentAsync);
	}

	private string GameVersionFilter()
	{
		string text = (_version.Text ?? "").Trim();
		if (text.Length == 0 || text == I18n.L("全部"))
		{
			return "";
		}
		return text;
	}

	private string SourceParam()
	{
		string text = _source.Str();
		if (!(text == I18n.L("全部")))
		{
			return text;
		}
		return "all";
	}

	private string CategoryParam()
	{
		if (_type.SelectedIndex <= 0)
		{
			return "";
		}
		string shown = _type.Str();
		return _kind.Types.FirstOrDefault((string t) => I18n.L(t) == shown) ?? shown;
	}

	private void ShowPlaceholder(params UIElement[] kids)
	{
        ++_summaryGeneration;
        _summaries.Clear();
        _resourceTitles.Clear();
        _summaryProgress.Visibility = Visibility.Collapsed;
		_results.ItemsSource = null;
		_placeholder.Children.Clear();
		foreach (UIElement element in kids)
		{
			_placeholder.Children.Add(element);
		}
		_placeholder.Visibility = Visibility.Visible;
        _resultCount.Text = ""; _resultOrder.IsEnabled = false;
		_listHost.Children[1].Visibility = Visibility.Visible;
		_results.Visibility = Visibility.Collapsed;
	}

	private void ShowRows(IEnumerable rows)
	{
        _shownRows = rows.Cast<object>().ToList();
        int generation = ++_summaryGeneration;
        _summaries.Clear();
        _resourceTitles.Clear();
        _summaryProgress.Visibility = Visibility.Collapsed;
		_placeholder.Children.Clear();
		_listHost.Children[1].Visibility = Visibility.Collapsed;
		_results.Visibility = Visibility.Visible;
		_results.ItemsSource = SortedRows();
		if (_results.Items.Count > 0)
		{
			_results.ScrollIntoView(_results.Items[0]);
		}
        Dispatcher.BeginInvoke(new Action(() => _ = LocalizeResults(generation)), System.Windows.Threading.DispatcherPriority.ContextIdle);
	}

	private UIElement BuildRow(object item)
	{
		if (!(item is CatalogItem item2))
		{
			if (!(item is ModEntry m))
			{
				if (item is string file)
				{
					return InstalledRow(file);
				}
				return Ui.Txt(item?.ToString() ?? "");
			}
			return ModRow(m);
		}
		return ResultCard(item2);
	}

	protected override async Task LoadAsync()
	{
		_instances = (await PageBase.Api.TryCallAsync("get_instances", null, new List<InstanceInfo>())) ?? new List<InstanceInfo>();
		_sync = true;
		_inst.Fill(_instances.Select((InstanceInfo i) => i.Name), PageBase.Win?.Prefs.CatalogInstance);
		_sync = false;
		await ReloadTarget();
		await ReloadFavoritesAsync();
		PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
	}

	private CancellationToken NextSearchToken()
	{
		CancelSearch();
		_searchCts = new CancellationTokenSource(SearchTimeoutMs);
		_searchInterrupted = false;
		return _searchCts.Token;
	}

	private void CancelSearch()
	{
		_searchInFlight = false;
		CancellationTokenSource searchCts = _searchCts;
		_searchCts = null;
		if (searchCts != null)
		{
			try
			{
				searchCts.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
			searchCts.Dispose();
		}
	}

	private void ShowFailure(string title, string detail)
	{
		Button button = Ui.Btn(I18n.L("重试"), BtnKind.Soft, delegate
		{
			PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
		}, "\ue72c");
		button.HorizontalAlignment = HorizontalAlignment.Center;
		button.Margin = new Thickness(0.0, -30.0, 0.0, 0.0);
		ShowPlaceholder(Ui.Empty("\ue7ba", title, detail), button);
	}

	private SPanel EmptyWithAction(string icon, string title, string hint, string actionText, Action action)
	{
		SPanel empty = Ui.Empty(icon, title, hint);
		Button button = Ui.Btn(actionText, BtnKind.Soft, delegate
		{
			action();
		}, "\ue8fd");
		button.HorizontalAlignment = HorizontalAlignment.Center;
		button.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		empty.Children.Add(button);
		return empty;
	}

	private Task ReloadCurrentAsync()
	{
		return _mode switch
		{
			Mode.Installed => LoadInstalledAsync(), 
			Mode.Favorites => LoadFavoritesAsync(), 
			_ => SearchAsync(), 
		};
	}

	private void SetMode(Mode mode)
	{
		if (mode == Mode.Installed && _kind.InstalledMethod.Length == 0)
		{
			mode = Mode.Search;
		}
		_mode = mode;
		_modeSearch.IsChecked = mode == Mode.Search;
		_modeInstalled.IsChecked = mode == Mode.Installed;
		_modeFav.IsChecked = mode == Mode.Favorites;
		PageBase.Run(ReloadCurrentAsync);
	}

	private async Task ReloadFavoritesAsync()
	{
		List<CatalogItem> source = (await PageBase.Api.TryCallAsync("catalog_favorites", null, new List<CatalogItem>())) ?? new List<CatalogItem>();
		_favKeys = source.Select(FavKey).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	private static string FavKey(CatalogItem item)
	{
		return item.Key;
	}

	private async Task ToggleFavAsync(CatalogItem item, ToggleButton star)
	{
		List<CatalogItem> source = (await PageBase.Api.CallAsync<List<CatalogItem>>("toggle_favorite", new
		{
			item
		})) ?? new List<CatalogItem>();
		_favKeys = source.Select(FavKey).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		bool flag = _favKeys.Contains(FavKey(item));
		star.IsChecked = flag;
		star.Content = Ui.Glyph(flag ? "\ue735" : "\ue734", 13.0, flag ? "B.Accent" : "B.InkMuted");
		PageBase.Toast(flag ? I18n.L("已收藏") : I18n.L("已取消收藏"), item.DisplayName);
		if (_mode == Mode.Favorites)
		{
			await LoadFavoritesAsync();
		}
	}

	private async Task LoadFavoritesAsync()
	{
		int token = ++_searchToken;
		CancelSearch();
		ShowPlaceholder(Ui.Skeleton(56.0));
		List<CatalogItem> list = (await PageBase.Api.TryCallAsync("catalog_favorites", null, new List<CatalogItem>())) ?? new List<CatalogItem>();
		if (token == _searchToken)
		{
			_favKeys = list.Select(FavKey).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (list.Count == 0)
			{
				ShowPlaceholder(Ui.Empty("\ue734", I18n.L("还没有收藏"), I18n.L("在搜索结果里点右边的星星就能收藏")));
			}
			else
			{
				ShowRows(list);
			}
		}
	}

	private async Task SearchAsync()
	{
		int token = ++_searchToken;
		CancellationToken ct = NextSearchToken();
		ShowPlaceholder(Ui.Skeleton(64.0), Ui.Skeleton(64.0), Ui.Skeleton(64.0));
		string query = _search.Text?.Trim() ?? "";
		_searchInFlight = true;
		try
		{
			List<CatalogItem> list = (await PageBase.Api.CallAsync<List<CatalogItem>>(_kind.SearchMethod, new
			{
				query = query,
				source = SourceParam(),
				extra = new
				{
					game_version = GameVersionFilter(),
					category = CategoryParam(),
                    loader = LoaderFilter()
				}
			}, ct)) ?? new List<CatalogItem>();
			if (token == _searchToken)
			{
				if (list.Count == 0)
				{
					ShowPlaceholder(Ui.Empty(Glyph(), _kind.Empty, I18n.L("换个关键词，或切换来源 / 游戏版本试试")));
				}
				else
				{
					ShowRows(list);
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (token == _searchToken && !_searchInterrupted)
			{
				ShowFailure(I18n.L("搜索超时"), I18n.L("{0} 秒内没等到结果。检查网络或换个来源再试。", Math.Max(1, (SearchTimeoutMs + 999) / 1000)));
			}
		}
		catch (Exception ex2)
		{
			if (token == _searchToken)
			{
				ShowFailure(I18n.L("搜索失败"), ex2.Message);
			}
		}
		finally
		{
			if (token == _searchToken)
			{
				_searchInFlight = false;
			}
		}
	}

	private UIElement ResultCard(CatalogItem item)
	{
		CatalogArtwork thumbTile = new CatalogArtwork(item, 56);
		thumbTile.VerticalAlignment = VerticalAlignment.Center;
		string author = string.IsNullOrEmpty(item.Author) || item.Author == "?" ? I18n.L("作者待获取") : item.Author;
		string byline = author + " · " + DisplayLabels.Count(item.Downloads) + I18n.L(" 次下载")
			+ (string.IsNullOrEmpty(item.Source) ? "" : " · " + DisplayLabels.Source(item.Source));
		TextBlock credits = Ui.Muted(byline).Trim();
		credits.ToolTip = byline;
		var name = new ResourceTitle(item);
		_resourceTitles[item.Key] = name;
		SPanel sPanel2 = Ui.V(4, name, credits);
        var summary = new ResourceSummary(item, () => Run(() => TranslateSummaries(new List<CatalogItem> { item }, _summaryGeneration)));
        sPanel2.Children.Add(summary);
        _summaries[item.Key] = (item, summary);
		List<string> tags = item.Tags;
		if (tags != null && tags.Count > 0)
		{
			string labels = string.Join(" · ", item.Tags.Take(3).Select(HelL.Core.Launcher.CatalogPresentation.Category));
			TextBlock categories = Ui.Small(labels).Trim();
			categories.ToolTip = labels;
			sPanel2.Children.Add(categories);
		}
		bool flag = _favKeys.Contains(FavKey(item));
		ToggleButton toggleButton = new ToggleButton
		{
			Style = Ui.S("Btn.Icon"),
			IsChecked = flag,
			ToolTip = I18n.L("收藏"),
			Content = Ui.Glyph(flag ? "\ue735" : "\ue734", 13.0, flag ? "B.Accent" : "B.InkMuted")
		};
		toggleButton.Click += delegate(object s, RoutedEventArgs e)
		{
			e.Handled = true;
			PageBase.Run(() => ToggleFavAsync(item, (ToggleButton)s), I18n.L("收藏失败"));
		};
		Button install = Ui.Btn(I18n.L("安装"), BtnKind.Soft, null, "\ue896");
		install.Click += delegate(object _, RoutedEventArgs e)
		{
			e.Handled = true;
			PageBase.RunOnce(install, () => InstallAsync(item, install), I18n.L("安装失败"));
		};
		System.Windows.Automation.AutomationProperties.SetName(toggleButton, I18n.L("收藏") + " " + item.DisplayName);
        Button details = Ui.Btn(I18n.L("详情"), BtnKind.Chip, (_, _) => Run(() => ShowDetails(item)));
        var actions = Ui.H(6, details, install, toggleButton); actions.Wrap = true;
        var grid = new CatalogResultLayout(thumbTile, sPanel2, actions);
		Border obj = Ui.RowCard(grid, delegate(object _, MouseButtonEventArgs e)
		{
			if (!Ui.FromButton(e.OriginalSource as DependencyObject))
			{
				PageBase.Run(() => ShowDetails(item));
			}
		}, 10.0);
		obj.ToolTip = I18n.L("查看封面、作者与项目简介");
		return obj;
	}

	private Dictionary<string, object?> BaseExtra(string name)
	{
		return new Dictionary<string, object>
		{
			["name"] = name,
			["source"] = SourceParam(),
			["instance"] = CurrentInstance(),
			["version"] = InstalledVersion(),
			["game_version"] = GameVersionFilter(),
            ["loader"] = LoaderFilter(),
			["kind"] = _kind.FileKind
		};
	}

	private async Task InstallAsync(CatalogItem item, FrameworkElement anchor)
	{
		Dictionary<string, object?> extra = BaseExtra(item.Name);
		extra["source"] = (string.IsNullOrEmpty(item.Source) ? SourceParam() : item.Source);
		extra["slug"] = item.Slug;
		extra["id"] = item.IdValue;
		string slug = item.Slug;
		if ((slug != null && slug.Length > 0) || item.IdValue != null)
		{
			Dictionary<string, object> dictionary = await PickVersionFileAsync(item, extra);
			if (dictionary == null)
			{
				return;
			}
			extra = dictionary;
		}
		if (_kind.Key == "datapack")
		{
			extra = await PickSaveAsync(extra);
		}
		else if (_kind.Key == "world")
		{
			string text = await SavesDialog.PickSaveTargetAsync(CurrentInstance(), I18n.L("装进哪里 · {0}", item.DisplayName));
			if (text == null)
			{
				return;
			}
			extra["version"] = text;
		}
		if (_kind.Key == "mod" && _installedNames != null && _installedNames.Count > 0)
		{
			string slugKey = (item.Slug ?? item.Name ?? "").ToLowerInvariant();
			List<string> required = new List<string>();
			if (slugKey.Contains("iris") || slugKey.Contains("sodium") || slugKey.Contains("lithium") || slugKey.Contains("indium"))
			{
				required.Add("fabric-api");
			}
			if (slugKey.Contains("cloth") || slugKey.Contains("roughly-enough-items") || slugKey.Contains("rei"))
			{
				required.Add("architectury");
			}
			var suggestions = StreamlineLaunchUtils.RecommendMissingModFixes(_installedNames, required);
			if (suggestions.Count > 0)
			{
				string warning = NetworkAndCatalogUtils.FormatDependencyWarning(suggestions.Select(s => s.DisplayName));
				if (!string.IsNullOrEmpty(warning))
				{
					PageBase.Toast(I18n.L("依赖提醒"), warning, ToastKind.Warning);
				}
			}
		}
		await DoInstallAsync(extra, anchor);
	}

	private async Task<Dictionary<string, object?>> PickSaveAsync(Dictionary<string, object?> extra)
	{
		string text = CurrentInstance();
		if (string.IsNullOrEmpty(text))
		{
			return extra;
		}
		List<SaveRow> list = (await PageBase.Api.TryCallAsync("list_saves", new
		{
			instance = text,
			version = ""
		}, new List<SaveRow>())) ?? new List<SaveRow>();
		if (list.Count == 0)
		{
			return extra;
		}
		ComboBox pick = Ui.Combo(new string[1] { I18n.L("不装进存档") }.Concat<string>(list.Select((SaveRow s) => s.Name)));
		if (!(await Dlg.Ask(I18n.L("装进存档"), Ui.V(8.0, Ui.Muted(I18n.L("可选：把数据包直接装进某个世界，或只放到 datapacks 文件夹。")), pick), I18n.L("继续"))))
		{
			return extra;
		}
		if (pick.SelectedIndex <= 0)
		{
			return extra;
		}
		return new Dictionary<string, object>(extra) { ["save"] = pick.Str() };
	}

	private async Task<Dictionary<string, object?>?> PickVersionFileAsync(CatalogItem item, Dictionary<string, object?> extra)
	{
		List<CatalogFile> files;
		using (Dlg.Busy(I18n.L("正在获取版本文件…")))
		{
			files = (await PageBase.Api.CallAsync<List<CatalogFile>>("list_catalog_files", new { extra })) ?? new List<CatalogFile>();
		}
		if (files.Count == 0)
		{
			await Dlg.Alert(I18n.L("没有找到版本文件"), I18n.L("当前来源没有符合条件的文件，请调整游戏版本或加载器后重试。"));
            return null;
		}
		CatalogFile chosen = files[0];
        CatalogFile? latest = files[0];
		SPanel listHost = Ui.V(6.0);
		SmoothScroll smoothScroll = Ui.Scroll(listHost);
		smoothScroll.MaxHeight = 340.0;
		List<string> list = new List<string> { I18n.L("全部") };
		List<string> list2 = new List<string> { I18n.L("全部") };
		foreach (CatalogFile item2 in files)
		{
			foreach (string item3 in item2.GameVersions ?? new List<string>())
			{
				if (!list.Contains(item3))
				{
					list.Add(item3);
				}
			}
			foreach (string item4 in item2.Loaders ?? new List<string>())
			{
				if (!list2.Contains(item4))
				{
					list2.Add(item4);
				}
			}
		}
		ComboBox gvBox = Ui.Combo(list, I18n.L("全部"), 130.0);
		ComboBox loaderBox = Ui.Combo(list2, I18n.L("全部"), 130.0);
		TextBlock count = Ui.Muted("");
		gvBox.SelectionChanged += delegate
		{
			Refill();
		};
		loaderBox.SelectionChanged += delegate
		{
			Refill();
		};
		Refill();
		var logButton = Ui.Btn(I18n.L("查看所选更新日志"), BtnKind.Ghost, (_, _) =>
        {
            if (chosen == null) return;
            var document = new ResourceDocumentPanel(item, "changelog", version: chosen.IdValue?.ToString() ?? "");
            Dlg.Panel(I18n.L("更新日志") + " · " + chosen.VersionNumber, document, 720);
        });
		SPanel sPanel = Ui.H(8.0, Ui.Txt(I18n.L("游戏版本"), 12.0, bold: false, "B.InkMuted").VCenter(), gvBox.VCenter(), Ui.Txt(I18n.L("加载器"), 12.0, bold: false, "B.InkMuted").VCenter(), loaderBox.VCenter(), count.VCenter(), logButton);
		sPanel.Wrap = true;
        var origin = Ui.Small(I18n.L(files[0].VersionSource) + (files[0].VersionWarning.Length > 0 ? " · " + I18n.L(files[0].VersionWarning) : "")).Wrap();
		switch (await Dlg.Choose(I18n.L("选择版本文件 · {0}", item.DisplayName), Ui.V(10.0, origin, sPanel, smoothScroll), new string[3]
		{
			I18n.L("取消"),
			I18n.L("安装最新"),
			I18n.L("安装所选")
		}, 660.0))
		{
		case 1:
            chosen = latest;
            goto case 2;
		case 2:
			if (chosen != null)
			{
				Dictionary<string, object> dictionary = new Dictionary<string, object>(extra)
				{
					["version_id"] = chosen.IdValue,
					["filename"] = chosen.Filename
				};
				if ((item.Source ?? "").ToLowerInvariant().StartsWith("curse") || chosen.Source == "curseforge")
				{
					dictionary["file_id"] = chosen.IdValue;
				}
				return dictionary;
			}
			break;
		}
		return null;
		RadioButton FileRow(CatalogFile f)
		{
			string relType = string.IsNullOrEmpty(f.ReleaseType) ? "release" : f.ReleaseType.ToLowerInvariant();
			string badgeColor = relType switch
			{
				"alpha" => "B.Danger",
				"beta" => "B.Warn",
				_ => "B.Accent"
			};
			TextBlock badge = Ui.Txt(DisplayLabels.Of(relType), 11.0, bold: true, badgeColor);
			TextBlock textBlock = Ui.Txt(string.IsNullOrEmpty(f.VersionNumber) ? f.Name : f.VersionNumber, 12.5, bold: true).Trim();
			var normLoaders = ModernVersionCatalogUtils.NormalizeLoaderTags(f.Loaders ?? new List<string>());
			string loadersStr = normLoaders.Count > 0 ? string.Join(", ", normLoaders) : string.Join(", ", f.Loaders ?? new List<string>());
			TextBlock el = Ui.Small(string.Join(", ", (f.GameVersions ?? new List<string>()).Take(4)) + " · " + loadersStr + " · " + f.Date + " · " + DisplayLabels.Count(f.Downloads) + I18n.L(" 次下载"));
			var option = new RadioButton
			{
				Content = Ui.V(4.0, Ui.H(6.0, textBlock, badge.VCenter()), el.Wrap()),
				Style = Ui.S("Choice.Row"), GroupName = "CatalogVersion", Tag = f,
				IsChecked = f == chosen
			};
			System.Windows.Automation.AutomationProperties.SetName(option, f.Name + " " + f.VersionNumber);
			option.Checked += delegate
			{
				chosen = f;
			};
			return option;
		}
		void Refill()
		{
			string gv = gvBox.Str();
			string ld = loaderBox.Str();
			bool anyGv = gvBox.SelectedIndex <= 0;
			bool anyLd = loaderBox.SelectedIndex <= 0;
			List<CatalogFile> list3 = files.Where(delegate(CatalogFile f)
			{
				if (!anyGv)
				{
					List<string> gameVersions = f.GameVersions;
					if (gameVersions != null && gameVersions.Count > 0 && !f.GameVersions.Contains(gv))
					{
						return false;
					}
				}
				if (!anyLd)
				{
					List<string> loaders = f.Loaders;
					if (loaders != null && loaders.Count > 0 && !f.Loaders.Any((string l) => string.Equals(l, ld, StringComparison.OrdinalIgnoreCase)))
					{
						return false;
					}
				}
				return true;
			}).ToList();
			if (list3.Count > 0 && !list3.Contains(chosen))
			{
				chosen = list3[0];
			}
			if (list3.Count == 0) chosen = null;
            latest = list3.FirstOrDefault();
			listHost.Children.Clear();
			foreach (CatalogFile item6 in list3.Take(80))
			{
				listHost.Children.Add(FileRow(item6));
			}
			count.Text = I18n.L("{0} 个匹配 / 共 {1} 个文件", list3.Count, files.Count);
			if (list3.Count == 0) listHost.Children.Add(Ui.Muted(I18n.L("没有匹配的版本，请调整筛选条件。")));
		}
	}

	private async Task<string> DoInstallAsync(Dictionary<string, object?> extra, FrameworkElement? anchor)
	{
		object value;
		string name = ((!extra.TryGetValue("name", out value)) ? _kind.Title : (value?.ToString() ?? _kind.Title));
		BridgeClient api;
		string installMethod;
		object obj;
		if (_kind.IsModpack)
		{
			api = PageBase.Api;
			installMethod = _kind.InstallMethod;
			if (extra.TryGetValue("source", out object value2))
			{
				string text = value2?.ToString();
				if (text != null && text.Length > 0)
				{
					obj = text;
					goto IL_015a;
				}
			}
			obj = "Modrinth";
			goto IL_015a;
		}
		string result = await PageBase.Api.StartTaskAsync(_kind.InstallMethod, new
		{
			name = name,
			instance = CurrentInstance(),
			extra = extra
		});
		goto IL_01df;
		IL_015a:
		result = await api.StartTaskAsync(installMethod, new
		{
			name = name,
			source = (string)obj,
			extra = extra
		});
		goto IL_01df;
		IL_01df:
		if (anchor != null && SettingsPage.FlyEnabled)
		{
			PageBase.Win?.FlyToTasks(anchor, name);
		}
		return result;
	}

	private async Task InstallFromLinkAsync(FrameworkElement anchor)
	{
		string text = await Dlg.Prompt(_kind.Title + I18n.L(" · 链接安装"), _kind.LinkHint, "", "https://…");
		if (!string.IsNullOrWhiteSpace(text))
		{
			Dictionary<string, object> dictionary = BaseExtra(text.Trim());
			dictionary["url"] = text.Trim();
			await DoInstallAsync(dictionary, anchor);
			PageBase.Win?.Navigate("tasks");
		}
	}

	private async Task ImportLocalAsync(FrameworkElement anchor)
	{
		string[] files = Dlg.PickFiles(_kind.LocalFilter, I18n.L("导入本地") + _kind.Title);
		if (files == null || files.Length == 0)
		{
			return;
		}
		if (SettingsPage.FlyEnabled)
		{
			PageBase.Win?.FlyToTasks(anchor, I18n.L("{0} 个文件", files.Length));
		}
		PageBase.Win?.Navigate("tasks");
		foreach (string file in files)
		{
			Dictionary<string, object> options = BaseExtra(Path.GetFileName(file));
			options["path"] = file;
			options["source"] = "本地";
			string taskId = await DoInstallAsync(options, null);
			if (!string.IsNullOrEmpty(taskId))
			{
				await PageBase.Api.TryCallAsync<OpResult>("wait_task", new
				{
					task_id = taskId,
					timeout = 1800
				});
			}
		}
		if (_mode != Mode.Search) await ReloadCurrentAsync();
	}

	private async Task LoadInstalledAsync()
	{
		int token = ++_searchToken;
		CancelSearch();
		ShowPlaceholder(Ui.Skeleton(46.0));
		string inst = CurrentInstance();
		if (string.IsNullOrEmpty(inst))
		{
			ShowPlaceholder(Ui.Empty(Glyph(), I18n.L("还没有实例"), I18n.L("先到「版本管理」页新建一个")));
			return;
		}
		await FillInstalledVersionsAsync(inst);
		string version = InstalledVersion();
		try
		{
			if (_kind.Key == "mod")
			{
				List<ModEntry> list = (await PageBase.Api.CallAsync<List<ModEntry>>("get_installed_mod_entries", new
				{
					instance = inst,
					version = version
				})) ?? new List<ModEntry>();
				if (token == _searchToken)
				{
					if (list.Count == 0)
					{
						ShowPlaceholder(EmptyWithAction(Glyph(), I18n.L("这个实例还没有模组"), I18n.L("切回「搜索下载」装一个"), I18n.L("去搜索下载"), delegate
						{
							SetMode(Mode.Search);
						}));
					}
					else
					{
						ShowRows(list);
					}
				}
				return;
			}
			List<string> list2 = (await PageBase.Api.CallAsync<List<string>>(_kind.InstalledMethod, new
			{
				instance = inst
			})) ?? new List<string>();
			if (token == _searchToken)
			{
				if (list2.Count == 0)
				{
					ShowPlaceholder(EmptyWithAction(Glyph(), I18n.L("这个实例还没有{0}", _kind.Title), I18n.L("切回「搜索下载」装一个"), I18n.L("去搜索下载"), delegate
					{
						SetMode(Mode.Search);
					}));
				}
				else
				{
					_installedNames = list2;
					ShowRows(list2);
				}
			}
		}
		catch (Exception ex)
		{
			if (token == _searchToken)
			{
				ShowFailure(I18n.L("读取已安装列表失败"), ex.Message);
			}
		}
	}

	private async Task FillInstalledVersionsAsync(string instance)
	{
		List<ModsTarget> list = (await PageBase.Api.TryCallAsync("get_mods_targets", new { instance }, new List<ModsTarget>())) ?? new List<ModsTarget>();
		if (list.Count == 0)
		{
			list.Add(new ModsTarget
			{
				Label = I18n.L("实例共享目录"),
				Value = ""
			});
		}
		int selectedIndex = _installedVer.SelectedIndex;
		_sync = true;
		_installedVer.Items.Clear();
		foreach (ModsTarget item in list)
		{
			_installedVer.Items.Add(item.Label);
		}
		_installedVer.SelectedIndex = ((selectedIndex >= 0 && selectedIndex < list.Count) ? selectedIndex : 0);
		_installedVerValues = list.Select((ModsTarget t) => t.Value).ToList();
		_sync = false;
	}

	private UIElement ModRow(ModEntry m)
	{
		string inst = CurrentInstance();
		string ver = InstalledVersion();
		string file = m.Filename;
		CheckBox checkBox = Ui.Switch(m.Enabled);
		checkBox.Checked += delegate
		{
			PageBase.Run(() => PageBase.Api.TryCallAsync<object>("enable_mod", new
			{
				instance = inst,
				filename = file,
				version = ver
			}));
		};
		checkBox.Unchecked += delegate
		{
			PageBase.Run(() => PageBase.Api.TryCallAsync<object>("disable_mod", new
			{
				instance = inst,
				filename = file,
				version = ver
			}));
		};
		Button el = Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteInstalledAsync(file), I18n.L("删除失败"));
		});
		Grid grid = Ui.G(null, "Auto,*,Auto,Auto");
		grid.Add(checkBox.M(0.0, 0.0, 10.0).VCenter(), 0);
		grid.Add(Ui.V(1.0, Ui.Txt(string.IsNullOrWhiteSpace(m.Name) ? file : m.Name).Trim(), Ui.Small(string.Join(" · ", new string[3] { file, m.Version, m.Size }.Where((string x) => !string.IsNullOrEmpty(x))))).VCenter(), 0, 1);
		grid.Add(Ui.Small(m.Enabled ? "" : I18n.L("已禁用")).VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 2);
		grid.Add(el.VCenter().M(8.0, 0.0, 0.0, 0.0), 0, 3);
		return Ui.RowCard(grid, null, 10.0);
	}

	private UIElement InstalledRow(string file)
	{
		Button el = Ui.Btn(_kind.IsModpack ? I18n.L("移除标记") : I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteInstalledAsync(file), I18n.L("删除失败"));
		});
		Grid grid = Ui.G(null, "*,Auto,Auto");
		grid.Add(Ui.Txt(file, 12.5).Trim().VCenter(), 0);
		if (ExportKind().Length > 0)
		{
			grid.Add(Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
			{
				PageBase.Run(() => ExportInstalledAsync(file), I18n.L("导出失败"));
			}, "\uede1").VCenter().M(0.0, 0.0, 6.0), 0, 1);
		}
		grid.Add(el.VCenter(), 0, 2);
		return Ui.RowCard(grid, null, 10.0);
	}

	private string ExportKind()
	{
		return _kind.Key switch
		{
			"mod" => "mod", 
			"shader" => "shader", 
			"resourcepack" => "resourcepack", 
			"datapack" => "datapack", 
			"world" => "world", 
			_ => "", 
		};
	}

	private async Task ExportInstalledAsync(string name)
	{
		string kind = ExportKind();
		if (kind.Length != 0)
		{
			string text = (await PageBase.Api.TryCallAsync("default_export_dir", null, "")) ?? "";
			string folder = Dlg.PickFolder(string.IsNullOrEmpty(text) ? I18n.L("选择导出位置") : I18n.L("选择导出位置（上次：{0}）", text));
			if (folder != null)
			{
				await PageBase.Api.TryCallAsync("remember_export_dir", new
				{
					path = folder
				}, folder);
				string text2 = await PageBase.Api.CallAsync<string>("export_content", new
				{
					kind = kind,
					name = name,
					dest_dir = folder,
					version = InstalledVersion(),
					instance = CurrentInstance()
				});
				PageBase.Toast(I18n.L("已导出"), text2 ?? folder, ToastKind.Success);
			}
		}
	}

	private async Task ExportAllInstalledAsync(List<string> names)
	{
		string kind = ExportKind();
		if (kind.Length == 0 || names.Count == 0)
		{
			return;
		}
		string text = (await PageBase.Api.TryCallAsync("default_export_dir", null, "")) ?? "";
		string folder = Dlg.PickFolder(string.IsNullOrEmpty(text) ? I18n.L("选择导出位置") : I18n.L("选择导出位置（上次：{0}）", text));
		if (folder == null)
		{
			return;
		}
		await PageBase.Api.TryCallAsync("remember_export_dir", new
		{
			path = folder
		}, folder);
		ExportResult exportResult;
		using (Dlg.Busy(I18n.L("正在导出 {0} 个…", names.Count)))
		{
			exportResult = await PageBase.Api.CallAsync<ExportResult>("export_contents", new
			{
				kind = kind,
				names = names,
				dest_dir = folder,
				version = InstalledVersion(),
				instance = CurrentInstance()
			});
		}
		if (exportResult != null)
		{
			if (exportResult.Failed.Count == 0)
			{
				PageBase.Toast(I18n.L("已导出"), I18n.L("{0} 个 → {1}", exportResult.Ok.Count, folder), ToastKind.Success);
			}
			else
			{
				await Dlg.Alert(I18n.L("部分没能导出"), I18n.L("成功 {0} 个，失败 {1} 个：\n\n", exportResult.Ok.Count, exportResult.Failed.Count) + string.Join("\n", exportResult.Failed));
			}
		}
	}

	private void SniffClipboard()
	{
		string text;
		try
		{
			text = (Clipboard.GetText() ?? "").Trim();
		}
		catch
		{
			return;
		}
		if (text.Length == 0 || text == _clipSeen)
		{
			return;
		}
		string text2 = text.ToLowerInvariant();
		if (text2.Contains("modrinth.com") || text2.Contains("curseforge.com"))
		{
			_clipSeen = text;
			if ((_search.Text ?? "").Trim().Length == 0)
			{
				_search.Text = text;
			}
			PageBase.Toast(I18n.L("识别到剪贴板链接"), (text.Length > 96) ? (text.Substring(0, 96) + "…") : text);
		}
	}

	private async Task DeleteInstalledAsync(string filename)
	{
		if (string.IsNullOrEmpty(_kind.DeleteMethod))
		{
			return;
		}
		string inst = CurrentInstance();
		if (_kind.DeleteMethod == "delete_modpack")
		{
			if (!(await Dlg.Confirm(I18n.L("移除整合包标记"), I18n.L("只清掉「已安装整合包」这条记录，游戏目录里的模组与存档一个不动。\n要腾空间的话，到「原版游戏」里逐个卸载版本。"), I18n.L("移除标记"), I18n.L("取消"))))
			{
				return;
			}
			await PageBase.Api.CallAsync("delete_modpack", new
			{
				instance = inst,
				filename = filename,
				purge_instance = false
			});
		}
		else
		{
			if (!(await Dlg.Confirm(I18n.L("删除确认"), I18n.L("将删除「{0}」，不可恢复。", filename), I18n.L("删除"), I18n.L("取消"), danger: true)))
			{
				return;
			}
			if (!(_kind.DeleteMethod == "delete_mod"))
			{
				await PageBase.Api.CallAsync(_kind.DeleteMethod, new
				{
					instance = inst,
					filename = filename
				});
			}
			else
			{
				await PageBase.Api.CallAsync(_kind.DeleteMethod, new
				{
					instance = inst,
					filename = filename,
					version = ""
				});
			}
		}
		PageBase.Toast(I18n.L("已处理"), filename, ToastKind.Success);
		await LoadInstalledAsync();
	}

	public override void OnEvent(BridgeEvent ev)
	{
		if (ev.Event == "finished" && ev.Success && _mode == Mode.Installed)
		{
			PageBase.Run(LoadInstalledAsync);
		}
	}

	public override void OnShown()
	{
		SniffClipboard();
		if (_searchInterrupted)
		{
			PageBase.Run(ReloadCurrentAsync, I18n.L("搜索失败"));
		}
	}

	public override void OnHidden()
	{
		_debounce.Stop();
		if (_searchInFlight)
		{
			_searchInterrupted = true;
			CancelSearch();
		}
	}
}
