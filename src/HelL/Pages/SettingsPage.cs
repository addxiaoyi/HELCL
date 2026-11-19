using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class SettingsPage : PageBase
{
	internal sealed class AiModelList
	{
		public bool Ok { get; set; }

		public List<string> Models { get; set; } = new List<string>();

		public string Error { get; set; } = "";
	}

	internal sealed class ThemeExtras
	{
		public string ThemeColor { get; set; } = DefaultColor;

		public string Background { get; set; } = "default";

		public string Skin { get; set; } = "win11";

		public int Tint { get; set; } = 62;

		public bool AutoAccent { get; set; }

		public bool FlyAnimation { get; set; } = true;

		public int FlyDurationMs { get; set; } = 620;
	}

	private sealed class Opt
	{
		public required string Key { get; init; }

		public required string Label { get; init; }

		public override string ToString()
		{
			return Label;
		}
	}

	private const string DefaultColor = "#A99AFF";

	// 必须排在 _visualColor 前面：静态字段按书写顺序初始化
	internal static ThemeExtras Extras { get; } = LoadExtras();

	private static string _visualColor = NormalizeColor(Extras.ThemeColor) ?? DefaultColor;

	private static string _visualBackground = NormalizeBackground(Extras.Background);

	private static string _visualSkin = NormalizeSkin(Extras.Skin);

	private static int _visualTint = NormalizeTint(Extras.Tint);

	internal static bool AutoAccent { get; private set; } = Extras.AutoAccent;

	private static readonly (string Key, string Label)[] IsoOpts = new(string, string)[4]
	{
		("none", I18n.L("关闭（共用实例目录）")),
		("saves", I18n.L("隔离存档")),
		("mods", I18n.L("隔离 Mod 与配置")),
		("all", I18n.L("隔离全部"))
	};

	private static readonly (string Key, string Label)[] GcOpts = new(string, string)[5]
	{
		("auto", I18n.L("G1（推荐）")),
		("g1", "G1"),
		("g1_tuned", I18n.L("调优 G1")),
		("zgc", "ZGC"),
		("none", I18n.L("不指定"))
	};

	private static readonly (string Key, string Label)[] VisOpts = new(string, string)[5]
	{
		("keep", I18n.L("保持显示")),
		("minimize", I18n.L("最小化")),
		("hide", I18n.L("隐藏")),
		("hide_reopen", I18n.L("隐藏，退出后重开")),
		("close", I18n.L("关闭启动器"))
	};

	private static readonly (string Key, string Label)[] HomeOpts = new(string, string)[3]
	{
		("news", I18n.L("Minecraft 新闻")),
		("custom", I18n.L("本地 HTML")),
		("blank", I18n.L("空白"))
	};

	private static readonly (string Key, string Label)[] WinOpts = new(string, string)[2]
	{
		("window", I18n.L("窗口")),
		("maximize", I18n.L("全屏"))
	};

	private static readonly (string Key, string Label)[] SrcOpts = new(string, string)[5]
	{
		("auto", I18n.L("自动（按当前网络选择）")),
		("official", I18n.L("仅官方")),
		("bmclapi", I18n.L("BMCLAPI 优先")),
		("mc_mirror", I18n.L("MC Mirror 优先")),
		("mc_mirror_west", I18n.L("MC Mirror 美西优先"))
	};

	private static readonly (string Key, string Label)[] CommOpts = new(string, string)[3]
	{
		("auto", I18n.L("自动")),
		("official", I18n.L("仅官方")),
		("mcim", I18n.L("仅 MCIM"))
	};

	private static readonly (string Key, string Label)[] BgOpts = new(string, string)[4]
	{
		("default", I18n.L("默认")),
		("eye", I18n.L("护眼绿")),
		("warm", I18n.L("暖米")),
		("mist", I18n.L("雾蓝"))
	};

	private static readonly (string Key, string Label)[] SkinOpts = new(string, string)[2]
	{
		("win11", I18n.L("Windows 11 · Mica")),
		("macos", I18n.L("macOS 风格 · Acrylic"))
	};

	private static readonly (string Key, string Label)[] AiModeOpts = new(string, string)[4]
	{
		("builtin", I18n.L("内置公益网关（免配置）")),
		("custom", I18n.L("自定义 OpenAI 兼容端点")),
		("public", I18n.L("自建 AI 网关")),
		("local", I18n.L("本地 Ollama（在 AI 助手的模型库管理）"))
	};

	private static readonly (string Key, string Label)[] AiPermOpts = new(string, string)[4]
	{
		("default", I18n.L("标准（写操作先问）")),
		("acceptEdits", I18n.L("少打扰（写操作直接执行）")),
		("plan", I18n.L("只看不动（禁止写操作）")),
		("yolo", I18n.L("全自动"))
	};

	private static readonly (string Name, string Hex)[] Palette = new(string, string)[7]
	{
		(I18n.L("星紫"), DefaultColor),
		(I18n.L("松绿"), "#2E9B6B"),
		(I18n.L("湛蓝"), "#2F72C4"),
		(I18n.L("紫罗兰"), "#7C5CFC"),
		(I18n.L("暖橙"), "#D68A17"),
		(I18n.L("绯红"), "#D64545"),
		(I18n.L("青碧"), "#149B9B")
	};

	private readonly CheckBox _dark = Ui.Switch();

	private readonly CheckBox _motion = Ui.Switch(value: true);

	private readonly CheckBox _fly = Ui.Switch(value: true);

	private readonly Slider _flyDur = Ui.Sld(200.0, 1200.0, 620.0, 20.0);

	private readonly TextBlock _flyDurLbl = Ui.Txt("620 ms", 12.0, bold: true);

	private readonly SPanel _swatches = Ui.H(8.0);

	private readonly ComboBox _bg = KeyCombo(BgOpts);

	private readonly ComboBox _skin = KeyCombo(SkinOpts);

	private readonly Slider _tint = Ui.Sld(0.0, 100.0, 62.0, 1.0);

	private readonly TextBlock _tintLbl = Ui.Txt("62", 12.0, bold: true);

	private readonly CheckBox _autoAccent = Ui.Switch();

	private readonly Slider _threads = Ui.Sld(1.0, 64.0, 8.0);

	private readonly TextBlock _threadsLbl = Ui.Txt("8", 12.0, bold: true);

	private readonly Slider _segs = Ui.Sld(1.0, 16.0, 4.0);

	private readonly TextBlock _segsLbl = Ui.Txt("4", 12.0, bold: true);

	private readonly ComboBox _src = KeyCombo(SrcOpts);

	private readonly ComboBox _comm = KeyCombo(CommOpts);

	private readonly CheckBox _proxy = Ui.Switch(value: true);

	private readonly TextBox _limit = Ui.Input("0", "0", 110.0);

	private readonly Slider _mem = Ui.Sld(512.0, 32768.0, 4096.0, 256.0);

	private readonly TextBlock _memLbl = Ui.Txt("4096 MB", 12.0, bold: true);

	private readonly TextBox _w = Ui.Input("854", "854", 80.0);

	private readonly TextBox _h = Ui.Input("480", "480", 80.0);

	private readonly ComboBox _inst = Ui.Combo(null, null, 200.0);

	private readonly ComboBox _iso = KeyCombo(IsoOpts);

	private readonly ComboBox _gc = KeyCombo(GcOpts);

	private readonly ComboBox _win = KeyCombo(WinOpts);

	private readonly TextBox _jvm = Ui.Input(I18n.L("例如 -XX:+UseG1GC（可空）"));

	private readonly CheckBox _shareLib = Ui.Switch();

	private readonly CheckBox _shareAssets = Ui.Switch();

	private readonly CheckBox _multi = Ui.Switch();

	private readonly ComboBox _vis = KeyCombo(VisOpts);

	private readonly ComboBox _home = KeyCombo(HomeOpts);

	private readonly TextBox _homePath = Ui.Input(I18n.L("本地 .html 文件路径（主页选「本地 HTML」时生效）"));

	private readonly CheckBox _autoUpdate = Ui.Switch(value: true);


	private readonly ComboBox _lang = KeyCombo(Array.Empty<(string, string)>());

	private readonly TextBlock _langPreview = Ui.Small("");

	private readonly TextBox _gameDir = Ui.Input(I18n.L("游戏目录（.minecraft 的落脚处）"));

	private readonly TextBlock _cleanInfo = Ui.Small(I18n.L("还没扫描"));

	private readonly ComboBox _themePack = Ui.Combo(null, null, 200.0);

	private readonly TextBlock _themeInfo = Ui.Small("");

	private readonly ComboBox _aiMode = KeyCombo(AiModeOpts);

	private readonly ComboBox _aiPerm = KeyCombo(AiPermOpts);

	private readonly TextBox _aiGateway = Ui.Input(I18n.L("https://…（公共网关地址）"));

	private readonly TextBox _aiBase = Ui.Input("https://api.openai.com/v1");

	private readonly SecretInput _aiKey = new SecretInput("AI API Key");

	private readonly ComboBox _aiModel = Ui.Combo(null, null, 220.0, I18n.L("模型名"), editable: true);

	private readonly Button _aiModelFetch;

	private readonly TextBox _aiCtx = Ui.Input("131072");

	private readonly TextBox _aiFallback = Ui.Input(I18n.L("留空不启用"));

	private readonly CheckBox _aiConfirm = Ui.Switch(value: true);


	private readonly SecretInput _cfKey = new SecretInput("CurseForge API Key（留空不修改）");

	private bool _cfSet;

	private bool _aiKeySet;

	private bool _cfClear;

	private bool _aiKeyClear;

	private string _cfHint = "";

	private string _aiKeyHint = "";

	private readonly Button _cfReset;

	private readonly Button _aiKeyReset;

	private readonly TextBox _bgPath = Ui.Input(I18n.L("图片或视频路径，留空则不用壁纸"));

	private readonly TextBox _bgFolder = Ui.Input(I18n.L("壁纸文件夹（设了就轮播，优先于上面那张）"));

	private readonly CheckBox _bgShuffle = Ui.Switch();

	private readonly Slider _bgInterval = Ui.Sld(1.0, 120.0, 10.0);

	private readonly TextBlock _bgIntervalLbl = Ui.Txt(I18n.L("10 分钟"), 12.0, bold: true);

	private readonly Slider _bgBlur = Ui.Sld(0.0, 40.0, 0.0);

	private readonly TextBlock _bgBlurLbl = Ui.Txt("0 px", 12.0, bold: true);

	private readonly Slider _bgDim = Ui.Sld(0.0, 80.0, 0.0);

	private readonly TextBlock _bgDimLbl = Ui.Txt("0 %", 12.0, bold: true);

	private readonly TextBlock _bgInfo = Ui.Small("");

	private readonly Button _bgUndo;

	private readonly Button _clean;

	private readonly Button _testAi;

	private readonly Button _rec;

	private string _lang0 = "";

	private string _gameDir0 = "";

	private string _color = DefaultColor;

	private bool _sync;

	private CleanerPreview? _cleanPreview;

	private static readonly string[] AccentKeys = new string[24]
	{
		"C.Accent", "C.AccentStrong", "C.AccentText", "C.AccentDeep", "C.AccentLite", "B.Accent", "B.AccentStrong", "B.OnAccentStrong", "B.AccentText", "B.AccentTint",
		"B.AccentDeep", "B.AccentLite", "B.AccentSoft", "B.AccentSoft2", "B.OnAccent", "B.Glass", "B.GlassStrong", "B.GlassBorder", "B.Ok", "B.OnOk",
		"B.BannerFill", "B.OnBanner", "B.LogoFill", "B.OnLogo"
	};

	private static readonly string[] BackgroundKeys = new string[3] { "C.Canvas", "B.Canvas", "B.PageWash" };

	public override string Title => I18n.L("设置");

	internal static bool FlyEnabled
	{
		get
		{
			if (Extras.FlyAnimation)
			{
				return Motion.Enabled;
			}
			return false;
		}
	}

	internal static string VisualThemeColor => _visualColor;

	private static string ExtrasPath
	{
		get
		{
			string path;
			try
			{
				path = BridgeHost.FindRoot();
			}
			catch
			{
				path = AppContext.BaseDirectory;
			}
			return Path.Combine(path, "wpf-theme.json");
		}
	}

	private bool IsBuiltinAi => KeyOf(_aiMode) == "builtin";
	private string _aiEditorMode = "";
	private string _aiCloudModel = "";
	private string _aiLocalModel = "";

	internal static bool SyncPersistedThemeColor(string? color)
	{
		string text = NormalizeColor(color);
		if (text == null)
		{
			return false;
		}
		bool result = !string.Equals(_visualColor, text, StringComparison.OrdinalIgnoreCase);
		_visualColor = text;
		if (!string.Equals(Extras.ThemeColor, text, StringComparison.OrdinalIgnoreCase))
		{
			Extras.ThemeColor = text;
		}
		return result;
	}

	internal static bool SyncPersistedBackground(string? preset)
	{
		if (string.IsNullOrWhiteSpace(preset))
		{
			return false;
		}
		string text = NormalizeBackground(preset.Trim());
		bool result = text != _visualBackground;
		_visualBackground = text;
		if (Extras.Background != text)
		{
			Extras.Background = text;
		}
		return result;
	}

	private static ThemeExtras LoadExtras()
	{
		try
		{
            string settings = Path.Combine(Path.GetDirectoryName(ExtrasPath), "config.json");
            if (File.Exists(settings))
            {
                using var saved = JsonDocument.Parse(File.ReadAllText(settings));
                if (saved.RootElement.TryGetProperty("ui_wpf_theme", out JsonElement theme))
                    return JsonSerializer.Deserialize<ThemeExtras>(theme.GetRawText()) ?? new ThemeExtras();
            }
			if (File.Exists(ExtrasPath))
			{
				return JsonSerializer.Deserialize<ThemeExtras>(File.ReadAllText(ExtrasPath)) ?? new ThemeExtras();
			}
		}
		catch
		{
		}
		return new ThemeExtras();
	}

	private Dictionary<string, object> ThemeSnapshot() => new Dictionary<string, object>
    {
        ["ThemeColor"] = _color, ["Background"] = KeyOf(_bg), ["Skin"] = KeyOf(_skin),
        ["Tint"] = (int)_tint.Value, ["AutoAccent"] = AutoAccent,
        ["FlyAnimation"] = _fly.IsChecked == true, ["FlyDurationMs"] = (int)_flyDur.Value
    };

	private static ComboBox KeyCombo(IEnumerable<(string Key, string Label)> items, double width = 200.0)
	{
		ComboBox comboBox = new ComboBox
		{
			Style = Ui.S("Combo"),
			Width = width
		};
		foreach (var (key, label) in items)
		{
			comboBox.Items.Add(new Opt
			{
				Key = key,
				Label = label
			});
		}
		if (comboBox.Items.Count > 0)
		{
			comboBox.SelectedIndex = 0;
		}
		return comboBox;
	}

	private static void SelectKey(ComboBox c, string key)
	{
		foreach (object item in (IEnumerable)c.Items)
		{
			if (item is Opt opt && opt.Key == key)
			{
				c.SelectedItem = item;
				return;
			}
		}
		if (c.Items.Count > 0)
		{
			c.SelectedIndex = 0;
		}
	}

	private static string KeyOf(ComboBox c)
	{
		return (c.SelectedItem as Opt)?.Key ?? "";
	}

	public SettingsPage()
	{
		_dark.Checked += delegate
		{
			if (!_sync)
			{
				ApplyDark(_dark.IsChecked == true);
			}
		};
		_dark.Unchecked += delegate
		{
			if (!_sync)
			{
				ApplyDark(dark: false);
			}
		};
		_bg.SelectionChanged += delegate
		{
			if (!_sync)
			{
				ApplyVisuals(_color, KeyOf(_bg));
			}
		};
		_skin.SelectionChanged += delegate
		{
			if (!_sync)
			{
				Glass.Use(KeyOf(_skin));
				ApplyVisuals(_color, KeyOf(_bg));
				PageBase.Win?.ApplySkinNow();
				SyncSkinHint();
			}
		};
		_autoAccent.Checked += delegate
		{
			if (!_sync)
			{
				AutoAccent = true;
				Extras.AutoAccent = true;
				ApplySkin();
				PageBase.Win?.ApplySkinNow();
			}
		};
		_autoAccent.Unchecked += delegate
		{
			if (!_sync)
			{
				AutoAccent = false;
				Extras.AutoAccent = false;
				ApplySkin();
				PageBase.Win?.ApplySkinNow();
			}
		};
		_tint.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_tintLbl.Text = ((int)e.NewValue).ToString();
			if (!_sync)
			{
				Glass.SetTint((int)e.NewValue);
				Glass.Refresh();
			}
		};
		_flyDur.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_flyDurLbl.Text = $"{(int)e.NewValue} ms";
		};
		_threads.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_threadsLbl.Text = ((int)e.NewValue).ToString();
		};
		_segs.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			int num = (int)e.NewValue;
			_segsLbl.Text = ((num <= 1) ? I18n.L("关") : num.ToString());
		};
		_mem.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_memLbl.Text = $"{(int)e.NewValue} MB";
		};
		_lang.SelectionChanged += delegate
		{
			if (!_sync)
			{
				PageBase.Run(PreviewLanguageAsync);
			}
		};
		_aiMode.SelectionChanged += delegate
		{
			string next = KeyOf(_aiMode);
			if (!_sync && _aiEditorMode.Length > 0 && next != _aiEditorMode)
			{
				if (_aiEditorMode == "local") _aiLocalModel = AiModelValue();
				else _aiCloudModel = AiModelValue();
				_aiModel.Items.Clear();
				SetAiModel(next == "local" ? _aiLocalModel : _aiCloudModel);
			}
			_aiEditorMode = next;
			SyncAiRows();
		};
		_aiKey.Tag = "sk-…";
		_aiGateway.Tag = I18n.L("留空用内置网关");
		_aiBase.Tag = I18n.L("留空用内置网关");
		_cfReset = Ui.Btn(I18n.L("恢复内置"), BtnKind.Chip, delegate
		{
			_cfClear = true;
			_cfKey.Text = "";
			SyncSecretBoxes();
		});
		_aiKeyReset = Ui.Btn(I18n.L("恢复内置"), BtnKind.Chip, delegate
		{
			_aiKeyClear = true;
			_aiKey.Text = "";
			SyncSecretBoxes();
		});
		_cfKey.TextChanged += delegate
		{
			if (!string.IsNullOrWhiteSpace(_cfKey.Text))
			{
				_cfClear = false;
			}
		};
		_aiKey.TextChanged += delegate
		{
			if (!string.IsNullOrWhiteSpace(_aiKey.Text))
			{
				_aiKeyClear = false;
			}
		};
		App.ThemeVisualsChanged += SyncAppearanceControls;
		BuildSwatches();
		Button el = Ui.Btn(I18n.L("浏览…"), BtnKind.Chip, delegate
		{
			string text = Dlg.PickFile(I18n.L("网页 (*.html;*.htm)|*.html;*.htm|全部文件|*.*"), I18n.L("选择自定义主页"));
			if (text != null)
			{
				_homePath.Text = text;
			}
		});
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(_homePath, 0);
		grid.Add(el.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Button el2 = Ui.Btn(I18n.L("浏览…"), BtnKind.Chip, delegate
		{
			PageBase.Run(BrowseGameDirAsync, I18n.L("切换失败"));
		}, "\ue8b7");
		Grid grid2 = Ui.G(null, "*,Auto");
		grid2.Add(_gameDir, 0);
		grid2.Add(el2.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Button button = Ui.Btn(I18n.L("检查更新"), BtnKind.Chip, delegate
		{
			PageBase.Run(CheckUpdateAsync, I18n.L("检查更新失败"));
		}, "\ue72c");
		Button button2 = Ui.Btn(I18n.L("保存设置"), BtnKind.Primary, delegate
		{
			PageBase.Run(SaveAsync, I18n.L("保存失败"));
		}, "\ue74e");
		_clean = Ui.Btn(I18n.L("扫描并清理"), BtnKind.Chip, delegate
		{
			PageBase.Run(CleanAsync, I18n.L("清理失败"));
		}, "\uea99");
		_testAi = Ui.Btn(I18n.L("测试 AI 连接"), BtnKind.Chip, delegate
		{
			PageBase.Run(TestAiAsync, I18n.L("AI 连接失败"));
		}, "\ue99a");
		_aiModelFetch = Ui.Btn(I18n.L("刷新模型"), BtnKind.Chip, delegate
		{
			PageBase.Run(FetchAiModelsAsync, I18n.L("拉取模型列表失败"));
		}, "\ue72c");
		_rec = Ui.Btn(I18n.L("查看推荐"), BtnKind.Chip, delegate
		{
			PageBase.Run(RecommendAsync, I18n.L("检测失败"));
		}, "\ue945");
		FrameworkElement border = Ui.Fields(Ui.Section(I18n.L("外观"), I18n.L("主题、皮肤、动效与飞入")), Ui.Field(I18n.L("深色模式"), _dark, I18n.L("立即生效")), Ui.Field("界面字体", Ui.Btn("选择字体…", BtnKind.Chip, (_, _) => PageBase.Run(Typography.Show), "\ue8d2"), "使用本机字体，应用后立即生效"), Ui.Field(I18n.L("主题色"), _swatches, I18n.L("点击圆圈即时预览，保存后记住")), Ui.Field(I18n.L("界面背景"), _bg, I18n.L("预设色调，立即预览")), Ui.Field(I18n.L("玻璃风格"), _skin, I18n.L("Mica 随桌面色调变化；Acrylic 的背景模糊更明显。macOS 风格使用 Windows Acrylic。自定义壁纸会在应用内磨砂。")), Ui.Field(I18n.L("着色强度"), SliderRow(_tint, _tintLbl), I18n.L("0 无色、100 满着色；决定当前主题色渗进玻璃的浓度")), Ui.Field(I18n.L("跟随系统强调色"), _autoAccent, I18n.L("打开后主题色取 Windows 设置里的强调色，忽略上面的色板")), Ui.Field(I18n.L("界面动画"), _motion, I18n.L("换页过渡、进度条、悬浮等动效；关闭则全部瞬时")), Ui.Field(I18n.L("下载飞入动画"), _fly, I18n.L("点安装时图标抛物线飞入侧栏「下载任务」")), Ui.Field(I18n.L("飞入动画时长"), SliderRow(_flyDur, _flyDurLbl), I18n.L("毫秒，建议 400–800；越小越快")));
		_bgInterval.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_bgIntervalLbl.Text = I18n.L("{0} 分钟", (int)e.NewValue);
		};
		_bgBlur.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_bgBlurLbl.Text = $"{(int)e.NewValue} px";
			if (!_sync)
			{
				Wallpaper.SetEffects((int)_bgBlur.Value, (int)_bgDim.Value);
			}
		};
		_bgDim.ValueChanged += delegate(object _, RoutedPropertyChangedEventArgs<double> e)
		{
			_bgDimLbl.Text = $"{(int)e.NewValue} %";
			if (!_sync)
			{
				Wallpaper.SetEffects((int)_bgBlur.Value, (int)_bgDim.Value);
			}
		};
		Button el3 = Ui.Btn(I18n.L("选图片 / 视频…"), BtnKind.Chip, delegate
		{
			string text = Dlg.PickFile(I18n.L("壁纸 (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.mp4;*.mkv;*.webm;*.mov)") + I18n.L("|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.mp4;*.mkv;*.webm;*.mov|全部文件|*.*"), I18n.L("选择壁纸"));
			if (text != null)
			{
				_bgPath.Text = text;
				_bgFolder.Clear();
				PageBase.Run(ApplyWallpaperAsync, I18n.L("壁纸没能应用"));
			}
		}, "\ueb9f");
		Button el4 = Ui.Btn(I18n.L("选文件夹…"), BtnKind.Chip, delegate
		{
			string text = Dlg.PickFolder(I18n.L("选择壁纸文件夹"));
			if (text != null)
			{
				_bgFolder.Text = text;
				PageBase.Run(ApplyWallpaperAsync, I18n.L("壁纸没能应用"));
			}
		}, "\ue8b7");
		Button button3 = Ui.Btn(I18n.L("关闭壁纸"), BtnKind.Ghost, delegate
		{
			_bgPath.Clear();
			_bgFolder.Clear();
			PageBase.Run(ApplyWallpaperAsync, I18n.L("壁纸没能关闭"));
		}, "\ue711");
		_bgUndo = Ui.Btn(I18n.L("撤销上一次"), BtnKind.Chip, delegate
		{
			PageBase.Run(UndoWallpaperAsync, I18n.L("撤销失败"));
		}, "\ue72c");
		Button button4 = Ui.Btn(I18n.L("恢复出厂"), BtnKind.Chip, delegate
		{
			PageBase.Run(ResetWallpaperAsync, I18n.L("恢复失败"));
		}, "\uea99");
		Grid grid3 = Ui.G(null, "*,Auto");
		grid3.Add(_bgPath, 0);
		grid3.Add(el3.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		Grid grid4 = Ui.G(null, "*,Auto");
		grid4.Add(_bgFolder, 0);
		grid4.Add(el4.M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		FrameworkElement border2 = Ui.Fields(Ui.Section(I18n.L("壁纸"), I18n.L("图片或 mp4 动态壁纸；也可以直接把文件拖进窗口")), Ui.Field(I18n.L("壁纸文件"), grid3, I18n.L("留空就不用壁纸。视频会静音循环播放")), Ui.Field(I18n.L("轮播文件夹"), grid4, I18n.L("设了文件夹就按间隔换图，优先于上面那张单图")), Ui.Field(I18n.L("随机顺序"), _bgShuffle, I18n.L("关掉则按文件名顺序轮")), Ui.Field(I18n.L("轮播间隔"), SliderRow(_bgInterval, _bgIntervalLbl), I18n.L("分钟")), Ui.Field(I18n.L("模糊"), SliderRow(_bgBlur, _bgBlurLbl), I18n.L("把壁纸推远一点，文字更好读；随拖随看")), Ui.Field(I18n.L("遮罩浓度"), SliderRow(_bgDim, _bgDimLbl), I18n.L("按当前主题压淡 / 压暗壁纸；随拖随看")), Ui.Field("", Ui.H(8.0, button3, _bgUndo, button4, _bgInfo.VCenter())));
		FrameworkElement border3 = Ui.Fields(Ui.Section(I18n.L("主题包"), I18n.L("把当前配色、背景、窗口与主页设置整包存下来，随时切回")), Ui.Field(I18n.L("已保存"), _themePack, I18n.L("从这里挑一个加载或删除")), Ui.Field("", Ui.H(7.0, Ui.Btn(I18n.L("保存为主题包"), BtnKind.Soft, delegate
		{
			PageBase.Run(SaveThemeAsync, I18n.L("保存失败"));
		}, "\ue74e"), Ui.Btn(I18n.L("加载"), BtnKind.Chip, delegate
		{
			PageBase.Run(LoadThemeAsync, I18n.L("加载失败"));
		}, "\ue790"), Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(DeleteThemeAsync, I18n.L("删除失败"));
		}, "\ue74d"), Ui.Btn(I18n.L("导入"), BtnKind.Chip, delegate
		{
			PageBase.Run(ImportThemeAsync, I18n.L("导入失败"));
		}, "\ue8b5"), Ui.Btn(I18n.L("导出"), BtnKind.Chip, delegate
		{
			PageBase.Run(ExportThemeAsync, I18n.L("导出失败"));
		}, "\uede1"))), _themeInfo);
		FrameworkElement border4 = Ui.Fields(Ui.Section(I18n.L("下载"), I18n.L("线程、分片、镜像与代理")), Ui.Field(I18n.L("下载并发线程数"), SliderRow(_threads, _threadsLbl), I18n.L("同时下载的文件数量")), Ui.Field(I18n.L("单文件分片数"), SliderRow(_segs, _segsLbl), I18n.L("8MB 以上的大文件拆成几段同时下；1 为关闭")), Ui.Field(I18n.L("文件下载源"), SourcePicker(), I18n.L("网络变化后重新检测；文件缺失或校验失败时回退。")), Ui.Field(I18n.L("社区资源源"), _comm, I18n.L("模组 / 整合包：MCIM 国内镜像，挂了可改官方")), Ui.Field(I18n.L("跟随系统代理"), _proxy, I18n.L("默认开。关掉才强制直连")), Ui.Field(I18n.L("下载限速 (KB/s)"), _limit.Left(), I18n.L("0 表示不限制")), Ui.Field("CurseForge Key", SecretRow(_cfKey, _cfReset), I18n.L("留空保存不修改；不填用内置 Key")));
		FrameworkElement border5 = Ui.Fields(Ui.Section(I18n.L("游戏"), I18n.L("目录、内存、分辨率与默认实例")), Ui.Field(I18n.L("游戏目录"), grid2, I18n.L("所有实例的落脚处；切换会校验可写性")), Ui.Field(I18n.L("默认内存"), SliderRow(_mem, _memLbl), I18n.L("新实例的默认 JVM 内存")), Ui.Field("", Ui.H(8.0, _rec, Ui.Small(I18n.L("按本机内存 / CPU 给一组推荐值")).VCenter())), Ui.Field(I18n.L("默认分辨率"), Ui.H(6.0, _w, Ui.Txt("×").VCenter(), _h), I18n.L("游戏窗口的默认宽高")), Ui.Field(I18n.L("默认实例"), _inst, I18n.L("安装与启动缺省使用的实例")), Ui.Field(I18n.L("新版本默认隔离"), _iso, I18n.L("安装新版本时写入，可在版本设置里改")), Ui.Field(I18n.L("内存回收器"), _gc, I18n.L("启动时写入 JVM，版本设置可覆盖")), Ui.Field(I18n.L("默认游戏窗口"), _win, I18n.L("可被版本设置覆盖")), Ui.Field(I18n.L("默认 JVM 参数"), StretchActionRow(_jvm, Ui.Btn(I18n.L("Aikar 参数"), BtnKind.Chip, delegate { _jvm.Text = LaunchUtils.GetAikarOptimizedFlags((int)_mem.Value); }, "\ue964")), I18n.L("所有版本都会带上，可一键套用 Aikar 优化参数")), Ui.Field(I18n.L("允许多开"), _multi, I18n.L("关掉则游戏已在运行时不再启动第二个")));
		FrameworkElement border6 = Ui.Fields(Ui.Section(I18n.L("存储"), I18n.L("共享依赖与空间清理")), Ui.Field(I18n.L("共享 libraries"), _shareLib, I18n.L("所有实例共享依赖库（节省空间，降低隔离性）")), Ui.Field(I18n.L("共享 assets 资源"), _shareAssets, I18n.L("所有实例共享资源文件（节省空间，降低隔离性）")), Ui.Field(I18n.L("空间清理"), Ui.H(8.0, _clean, _cleanInfo.VCenter()), I18n.L("扫描未引用的依赖库、残留 .part 与更新缓存，确认后再删")));
		FrameworkElement border7 = Ui.Fields(Ui.Section(I18n.L("AI 助手"), I18n.L("对话里下游戏、装模组、读崩溃日志")), Ui.Field(I18n.L("接入方式"), _aiMode), Ui.Field(I18n.L("网关地址"), _aiGateway, I18n.L("公共网关模式下生效")), Ui.Field(I18n.L("端点地址"), _aiBase, I18n.L("自定义模式下的 OpenAI 兼容 base_url")), Ui.Field("API Key", SecretRow(_aiKey, _aiKeyReset), I18n.L("留空保存不修改；不填用内置令牌")), Ui.Field(I18n.L("模型"), StretchActionRow(_aiModel, _aiModelFetch), I18n.L("自动拉取可用模型；拉不到可直接手填")), Ui.Field(I18n.L("上下文窗口（token）"), _aiCtx, I18n.L("模型真实窗口未知时按 128k 保守压缩；实测后可改大")), Ui.Field(I18n.L("备用模型（可选）"), _aiFallback, I18n.L("主模型连续 429/5xx 时本回合自动切到它；留空不启用")), Ui.Field(I18n.L("权限"), Ui.Muted(I18n.L("在 AI 对话界面的输入框上方设置"))), BuildVisionField(), BuildMcpField(), Ui.Field("", _testAi.Left()));
		FrameworkElement border8 = Ui.Fields(Ui.Section(I18n.L("启动器"), I18n.L("启动后行为、主页、更新与语言")), Ui.Field(I18n.L("启动器可见性"), _vis, I18n.L("游戏启动后启动器窗口怎么处理")), Ui.Field(I18n.L("启动页主页"), _home, I18n.L("新闻、自定义 HTML 或留空")), Ui.Field(I18n.L("自定义主页"), grid, I18n.L("本地 .html 文件路径")), Ui.Field(I18n.L("自动检查更新"), _autoUpdate, I18n.L("启动时检查启动器更新")), Ui.Field(I18n.L("语言"), _lang, I18n.L("切换界面语言，重启后完全生效")), Ui.Field("", _langPreview), Ui.Field(I18n.L("启动向导"), Ui.Btn(I18n.L("重新运行"), BtnKind.Chip, delegate
		{
			PageBase.Run(FirstRunWizard.ShowAsync, I18n.L("向导没能打开"));
		}, "\ue7c1").Left(), I18n.L("目录、下载源、内存、隔离会再问一遍，最后一步还会指一遍容易错过的功能")));
        ((SPanel)border).Children.Insert(6, Ui.Field("玻璃通透度", Ui.Btn("实时调整玻璃…", BtnKind.Chip, (_, _) => Run(MaterialDialog.Show), "\ue790"), "预览透明度和壁纸磨砂；取消可恢复原来的外观。"));
        ((SPanel)border).Children.Insert(2, Ui.Field("外观方案", Ui.Btn("一键切换外观…", BtnKind.Chip, (_, _) => Run(AppearanceDialog.Show), "\ue8fd"), "一起保存透明度、磨砂与背景模式。"));
        ((SPanel)border2).Children.Add(new WallpaperCachePanel());
		((SPanel)border8).Children.Add(Ui.Field("诊断包", Ui.Btn("预览并导出诊断包", BtnKind.Chip, (_, _) => DiagnosticPanel.Show()).Left(), "选择字段并检查预览后手动保存；不包含账号、密钥或原始日志，不会自动上传。"));
		base.Content = SettingsShell(button, button2, border, border2, border3, border4, border5, border6, BuildPhoneSyncCard(), border7, BuildMemoryCard(), border8, BuildAboutCard());
	}

	/// <summary>
	/// PCL 式设置页：左栏一行一个分类，右边只铺当前分类，保存操作常驻在底部（不随内容滚走）。
	/// 顺序与调用方传进来的 sections 一一对应。
	/// </summary>
	private FrameworkElement SettingsShell(Button check, Button save, params FrameworkElement[] sections)
	{
		(string Key, string Label, string Glyph, FrameworkElement[] Sections)[] cats =
		{
			("look", I18n.L("外观"), "\ue790", new[] { sections[0], sections[1], sections[2] }),
			("download", I18n.L("下载"), "\ue896", new[] { sections[3] }),
			("game", I18n.L("游戏"), "\ue7fc", new[] { sections[4], sections[5], sections[6] }),
			("ai", I18n.L("助手"), "\ue99a", new[] { sections[7], sections[8] }),
			("launcher", I18n.L("启动器"), "\ue713", new[] { sections[9], sections[10] })
		};
		Dictionary<string, UIElement> panes = new Dictionary<string, UIElement>();
		Rail rail = new Rail();
		foreach (var cat in cats)
		{
			panes[cat.Key] = PageBase.ScrollBody(cat.Sections);
			rail.Add(cat.Key, cat.Label, cat.Glyph);
		}
		rail.Select(cats[0].Key);
		ContentControl host = new ContentControl
		{
			Content = panes[rail.Key],
			Focusable = false,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			VerticalContentAlignment = VerticalAlignment.Stretch
		};
		rail.Selected += delegate
		{
			host.Content = panes[rail.Key];
		};
		Grid bar = new PageHeader(
			_saveState.Wrap().VCenter(),
			Ui.H(8, check, save).Right().VCenter());
		bar.Margin = new Thickness(UiMetrics.PageInset, 12, UiMetrics.PageInset, 12);
        var footer = new Border { Child = bar, BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BackgroundProperty, "B.SurfaceInset");
        footer.SetResourceReference(Border.BorderBrushProperty, "B.SurfaceEdge");
		        HookSettingsChanges();
        return SettingsSearch.Wrap(new SettingsWorkspace(rail, host, footer), rail,
            cats.Select(cat => new SettingsSearch.Section { Key = cat.Key, Label = cat.Label, Controls = cat.Sections }));
	}

	private static UIElement SliderRow(Slider s, TextBlock label)
	{
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(s.VCenter(), 0);
		grid.Add(label.M(10.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
		return grid;
	}

	private static UIElement SecretRow(SecretInput box, Button reset)
	{
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(box, 0);
		reset.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		reset.VerticalAlignment = VerticalAlignment.Center;
		grid.Add(reset, 0, 1);
		return grid;
	}

	private static Grid StretchActionRow(FrameworkElement editor, Button action)
	{
		editor.Width = double.NaN;
		editor.MinWidth = 0.0;
		editor.HorizontalAlignment = HorizontalAlignment.Stretch;
		action.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
		action.VerticalAlignment = VerticalAlignment.Center;
		return Ui.G(null, "*,Auto").Add(editor, 0).Add(action, 0, 1);
	}

	private void ApplySecretState(Dictionary<string, JsonElement> s)
	{
		_cfSet = GetBool(s, "curseforge_api_key_set");
		_cfHint = GetStr(s, "curseforge_api_key_hint");
		_aiKeySet = GetBool(s, "ai_api_key_set");
		_aiKeyHint = GetStr(s, "ai_api_key_hint");
		_cfClear = (_aiKeyClear = false);
		_cfKey.Text = "";
		_aiKey.Text = "";
		SyncSecretBoxes();
	}

	private void SyncSecretBoxes()
	{
		_cfKey.Tag = (_cfClear ? I18n.L("保存后恢复为内置 Key") : (_cfSet ? string.Format(I18n.L("已保存自己的 Key（{0}），留空不修改"), _cfHint) : I18n.L("CurseForge API Key（留空用内置）")));
		_aiKey.Tag = (_aiKeyClear ? I18n.L("保存后恢复为内置令牌") : (_aiKeySet ? string.Format(I18n.L("已保存自己的令牌（{0}），留空不修改"), _aiKeyHint) : "sk-…"));
		_cfReset.Visibility = ((!_cfSet || _cfClear) ? Visibility.Collapsed : Visibility.Visible);
		_aiKeyReset.Visibility = ((!_aiKeySet || _aiKeyClear) ? Visibility.Collapsed : Visibility.Visible);
	}

	private Grid BuildVisionField()
	{
		TextBlock status = Ui.Muted(I18n.L("检测中…"));
		ComboBox mode = KeyCombo(new(string, string)[3]
		{
			("auto", I18n.L("自动检测")),
			("on", I18n.L("支持")),
			("off", I18n.L("不支持"))
		}, 120.0);
		bool syncing = true;
		async Task RefreshAsync(bool force)
		{
			status.Text = force ? I18n.L("检测中…") : status.Text;
			try
			{
				Dictionary<string, object> dictionary = ((await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>()).ToDictionary<KeyValuePair<string, JsonElement>, string, object>((KeyValuePair<string, JsonElement> kv) => kv.Key, (KeyValuePair<string, JsonElement> kv) => kv.Value);
				dictionary["ai_mode"] = AiModeWire();
				dictionary["ai_gateway_url"] = _aiGateway.Text?.Trim() ?? "";
				dictionary["ai_base_url"] = (IsBuiltinAi ? "" : (_aiBase.Text?.Trim() ?? ""));
				if (!IsBuiltinAi && !string.IsNullOrEmpty(_aiKey.Text?.Trim()))
				{
					dictionary["ai_api_key"] = _aiKey.Text.Trim();
				}
				dictionary[KeyOf(_aiMode) == "local" ? "ai_local_model" : "ai_model"] = AiModelValue();
				JsonElement r = await PageBase.Api.CallAsync<JsonElement>("ai_vision_status", new
				{
					settings = dictionary,
					force = force
				});
				if (r.TryGetProperty("mode", out var m))
				{
					syncing = true;
					SelectKey(mode, m.GetString() ?? "auto");
					syncing = false;
				}
				status.Text = r.TryGetProperty("text", out var t) ? (t.GetString() ?? "") : "";
			}
			catch (Exception ex)
			{
				status.Text = I18n.L("检测失败：") + ex.Message;
			}
		}
		SelectKey(mode, "auto");
		mode.SelectionChanged += async delegate
		{
			if (syncing)
			{
				return;
			}
			await PageBase.Api.TryCallAsync<JsonElement>("ai_vision_set_mode", new
			{
				mode = KeyOf(mode)
			});
			await RefreshAsync(force: false);
		};
		Button detect = Ui.Btn(I18n.L("重新检测"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => RefreshAsync(force: true), I18n.L("检测失败"));
		});
		syncing = false;
		base.Loaded += delegate
		{
			PageBase.Run(() => RefreshAsync(force: false));
		};
		return Ui.Field(I18n.L("识图"), Ui.V(6.0, Ui.H(8.0, mode, detect.VCenter()), status), I18n.L("自动检测：先查公开模型能力库，查不到就发张小图实测；也可以手动指定"));
	}

	/// <summary>设置 → AI 助手 → 记忆：列出 ai_memory.md 的全部条目，可编辑、删除、清空。</summary>
	private FrameworkElement BuildMemoryCard()
	{
		SPanel list = Ui.V(4.0);
		TextBlock count = Ui.Muted("");
		Button clear = null;
		void Render()
		{
			list.Children.Clear();
			List<string> entries = HelL.Core.Launcher.Ai.FileMemory.Entries();
			count.Text = (entries.Count == 0) ? I18n.L("还没有记忆。对话里说出偏好或修好问题后，助手会自己记下") : I18n.L("共 {0} 条，新对话开始时会带给助手", entries.Count);
			clear.IsEnabled = entries.Count > 0;
			for (int i = 0; i < entries.Count; i++)
			{
				int index = i;
				string entry = entries[i];
				string body = HelL.Core.Launcher.Ai.FileMemory.TextOf(entry);
				string date = (entry.Length > body.Length) ? entry.Substring(0, entry.Length - body.Length).Trim() : "";
				TextBlock text = Ui.Txt(body, 12.5, bold: false, "B.Ink").Wrap();
				Grid row = Ui.G(null, "Auto,*,Auto,Auto");
				row.Add(Ui.Txt(date, 11.5, bold: false, "B.InkFaint").M(0.0, 0.0, 10.0, 0.0).VCenter(), 0);
				row.Add(text.VCenter(), 0, 1);
				row.Add(Ui.Btn(I18n.L("编辑"), BtnKind.Chip, async delegate
				{
					string edited = await Dlg.Prompt(I18n.L("编辑记忆"), I18n.L("内容"), body);
					if (edited != null && Same(index, entry))
					{
						HelL.Core.Launcher.Ai.FileMemory.Update(index, edited);
					}
					Render();
				}).M(8.0, 0.0, 0.0, 0.0).VCenter(), 0, 2);
				row.Add(Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
				{
					if (Same(index, entry))
					{
						HelL.Core.Launcher.Ai.FileMemory.RemoveAt(index);
					}
					Render();
				}).M(4.0, 0.0, 0.0, 0.0).VCenter(), 0, 3);
				list.Children.Add(Ui.Pane(row, "B.Paper2", 8.0, 8.0));
			}
		}
		static bool Same(int index, string entry)
		{
			List<string> now = HelL.Core.Launcher.Ai.FileMemory.Entries();
			if (index < now.Count && now[index] == entry)
			{
				return true;
			}
			AppServices.Toast(I18n.L("记忆已变化"), I18n.L("文件刚被改过，已刷新列表"), ToastKind.Warning);
			return false;
		}
		clear = Ui.Btn(I18n.L("清空全部"), BtnKind.Danger, async delegate
		{
			if (await Dlg.Confirm(I18n.L("清空全部记忆？"), I18n.L("助手记下的偏好、配置和修好过的问题都会删除，无法恢复。"), I18n.L("清空"), null, danger: true))
			{
				int n = HelL.Core.Launcher.Ai.FileMemory.Clear();
				AppServices.Toast(I18n.L("已清空记忆"), I18n.L("删除了 {0} 条", n), ToastKind.Success);
			}
			Render();
		});
		Button refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Chip, delegate
		{
			Render();
		}, "\ue72c");
		Render();
		base.Loaded += delegate
		{
			Render();
		};
		return Ui.Fields(Ui.Section(I18n.L("记忆"), I18n.L("AI 助手跨对话记住的偏好和配置（数据目录 ai_memory.md，一行一条）"), Ui.H(8.0, refresh, clear)), count, list);
	}

	private Grid BuildMcpField()
	{
		TextBlock status = Ui.Muted("");
		CheckBox sw = null;
		void Refresh()
		{
			status.Text = HelL.Core.Launcher.Ai.McpServer.Running ? HelL.Core.Launcher.Ai.McpServer.Url : (string.IsNullOrEmpty(HelL.Core.Launcher.Ai.McpServer.LastError) ? I18n.L("未开启") : I18n.L("启动失败：") + HelL.Core.Launcher.Ai.McpServer.LastError);
		}
		sw = Ui.Switch(HelL.Core.Launcher.Ai.McpServer.Enabled && HelL.Core.Launcher.Ai.McpServer.Running, delegate
		{
			bool on = sw.IsChecked == true;
			bool running = HelL.Core.Launcher.Ai.McpServer.SetEnabled(on);
			if (on && !running)
			{
				sw.IsChecked = false;
			}
			Refresh();
		});
		Button copy = Ui.Btn(I18n.L("复制地址"), BtnKind.Chip, delegate
		{
			Ui.Copy(HelL.Core.Launcher.Ai.McpServer.Url, I18n.L("已复制"), I18n.L("MCP 服务地址已放进剪贴板"));
		});
		Refresh();
		return Ui.Field("MCP Server", Ui.V(6.0, Ui.H(8.0, sw, copy.VCenter()), status), I18n.L("让 Claude、Cursor 等外部 AI 直接调用启动器的工具；只监听本机"));
	}

	private void BuildSwatches()
	{
		_swatches.Children.Clear();
		(string, string)[] palette = Palette;
		for (int i = 0; i < palette.Length; i++)
		{
			(string, string) tuple = palette[i];
			string item = tuple.Item1;
			string item2 = tuple.Item2;
			bool flag = string.Equals(_color, item2, StringComparison.OrdinalIgnoreCase);
			Border border = new Border
			{
				Width = 26.0,
				Height = 26.0,
				CornerRadius = new CornerRadius(999.0),
				Padding = new Thickness(3.0),
				BorderThickness = new Thickness(flag ? 2 : 0),
				Cursor = Cursors.Hand,
				ToolTip = item
			};
			border.SetResourceReference(Border.BorderBrushProperty, "B.AccentDeep");
			border.Child = new Border
			{
				CornerRadius = new CornerRadius(10.0),
				Background = new SolidColorBrush(Parse(item2))
			};
			string pick = item2;
			border.MouseLeftButtonUp += delegate
			{
				_color = pick;
				BuildSwatches();
				ApplyVisuals(_color, KeyOf(_bg));
			};
			_swatches.Children.Add(border);
		}
	}

	private void SyncAppearanceControls()
	{
		bool sync = _sync;
		_sync = true;
		try
		{
			if (_dark.IsChecked != App.IsDark)
			{
				_dark.IsChecked = App.IsDark;
			}
			if (!string.Equals(_color, _visualColor, StringComparison.OrdinalIgnoreCase))
			{
				_color = _visualColor;
				BuildSwatches();
			}
			if (KeyOf(_bg) != _visualBackground)
			{
				SelectKey(_bg, _visualBackground);
			}
			if (KeyOf(_skin) != _visualSkin)
			{
				SelectKey(_skin, _visualSkin);
			}
			if ((int)_tint.Value != _visualTint)
			{
				_tint.Value = _visualTint;
			}
			_tintLbl.Text = _visualTint.ToString();
			bool autoAccent = AutoAccent;
			if (_autoAccent.IsChecked != autoAccent)
			{
				_autoAccent.IsChecked = autoAccent;
			}
		}
		finally
		{
			_sync = sync;
		}
	}

	private void SyncSkinHint()
	{
	}

	private void SyncAiRows()
	{
		string text = KeyOf(_aiMode);
		TextBox aiBase = _aiBase;
		SecretInput aiKey = _aiKey;
		bool flag = (_aiKeyReset.IsEnabled = text == "custom");
		bool flag3 = flag;
		flag = (aiKey.IsEnabled = flag3);
		bool isEnabled = flag;
		aiBase.IsEnabled = isEnabled;
		_aiGateway.IsEnabled = text == "public";
		_aiModel.IsEnabled = text != "local";
		_aiModelFetch.IsEnabled = text != "local";
	}

	private string AiModeWire()
	{
		if (!IsBuiltinAi)
		{
			return KeyOf(_aiMode);
		}
		return "custom";
	}

	private static string AiModeFromBackend(Dictionary<string, JsonElement> s)
	{
		string text = GetStr(s, "ai_mode", "custom").Trim().ToLowerInvariant();
		if (text == "local") return "local";
		if (text == "newapi" || text == "自定义")
		{
			text = "custom";
		}
		if (text == "custom" && string.IsNullOrWhiteSpace(GetStr(s, "ai_base_url")) && !GetBool(s, "ai_api_key_set"))
		{
			return "builtin";
		}
		if (!(text == "public"))
		{
			return "custom";
		}
		return "public";
	}

	private string AiModelValue()
	{
		string text = _aiModel.Text?.Trim();
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		return (_aiModel.SelectedItem as string) ?? "";
	}

	private void SetAiModel(string? name)
	{
		string text = name?.Trim() ?? "";
		if (text.Length != 0)
		{
			if (!_aiModel.Items.Contains(text))
			{
				_aiModel.Items.Add(text);
			}
			_aiModel.SelectedItem = text;
			_aiModel.Text = text;
		}
		else { _aiModel.SelectedIndex = -1; _aiModel.Text = ""; }
	}

	private Task FetchAiModelsAsync() => FetchAiModelsAsync(true);

	private async Task FetchAiModelsAsync(bool manual)
	{
		Dictionary<string, JsonElement> obj = (await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>();
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		foreach (KeyValuePair<string, JsonElement> item in obj)
		{
			dictionary[item.Key] = item.Value;
		}
		dictionary["ai_mode"] = AiModeWire();
		dictionary["ai_gateway_url"] = _aiGateway.Text?.Trim() ?? "";
		dictionary["ai_base_url"] = (IsBuiltinAi ? "" : (_aiBase.Text?.Trim() ?? ""));
		dictionary["ai_api_key"] = (IsBuiltinAi ? "" : (_aiKey.Text?.Trim() ?? ""));
		dictionary[KeyOf(_aiMode) == "local" ? "ai_local_model" : "ai_model"] = AiModelValue();
		AiModelList aiModelList;
		using (manual ? Dlg.Busy(I18n.L("正在拉取模型列表…")) : null)
		{
			aiModelList = await PageBase.Api.TryCallAsync<AiModelList>("ai_list_models", new
			{
				settings = dictionary
			});
		}
		if (aiModelList == null)
		{
			if (manual) PageBase.Toast(I18n.L("拉取失败"), I18n.L("没能从网关取到模型列表，可直接手填模型名"), ToastKind.Warning);
			else Smoke.Note("SettingsPage.FetchAiModelsAsync", "Model list unavailable during warm-up");
			return;
		}
		string text = AiModelValue();
		_aiModel.Items.Clear();
		foreach (string model in aiModelList.Models)
		{
			if (!string.IsNullOrWhiteSpace(model))
			{
				_aiModel.Items.Add(model);
			}
		}
		if (text.Length > 0)
		{
			SetAiModel(text);
		}
		else if (_aiModel.Items.Count > 0)
		{
			_aiModel.SelectedIndex = 0;
		}
		if (aiModelList.Ok && aiModelList.Models.Count > 0)
		{
			if (manual) PageBase.Toast(I18n.L("模型列表已刷新"), I18n.L("共 {0} 个可用模型", aiModelList.Models.Count), ToastKind.Success);
		}
		else
		{
			string reason = string.IsNullOrEmpty(aiModelList.Error) ? I18n.L("网关没返回模型列表，可直接手填模型名") : aiModelList.Error;
			if (manual) PageBase.Toast(I18n.L("没取到模型"), reason, ToastKind.Warning);
			else Smoke.Note("SettingsPage.FetchAiModelsAsync", reason);
		}
	}

	protected override async Task LoadAsync()
	{
        if (_savingSettings || DeferSettingsRefresh() || _sync) return;
		_sync = true;
        bool wasEnabled = IsEnabled;
        IsEnabled = false;
		try
		{
			Dictionary<string, JsonElement> dictionary = await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
			bool backendDark = App.IsDark;
			if (dictionary != null)
			{
				JsonElement value;
				bool flag = dictionary.TryGetValue("ui_dark", out value);
				if (flag)
				{
					flag = (int)(value.ValueKind - 5) <= 1;
				}
				if (flag)
				{
					backendDark = value.GetBoolean();
				}
				_dark.IsChecked = backendDark;
				_threads.Value = Clamp.Of(GetInt(dictionary, "download_threads", 8), 1, 64);
				_segs.Value = Clamp.Of(GetInt(dictionary, "download_segments", 4), 1, 16);
				_mem.Value = Clamp.Of(GetInt(dictionary, "default_memory_mb", 4096), 512, 32768);
				if (dictionary.TryGetValue("default_resolution", out var value2) && value2.ValueKind == JsonValueKind.Array && value2.GetArrayLength() >= 2)
				{
					_w.Text = value2[0].ToString();
					_h.Text = value2[1].ToString();
				}
				_limit.Text = GetInt(dictionary, "download_limit_kbps", 0).ToString();
				SelectKey(_src, GetStr(dictionary, "download_source", "auto"));
				SelectKey(_comm, GetStr(dictionary, "community_source", "auto"));
				_proxy.IsChecked = GetBool(dictionary, "use_system_proxy", def: true);
				SelectKey(_iso, GetStr(dictionary, "default_isolation", "none"));
				SelectKey(_gc, GetStr(dictionary, "gc_preset", "auto"));
				SelectKey(_vis, GetStr(dictionary, "launcher_visibility", "keep"));
				SelectKey(_home, GetStr(dictionary, "homepage_mode", "news"));
				SelectKey(_win, GetStr(dictionary, "window_mode", "window"));
				_homePath.Text = GetStr(dictionary, "custom_homepage");
				_jvm.Text = GetStr(dictionary, "default_jvm_args");
				_shareLib.IsChecked = GetBool(dictionary, "share_libraries");
				_shareAssets.IsChecked = GetBool(dictionary, "share_assets");
				_autoUpdate.IsChecked = GetBool(dictionary, "auto_check_update", def: true);
				ApplySecretState(dictionary);
				_aiCloudModel = GetStr(dictionary, "ai_model");
				_aiLocalModel = GetStr(dictionary, "ai_local_model");
				SelectKey(_aiMode, AiModeFromBackend(dictionary));
				SelectKey(_aiPerm, GetStr(dictionary, "ai_permission_mode", "default"));
				_aiGateway.Text = GetStr(dictionary, "ai_gateway_url");
				_aiBase.Text = GetStr(dictionary, "ai_base_url");
				SetAiModel(GetStr(dictionary, KeyOf(_aiMode) == "local" ? "ai_local_model" : "ai_model"));
				_aiCtx.Text = GetStr(dictionary, "ai_context_window", "131072");
				_aiFallback.Text = GetStr(dictionary, "ai_fallback_model");
				_aiConfirm.IsChecked = GetBool(dictionary, "ai_confirm_writes", def: true);
				_gameDir0 = GetStr(dictionary, "game_dir");
				if (string.IsNullOrEmpty(_gameDir0))
				{
					_gameDir0 = GetStr(dictionary, "root");
				}
				_gameDir.Text = _gameDir0;
				_bgPath.Text = GetStr(dictionary, "ui_background");
				_bgFolder.Text = GetStr(dictionary, "ui_background_folder");
				_bgShuffle.IsChecked = GetBool(dictionary, "ui_background_shuffle");
				_bgInterval.Value = Clamp.Of(GetInt(dictionary, "ui_background_interval", 10), 1, 120);
				_bgBlur.Value = Clamp.Of(GetInt(dictionary, "ui_background_blur", 0), 0, 40);
				_bgDim.Value = Clamp.Of(GetInt(dictionary, "ui_background_dim", 0), 0, 80);
				_bgIntervalLbl.Text = I18n.L("{0} 分钟", (int)_bgInterval.Value);
				_bgBlurLbl.Text = $"{(int)_bgBlur.Value} px";
				_bgDimLbl.Text = $"{(int)_bgDim.Value} %";
                if (dictionary.TryGetValue("ui_wpf_theme", out var appearance) && appearance.ValueKind == JsonValueKind.Object)
                {
                    ThemeExtras theme = JsonSerializer.Deserialize<ThemeExtras>(appearance.GetRawText()) ?? new ThemeExtras();
                    Extras.Skin = NormalizeSkin(theme.Skin); Extras.Tint = NormalizeTint(theme.Tint);
                    Extras.AutoAccent = theme.AutoAccent; AutoAccent = theme.AutoAccent;
                    Extras.FlyAnimation = theme.FlyAnimation; Extras.FlyDurationMs = theme.FlyDurationMs;
                    _autoAccent.IsChecked = AutoAccent;
                }
				SyncPersistedThemeColor(GetStr(dictionary, "theme_color"));
				SyncPersistedBackground(GetStr(dictionary, "ui_bg_preset"));
			}
			else
			{
				_dark.IsChecked = App.IsDark;
			}
			SyncWallpaperInfo();
			SyncAiRows();
			PageBase.Run(() => FetchAiModelsAsync(false));
			(List<InstanceInfo> rows, string instErr) = await PageBase.Api.TryCallAsyncOrError("get_instances", null, new List<InstanceInfo>());
			List<InstanceInfo> source = rows ?? new List<InstanceInfo>();
			_inst.Fill(source.Select((InstanceInfo i) => i.Name));
			if (instErr != null && source.Count == 0)
			{
				PageBase.Toast(I18n.L("读取实例失败"), instErr, ToastKind.Warning);
			}
			string text = await PageBase.Api.TryCallAsync("get_setting", new Dictionary<string, object>
			{
				["key"] = "default_instance",
				["default"] = ""
			}, "");
			if (!string.IsNullOrEmpty(text) && _inst.Items.Contains(text))
			{
				_inst.SelectedItem = text;
			}
			CheckBox multi = _multi;
			CheckBox checkBox = multi;
			checkBox.IsChecked = await PageBase.Api.TryCallAsync("allow_multi_instance", null, fallback: false);
			Dictionary<string, string> dictionary2 = await PageBase.Api.TryCallAsync<Dictionary<string, string>>("available_languages");
			if (dictionary2 != null && dictionary2.Count > 0)
			{
				_lang.Items.Clear();
				foreach (KeyValuePair<string, string> item in dictionary2)
				{
					_lang.Items.Add(new Opt
					{
						Key = item.Key,
						Label = item.Value
					});
				}
			}
			_lang0 = (await PageBase.Api.TryCallAsync("get_language", null, "")) ?? "";
			SelectKey(_lang, _lang0);
			_motion.IsChecked = Motion.UserEnabled;
			_fly.IsChecked = Extras.FlyAnimation;
			_flyDur.Value = Clamp.Of(Extras.FlyDurationMs, 200, 1200);
			_flyDurLbl.Text = $"{(int)_flyDur.Value} ms";
			_color = (IsValidColor(_visualColor) ? _visualColor : DefaultColor);
			BuildSwatches();
			_visualBackground = NormalizeBackground(Extras.Background);
			SelectKey(_bg, _visualBackground);
			_visualSkin = NormalizeSkin(Extras.Skin);
			SelectKey(_skin, _visualSkin);
			_visualTint = ((Extras.Tint < 0) ? (-1) : NormalizeTint(Extras.Tint));
			_tint.Value = ((_visualTint < 0) ? Glass.Tint : _visualTint);
			_tintLbl.Text = ((int)_tint.Value).ToString();

			if (backendDark != App.IsDark)
			{
				MainWindow win = PageBase.Win;
				if (win != null)
				{
					win.SetDark(backendDark, persistBackend: false);
				}
				else
				{
					App.ApplyTheme(backendDark);
				}
			}
			else
			{
				ApplyVisuals();
				PageBase.Win?.SyncThemeIndicator();
			}
            await ReloadThemesAsync();
            _changedSettings.Clear(); UpdateSaveState();
		}
		finally
		{
			_sync = false;
            IsEnabled = wasEnabled;
		}
	}

    private bool _savingSettings;
	private async Task SaveAsync()
	{
        if (_savingSettings || _sync) return;
        int width = FrontendValidation.Integer(_w, "分辨率宽度", 64, 16384);
        int height = FrontendValidation.Integer(_h, "分辨率高度", 64, 16384);
        int limit = FrontendValidation.Integer(_limit, "下载限速", 0, 1000000000);
        int context = FrontendValidation.Integer(_aiCtx, "AI 上下文窗口", 8192, 2000000);
        _savingSettings = true;
        bool wasEnabled = IsEnabled;
        IsEnabled = false;
        try { await SaveSettingsAsync(width, height, limit, context); }
        finally { _savingSettings = false; IsEnabled = wasEnabled; }
    }

    private async Task SaveSettingsAsync(int width, int height, int limit, int context)
    {
		string typed = _gameDir.Text?.Trim() ?? "";
        string lang = KeyOf(_lang);
        bool langChanged = !string.IsNullOrEmpty(lang) && lang != _lang0;
		bool dark = _dark.IsChecked == true;
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["ui_dark"] = dark;
		dictionary["download_threads"] = (int)_threads.Value;
		dictionary["download_segments"] = (int)_segs.Value;
		dictionary["default_memory_mb"] = (int)_mem.Value;
		dictionary["default_resolution"] = new int[2]
		{
			width,
			height
		};
		dictionary["download_source"] = KeyOf(_src);
		dictionary["community_source"] = KeyOf(_comm);
		dictionary["use_system_proxy"] = _proxy.IsChecked == true;
		dictionary["download_limit_kbps"] = limit;
		dictionary["default_isolation"] = KeyOf(_iso);
		dictionary["default_jvm_args"] = _jvm.Text?.Trim() ?? "";
		dictionary["gc_preset"] = KeyOf(_gc);
		dictionary["launcher_visibility"] = KeyOf(_vis);
		dictionary["homepage_mode"] = KeyOf(_home);
		dictionary["custom_homepage"] = _homePath.Text?.Trim() ?? "";
		dictionary["window_mode"] = KeyOf(_win);
		dictionary["auto_check_update"] = _autoUpdate.IsChecked == true;
		dictionary["share_libraries"] = _shareLib.IsChecked == true;
		dictionary["share_assets"] = _shareAssets.IsChecked == true;
		dictionary["default_instance"] = _inst.Str();
		dictionary["ai_mode"] = AiModeWire();
		dictionary["ai_gateway_url"] = _aiGateway.Text?.Trim() ?? "";
		dictionary["ai_base_url"] = (IsBuiltinAi ? "" : (_aiBase.Text?.Trim() ?? ""));
		dictionary[KeyOf(_aiMode) == "local" ? "ai_local_model" : "ai_model"] = AiModelValue();
		dictionary["ai_context_window"] = context;
		dictionary["ai_fallback_model"] = _aiFallback.Text?.Trim() ?? "";
		dictionary["ui_background"] = _bgPath.Text?.Trim() ?? "";
		dictionary["ui_background_folder"] = _bgFolder.Text?.Trim() ?? "";
		dictionary["ui_background_shuffle"] = _bgShuffle.IsChecked == true;
		dictionary["ui_background_interval"] = (int)_bgInterval.Value;
		dictionary["ui_background_blur"] = (int)_bgBlur.Value;
		dictionary["ui_background_dim"] = (int)_bgDim.Value;
		dictionary["theme_color"] = _color;
		dictionary["ui_bg_preset"] = KeyOf(_bg);
		dictionary["ui_motion"] = _motion.IsChecked == true;
		dictionary["ui_fly_animation"] = _fly.IsChecked == true;
		dictionary["ui_fly_duration_ms"] = (int)_flyDur.Value;
		Dictionary<string, object> dictionary2 = dictionary;
        if (typed.Length > 0) dictionary2["instances_dir"] = typed;
        dictionary2["allow_multi_instance"] = _multi.IsChecked == true;
        if (!string.IsNullOrEmpty(lang)) dictionary2["language"] = lang;
        dictionary2["ui_wpf_theme"] = ThemeSnapshot();
		string text = _cfKey.Text?.Trim() ?? "";
		if (text.Length > 0)
		{
			dictionary2["curseforge_api_key"] = text;
		}
		else if (_cfClear)
		{
			dictionary2["curseforge_api_key_clear"] = true;
		}
		string text2 = _aiKey.Text?.Trim() ?? "";
		if (IsBuiltinAi)
		{
			if (_aiKeySet)
			{
				dictionary2["ai_api_key_clear"] = true;
			}
		}
		else if (text2.Length > 0)
		{
			dictionary2["ai_api_key"] = text2;
		}
		else if (_aiKeyClear)
		{
			dictionary2["ai_api_key_clear"] = true;
		}
        try { await PageBase.Api.CallAsync<object>("save_settings", new { data = dictionary2 }); }
        catch (Exception error)
        {
            throw new InvalidOperationException("未收到保存确认。当前输入已保留，请重试或重新读取配置核对结果。原因：" + error.Message, error);
        }
        if (typed.Length > 0) _gameDir0 = typed;
        if (langChanged) _lang0 = lang;
        _changedSettings.Clear(); UpdateSaveState();
		Dictionary<string, JsonElement> dictionary3 = await PageBase.Api.TryCallAsync<Dictionary<string, JsonElement>>("get_settings");
		if (dictionary3 != null)
		{
			ApplySecretState(dictionary3);
		}
		Extras.ThemeColor = _color;
		Extras.Background = KeyOf(_bg);
		Extras.Skin = KeyOf(_skin);
		Extras.Tint = (int)_tint.Value;
		Extras.AutoAccent = AutoAccent;
		Extras.FlyAnimation = _fly.IsChecked == true;
		Extras.FlyDurationMs = (int)_flyDur.Value;
		ApplySkin();
		MainWindow win = PageBase.Win;
		if (win != null)
		{
			win.SetDark(dark, persistBackend: false);
		}
		else if (dark != App.IsDark)
		{
			App.ApplyTheme(dark);
		}
		Motion.Enabled = _motion.IsChecked == true;
		ApplyVisuals();
		try { await Wallpaper.ReloadAsync(); }
        catch (Exception error) { PageBase.Toast("设置已保存，背景预览未完成", error.Message, ToastKind.Warning); }
		SyncWallpaperInfo();
		PageBase.Win?.SaveUiPrefs();
		AppServices.Window?.Toast(I18n.L("设置已保存"), I18n.L("外观与动效已立即生效"), ToastKind.Success);
		if (langChanged)
		{
			await OfferLanguageRestartAsync();
		}
	}

	private async Task OfferLanguageRestartAsync()
	{
		if (!(await Dlg.Confirm(I18n.L("语言已切换"), I18n.L("界面文字要重启启动器才会全部变成新语言。是否现在重启？"), I18n.L("立即重启"), I18n.L("稍后"))))
		{
			PageBase.Toast(I18n.L("语言已切换"), I18n.L("下次启动启动器后界面才会变成新语言"));
			return;
		}
		try
		{
			string text = Assembly.GetEntryAssembly()?.Location;
			if (string.IsNullOrEmpty(text))
			{
				text = Process.GetCurrentProcess().MainModule?.FileName ?? "";
			}
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true,
				WorkingDirectory = Environment.CurrentDirectory
			});
			Application.Current.Shutdown();
		}
		catch (Exception ex)
		{
			PageBase.Toast(I18n.L("重启失败"), I18n.L("无法拉起新进程，请手动关闭后重新打开") + "\n" + ex.Message, ToastKind.Error);
		}
	}

	private async Task ApplyWallpaperAsync()
	{
		await PageBase.Api.CallAsync<object>("save_settings", new
		{
			data = new Dictionary<string, object>
			{
				["ui_wallpaper_mode"] = "local",
				["ui_background"] = _bgPath.Text?.Trim() ?? "",
				["ui_background_folder"] = _bgFolder.Text?.Trim() ?? "",
				["ui_background_shuffle"] = _bgShuffle.IsChecked == true,
				["ui_background_interval"] = (int)_bgInterval.Value,
				["ui_background_blur"] = (int)_bgBlur.Value,
				["ui_background_dim"] = (int)_bgDim.Value
			}
		});
		await Wallpaper.ReloadAsync();
		SyncWallpaperInfo();
	}

	private void SyncWallpaperInfo()
	{
		PageBase.Run(SyncWallpaperInfoAsync);
	}

	private async Task SyncWallpaperInfoAsync()
	{
		List<string> history = (await PageBase.Api.TryCallAsync("background_history", null, new List<string>())) ?? new List<string>();
		bool isEnabled = await PageBase.Api.TryCallAsync("can_undo_background", null, fallback: false);
		_bgUndo.IsEnabled = isEnabled;
		_bgUndo.ToolTip = ((history.Count == 0) ? null : I18n.L("上一张：{0}", history[history.Count - 1]));
		if (!string.IsNullOrEmpty(Wallpaper.Error))
		{
			_bgInfo.Text = Wallpaper.Error;
			return;
		}
		string text = _bgFolder.Text?.Trim() ?? "";
		string text2 = _bgPath.Text?.Trim() ?? "";
		string text3 = ((text.Length > 0) ? I18n.L("正在轮播文件夹里的图片") : ((text2.Length == 0) ? I18n.L("当前没有壁纸，界面走主题底色") : (FileKinds.IsVideo(text2) ? I18n.L("当前是动态壁纸（静音循环）") : I18n.L("当前是静态背景图"))));
		_bgInfo.Text = ((history.Count == 0) ? text3 : I18n.L("{0} · 历史 {1} 张可退", text3, history.Count));
	}

	private async Task UndoWallpaperAsync()
	{
		BackgroundState prev = await PageBase.Api.CallAsync<BackgroundState>("undo_background");
		if (prev != null)
		{
			_sync = true;
			_bgPath.Text = prev.Image;
			_bgFolder.Text = prev.Folder;
			_sync = false;
			await Wallpaper.ReloadAsync();
			await SyncWallpaperInfoAsync();
			PageBase.Toast(I18n.L("已撤销"), (string.IsNullOrEmpty(prev.Image) && string.IsNullOrEmpty(prev.Folder)) ? I18n.L("退回到没有壁纸") : ((prev.Image.Length > 0) ? prev.Image : prev.Folder), ToastKind.Success);
		}
	}

	private async Task ResetWallpaperAsync()
	{
		if (await Dlg.Confirm(I18n.L("恢复出厂壁纸"), I18n.L("会清掉当前壁纸与轮播文件夹；这一组仍会进历史，可以撤销回来。"), I18n.L("恢复"), I18n.L("取消")))
		{
			BackgroundState backgroundState = await PageBase.Api.CallAsync<BackgroundState>("reset_background");
			_sync = true;
			_bgPath.Text = backgroundState?.Image ?? "";
			_bgFolder.Text = backgroundState?.Folder ?? "";
			_sync = false;
			await Wallpaper.ReloadAsync();
			await SyncWallpaperInfoAsync();
			PageBase.Toast(I18n.L("已恢复出厂壁纸"), I18n.L("想退回去点「撤销上一次」"), ToastKind.Success);
		}
	}

	private void ApplyDark(bool dark)
	{
		MainWindow win = PageBase.Win;
		if (win != null)
		{
			win.SetDark(dark);
		}
		else if (dark != App.IsDark)
		{
			App.ApplyTheme(dark);
		}
	}

	private Task BrowseGameDirAsync()
	{
		string path = Dlg.PickFolder(I18n.L("选择游戏目录"));
		if (path != null)
		{
            _gameDir.Text = path;
		}
        return Task.CompletedTask;
	}

	private async Task CheckUpdateAsync()
	{
		UpdateInfo updateInfo;
		using (Dlg.Busy(I18n.L("正在检查更新…")))
		{
			Task<UpdateInfo> task = PageBase.Api.TryCallAsync<UpdateInfo>("check_update");
			Task completed = await Task.WhenAny(task, Task.Delay(5000));
			updateInfo = ((completed == task) ? task.Result : null);
		}
		if (updateInfo == null)
		{
			PageBase.Toast(I18n.L("检查失败"), I18n.L("更新源没有响应"), ToastKind.Warning);
			return;
		}
		if (!updateInfo.Ok)
		{
			PageBase.Toast(I18n.L("检查失败"), updateInfo.Message, ToastKind.Warning);
			return;
		}
		if (!updateInfo.HasUpdate)
		{
			PageBase.Toast(I18n.L("已是最新"), string.IsNullOrEmpty(updateInfo.Message) ? I18n.L("当前就是最新版本") : updateInfo.Message, ToastKind.Success);
			return;
		}
		string text = (updateInfo.Version + "\n" + updateInfo.Notes).Trim();
		if (await Dlg.Confirm(I18n.L("发现新版本"), text + I18n.L("\n\n现在下载更新？"), I18n.L("下载更新"), I18n.L("以后再说")))
		{
			await PageBase.Api.StartTaskAsync("start_self_update");
			PageBase.Toast(I18n.L("正在下载更新"), I18n.L("下载完会提示你重启完成更新"), ToastKind.Success);
		}
	}

	private async Task CleanAsync()
	{
		_clean.IsEnabled = false;
		_clean.Content = I18n.L("扫描中…");
		try
		{
			_cleanPreview = await PageBase.Api.CallAsync<CleanerPreview>("cleaner_preview");
		}
		finally
		{
			_clean.IsEnabled = true;
			_clean.Content = Ui.H(6.0, Ui.Glyph("\uea99", 12.5), Ui.Txt(I18n.L("扫描并清理")));
		}
		CleanerPreview cleanPreview = _cleanPreview;
		if (cleanPreview == null || cleanPreview.Count == 0)
		{
			_cleanInfo.Text = I18n.L("没有可清理的文件");
			PageBase.Toast(I18n.L("很干净"), I18n.L("没有找到可清理的文件"), ToastKind.Success);
			return;
		}
		_cleanInfo.Text = I18n.L("可清理 {0} 个文件 · {1}", cleanPreview.Count, Fmt.Size(cleanPreview.Bytes));
		CheckBox libs = Ui.Check(I18n.L("未引用依赖库（{0} 个 · {1}）", cleanPreview.UnusedLibraries.Count, Fmt.Size(cleanPreview.UnusedLibraries.Sum((CleanerEntry x) => x.Bytes))), value: true);
		CheckBox parts = Ui.Check(I18n.L("残留 .part（{0} 个 · {1}）", cleanPreview.Parts.Count, Fmt.Size(cleanPreview.Parts.Sum((CleanerEntry x) => x.Bytes))), value: true);
		CheckBox cache = Ui.Check(I18n.L("更新缓存（{0} 个 · {1}）", cleanPreview.Cache.Count, Fmt.Size(cleanPreview.Cache.Sum((CleanerEntry x) => x.Bytes))), value: true);
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("共 {0} 个文件，约 {1}。删掉的文件下次用到会自动重新下载。", cleanPreview.Count, Fmt.Size(cleanPreview.Bytes))), libs, parts, cache);
		if (!(await Dlg.Ask(I18n.L("清理文件"), body, I18n.L("开始清理"), I18n.L("取消"), danger: true)))
		{
			return;
		}
		List<string> list = new List<string>();
		if (libs.IsChecked == true)
		{
			list.Add("unused_libraries");
		}
		if (parts.IsChecked == true)
		{
			list.Add("parts");
		}
		if (cache.IsChecked == true)
		{
			list.Add("cache");
		}
		if (list.Count != 0)
		{
			CleanerResult cleanerResult;
			using (Dlg.Busy(I18n.L("正在清理…")))
			{
				cleanerResult = await PageBase.Api.CallAsync<CleanerResult>("cleaner_apply", new
				{
					kinds = list
				});
			}
			_cleanInfo.Text = I18n.L("已清理 {0} 个文件 · {1}", cleanerResult?.Removed ?? 0, Fmt.Size(cleanerResult?.Bytes ?? 0));
			PageBase.Toast(I18n.L("清理完成"), _cleanInfo.Text, ToastKind.Success);
		}
	}

	private async Task RecommendAsync()
	{
		_rec.IsEnabled = false;
		SmartRecommendation rec;
		try
		{
			using (Dlg.Busy(I18n.L("正在读取硬件信息…")))
			{
				rec = await PageBase.Api.CallAsync<SmartRecommendation>("get_smart_recommendation");
			}
		}
		finally
		{
			_rec.IsEnabled = true;
		}
		if (rec == null)
		{
			return;
		}
		var (adaptiveMin, adaptiveMax) = PerformanceOptimizerUtils.CalculateAdaptiveMemory((int)(rec.TotalRamGb * 1024), (int)(rec.TotalRamGb * 768), 0, false);
		SPanel body = Ui.V(6.0, Ui.Muted(I18n.L("本机：{0} GB 内存 / {1} 核 CPU", rec.TotalRamGb, rec.CpuCount)), Ui.Txt(I18n.L("推荐内存 {0} MB (动态区间 {1}~{2} MB)", rec.MemoryMb, adaptiveMin, adaptiveMax), 14.0, bold: true), Ui.Small(I18n.L("推荐 Java {0} · 窗口 {1}×{2} · 回收器 {3}", rec.JavaMajor, rec.WindowWidth, rec.WindowHeight, rec.GcPreset)));
		if (await Dlg.Ask(I18n.L("智能推荐"), body, I18n.L("应用推荐")))
		{
			_mem.Value = Clamp.Of(rec.MemoryMb, 512, 32768);
			if (rec.WindowWidth > 0)
			{
				_w.Text = rec.WindowWidth.ToString();
			}
			if (rec.WindowHeight > 0)
			{
				_h.Text = rec.WindowHeight.ToString();
			}
			SelectKey(_gc, string.IsNullOrEmpty(rec.GcPreset) ? "auto" : rec.GcPreset);
			PageBase.Toast(I18n.L("已应用"), I18n.L("内存已设为 {0} MB，保存设置后生效", rec.MemoryMb), ToastKind.Success);
		}
	}

	private async Task TestAiAsync()
	{
		_testAi.IsEnabled = false;
		_testAi.Content = I18n.L("测试中…");
		try
		{
			Dictionary<string, object> dictionary = ((await PageBase.Api.TryCallAsync("get_settings", null, new Dictionary<string, JsonElement>())) ?? new Dictionary<string, JsonElement>()).ToDictionary<KeyValuePair<string, JsonElement>, string, object>((KeyValuePair<string, JsonElement> kv) => kv.Key, (KeyValuePair<string, JsonElement> kv) => kv.Value);
			dictionary["ai_mode"] = AiModeWire();
			dictionary["ai_gateway_url"] = _aiGateway.Text?.Trim() ?? "";
			dictionary["ai_base_url"] = (IsBuiltinAi ? "" : (_aiBase.Text?.Trim() ?? ""));
			dictionary["ai_api_key"] = (IsBuiltinAi ? "" : (_aiKey.Text?.Trim() ?? ""));
			dictionary[KeyOf(_aiMode) == "local" ? "ai_local_model" : "ai_model"] = AiModelValue();
			string text = await PageBase.Api.CallAsync<string>("test_ai_connection", new
			{
				settings = dictionary
			});
			PageBase.Toast(I18n.L("AI 连接成功"), text ?? "", ToastKind.Success);
		}
		finally
		{
			_testAi.IsEnabled = true;
			_testAi.Content = Ui.H(6.0, Ui.Glyph("\ue99a", 12.5), Ui.Txt(I18n.L("测试 AI 连接")));
		}
	}

	private async Task PreviewLanguageAsync()
	{
		string lang = KeyOf(_lang);
		if (string.IsNullOrEmpty(lang))
		{
			_langPreview.Text = "";
			return;
		}
		string[] value = await Task.WhenAll(new string[3] { "设置", "启动游戏", "下载任务" }.Select((string k) => PageBase.Api.TryCallAsync("translate", new
		{
			key = k,
			lang = lang
		}, I18n.L(k))));
		_langPreview.Text = ((lang == _lang0) ? (I18n.L("预览：") + string.Join(" · ", value)) : (I18n.L("切换后界面会变成：") + string.Join(" · ", value) + I18n.L("（重启后全量生效）")));
	}

	private async Task ReloadThemesAsync()
	{
		List<ThemeRow> list = (await PageBase.Api.TryCallAsync("list_themes", null, new List<ThemeRow>())) ?? new List<ThemeRow>();
		string keep = _themePack.Str();
		_themePack.Fill(list.Select((ThemeRow t) => t.Name), keep);
		_themeInfo.Text = ((list.Count == 0) ? I18n.L("还没有主题包。调好配色后点「保存为主题包」。") : (I18n.L("共 {0} 个：", list.Count) + string.Join(I18n.L("、"), from t in list.Take(6)
			select I18n.L("{0}（{1} {2}）", t.Name, t.UiDark ? I18n.L("深色") : I18n.L("浅色"), t.ThemeColor))));
	}

	private async Task SaveThemeAsync()
	{
		string name = await Dlg.Prompt(I18n.L("保存主题包"), I18n.L("主题包名称"), I18n.L("我的主题"));
		if (!string.IsNullOrWhiteSpace(name))
		{
			await PageBase.Api.CallAsync<object>("save_settings", new
			{
				data = new Dictionary<string, object>
				{
					["theme_color"] = _color,
					["ui_dark"] = App.IsDark,
					["ui_bg_preset"] = KeyOf(_bg),
                    ["ui_wpf_theme"] = ThemeSnapshot()
				}
			});
			Extras.ThemeColor = _color;
			Extras.Background = KeyOf(_bg);
			await PageBase.Api.CallAsync<object>("save_theme", new
			{
				name = name.Trim()
			});
			PageBase.Toast(I18n.L("已保存"), I18n.L("主题包「{0}」已保存", name.Trim()), ToastKind.Success);
			await ReloadThemesAsync();
		}
	}

	private async Task LoadThemeAsync()
	{
        if (_savingSettings || _sync) return;
		string name = _themePack.Str();
		if (string.IsNullOrEmpty(name))
		{
			PageBase.Toast(I18n.L("没有主题包"), I18n.L("先保存一个再来加载"), ToastKind.Warning);
			return;
		}
        if (_changedSettings.Count > 0 && !await Dlg.Confirm("加载主题包", "加载「" + name + "」会重新读取设置并放弃本页未保存的更改。是否继续？", "加载并放弃草稿", "保留草稿")) return;
        _savingSettings = true;
        bool wasEnabled = IsEnabled;
        IsEnabled = false;
        try
        {
		    await PageBase.Api.CallAsync<object>("load_theme", new { name });
            _changedSettings.Clear(); UpdateSaveState();
            _savingSettings = false;
		    await LoadAsync();
		    await Wallpaper.ReloadAsync();
		    SyncWallpaperInfo();
        }
        finally { _savingSettings = false; IsEnabled = wasEnabled; }
		PageBase.Toast(I18n.L("已加载"), I18n.L("主题包「{0}」已应用", name), ToastKind.Success);
        if (await AppearanceDialog.NeedsRestart()) await AppearanceDialog.Show();
	}

	private async Task DeleteThemeAsync()
	{
		string name = _themePack.Str();
		bool flag = !string.IsNullOrEmpty(name);
		if (flag)
		{
			flag = await Dlg.Confirm(I18n.L("删除主题包"), I18n.L("删除「{0}」？", name), I18n.L("删除"), I18n.L("取消"), danger: true);
		}
		if (flag)
		{
			await PageBase.Api.CallAsync<object>("delete_theme", new { name });
			PageBase.Toast(I18n.L("已删除"), name, ToastKind.Success);
			await ReloadThemesAsync();
		}
	}

	private async Task ImportThemeAsync()
	{
		string path = Dlg.PickFile(I18n.L("主题包 (*.json)|*.json|全部文件|*.*"), I18n.L("导入主题包"));
		if (path != null)
		{
			string text = await PageBase.Api.CallAsync<string>("import_theme", new { path });
			PageBase.Toast(I18n.L("已导入"), string.IsNullOrEmpty(text) ? path : I18n.L("主题包「{0}」", text), ToastKind.Success);
			await ReloadThemesAsync();
		}
	}

	private async Task ExportThemeAsync()
	{
		string text = _themePack.Str();
		if (string.IsNullOrEmpty(text))
		{
			PageBase.Toast(I18n.L("没有主题包"), I18n.L("先保存一个再来导出"), ToastKind.Warning);
			return;
		}
		string dest = Dlg.SaveFile(I18n.L("主题包 (*.json)|*.json"), text + ".json", I18n.L("导出主题包"));
		if (dest != null)
		{
			string text2 = await PageBase.Api.CallAsync<string>("export_theme", new
			{
				name = text,
				dest = dest
			});
			PageBase.Toast(I18n.L("已导出"), text2 ?? dest, ToastKind.Success);
		}
	}

	internal static void ApplyVisuals()
	{
		ApplyVisuals(_visualColor, _visualBackground);
	}

	/// <summary>把当前皮肤和着色强度推给 Glass，再重算玻璃画刷。主题 / 深浅色 / 皮肤变化时都要跑。</summary>
	internal static void ApplySkin()
	{
		Glass.Use(_visualSkin);
		Glass.SetTint((_visualTint < 0) ? (-1) : _visualTint);
		ApplyVisuals(_visualColor, _visualBackground);
	}

	private static void ApplyVisuals(string color, string bg)
	{
		if (AutoAccent)
		{
			color = Accent.Hex();
		}
		color = NormalizeColor(color) ?? DefaultColor;
		bg = NormalizeBackground(bg);
		_visualColor = color;
		_visualBackground = bg;
		try
		{
			ResourceDictionary paletteDictionary = App.PaletteDictionary;
			if (paletteDictionary == null)
			{
				return;
			}
			bool isDark = App.IsDark;
			ResourceDictionary resourceDictionary = new ResourceDictionary
			{
				Source = App.PaletteUri(isDark)
			};
			Color paper = ((resourceDictionary["C.Paper"] is Color color2) ? color2 : (isDark ? Color.FromRgb(30, 30, 30) : Colors.White));
			if (string.Equals(color, DefaultColor, StringComparison.OrdinalIgnoreCase))
			{
				string[] accentKeys = AccentKeys;
				foreach (string key in accentKeys)
				{
					if (resourceDictionary.Contains(key))
					{
						paletteDictionary[key] = resourceDictionary[key];
					}
				}
			}
			else
			{
				ApplyAccent(paletteDictionary, Opaque(Parse(color)), paper, isDark);
			}
			(string, string[]) tuple;
			switch (bg)
			{
			case "default":
			{
				string[] accentKeys = BackgroundKeys;
				foreach (string key2 in accentKeys)
				{
					if (resourceDictionary.Contains(key2))
					{
						paletteDictionary[key2] = resourceDictionary[key2];
					}
				}
				goto end_IL_0025;
			}
			case "eye":
				tuple = (isDark ? ("#121A15", new string[3] { "#141D17", "#16211A", "#121A15" }) : ("#EBF4EC", new string[3] { "#EFF7F0", "#EAF4EB", "#EDF6EE" }));
				break;
			case "warm":
				tuple = (isDark ? ("#1B1813", new string[3] { "#1D1A15", "#201C16", "#1B1813" }) : ("#F7F3EA", new string[3] { "#F9F5ED", "#F6F2E8", "#F8F4EC" }));
				break;
			default:
				tuple = (isDark ? ("#14181D", new string[3] { "#161B21", "#181E25", "#14181D" }) : ("#EDF1F6", new string[3] { "#F0F4F9", "#EBF0F6", "#EEF2F8" }));
				break;
			}
			(string, string[]) tuple2 = tuple;
			string item = tuple2.Item1;
			string[] item2 = tuple2.Item2;
			Color color3 = Parse(item);
			paletteDictionary["C.Canvas"] = color3;
			paletteDictionary["B.Canvas"] = new SolidColorBrush(color3);
			LinearGradientBrush linearGradientBrush = new LinearGradientBrush
			{
				StartPoint = new Point(0.0, 0.0),
				EndPoint = new Point(0.9, 1.0)
			};
			linearGradientBrush.GradientStops.Add(new GradientStop(Parse(item2[0]), 0.0));
			linearGradientBrush.GradientStops.Add(new GradientStop(Parse(item2[1]), 0.55));
			linearGradientBrush.GradientStops.Add(new GradientStop(Parse(item2[2]), 1.0));
			paletteDictionary["B.PageWash"] = linearGradientBrush;
			end_IL_0025:;
			if (!string.Equals(_visualSkin, Glass.Key, StringComparison.Ordinal) || _visualTint >= 0)
			{
				Glass.Use(_visualSkin);
				Glass.SetTint(_visualTint);
			}
			if (AutoAccent)
			{
				ApplyAccent(paletteDictionary, Accent.Rgb, paper, isDark);
			}
			Glass.Refresh();
		}
		catch (Exception)
		{
		}
		App.RaiseThemeVisualsChanged();
	}

	private static void ApplyAccent(ResourceDictionary d, Color accent, Color paper, bool dark)
	{
		Color color = EnsureWhiteSurface(accent, 4.8);
		Color color2 = EnsureTextContrast(accent, paper, dark, 4.8);
		Color color3 = EnsureTextContrast(accent, paper, dark, 1.6);
		Color color4 = Mix(color3, Colors.White, dark ? 0.28 : 0.3);
		Color color5 = Mix(paper, color3, dark ? 0.16 : 0.105);
		Color color6 = Mix(paper, color3, dark ? 0.24 : 0.165);
		d["C.Accent"] = color3;
		d["C.AccentStrong"] = color;
		d["C.AccentText"] = color2;
		d["C.AccentDeep"] = color2;
		d["C.AccentLite"] = color4;
		d["B.Accent"] = new SolidColorBrush(color3);
		d["B.AccentStrong"] = new SolidColorBrush(color);
		d["B.OnAccentStrong"] = Brushes.White;
		d["B.AccentText"] = new SolidColorBrush(color2);
		d["B.AccentTint"] = new SolidColorBrush(Mix(paper, color3, dark ? 0.1 : 0.07));
		d["B.AccentDeep"] = new SolidColorBrush(color2);
		d["B.AccentLite"] = new SolidColorBrush(color4);
		d["B.AccentSoft"] = new SolidColorBrush(color5);
		d["B.AccentSoft2"] = new SolidColorBrush(color6);
		d["B.OnAccent"] = Brushes.White;
		Color color7 = Mix(paper, color3, dark ? 0.1 : 0.07);
		Color color8 = Mix(paper, color3, dark ? 0.07 : 0.045);
		Color color9 = Mix(paper, color3, dark ? 0.26 : 0.2);
		d["B.Glass"] = new SolidColorBrush(color7)
		{
			Opacity = (dark ? 0.72 : 0.76)
		};
		d["B.GlassStrong"] = new SolidColorBrush(color8)
		{
			Opacity = (dark ? 0.86 : 0.88)
		};
		d["B.GlassBorder"] = new SolidColorBrush(color9)
		{
			Opacity = (dark ? 0.62 : 0.68)
		};
		Color color10 = (dark ? Color.FromRgb(63, 176, 124) : Color.FromRgb(46, 155, 107));
		d["B.Ok"] = new SolidColorBrush(color10);
		d["B.OnOk"] = new SolidColorBrush(ContrastForeground(color10));
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(1.0, 1.0)
		};
		linearGradientBrush.GradientStops.Add(new GradientStop(Mix(color, Colors.White, 0.06), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(color, 0.52));
		linearGradientBrush.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, 0.18), 1.0));
		d["B.BannerFill"] = linearGradientBrush;
		d["B.OnBanner"] = Brushes.White;
		LinearGradientBrush linearGradientBrush2 = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(1.0, 1.0)
		};
		linearGradientBrush2.GradientStops.Add(new GradientStop(Mix(color, Colors.White, 0.08), 0.0));
		linearGradientBrush2.GradientStops.Add(new GradientStop(Mix(color, Colors.Black, 0.16), 1.0));
		d["B.LogoFill"] = linearGradientBrush2;
		d["B.OnLogo"] = Brushes.White;
	}

	private static Color Parse(string hex)
	{
		return (Color)(ColorConverter.ConvertFromString(hex) ?? ((object)Colors.SeaGreen));
	}

	private static Color Opaque(Color c)
	{
		return Color.FromRgb(c.R, c.G, c.B);
	}

	private static string NormalizeBackground(string? bg)
	{
		return bg switch
		{
			"eye" => "eye", 
			"warm" => "warm", 
			"mist" => "mist", 
			_ => "default", 
		};
	}

	private static string NormalizeSkin(string? skin)
	{
		return (skin == GlassSkin.MacOS) ? GlassSkin.MacOS : GlassSkin.Win11;
	}

	private static int NormalizeTint(int tint)
	{
		if (tint < 0)
		{
			return 0;
		}
		if (tint > 100)
		{
			return 100;
		}
		return tint;
	}

	private static Color EnsureWhiteSurface(Color color, double minRatio)
	{
		if (ContrastRatio(color, Colors.White) >= minRatio)
		{
			return color;
		}
		for (int i = 1; i <= 20; i++)
		{
			Color color2 = Mix(color, Colors.Black, (double)i * 0.035);
			if (ContrastRatio(color2, Colors.White) >= minRatio)
			{
				return color2;
			}
		}
		return Mix(color, Colors.Black, 0.7);
	}

	private static Color EnsureTextContrast(Color color, Color paper, bool dark, double minRatio)
	{
		if (ContrastRatio(color, paper) >= minRatio)
		{
			return color;
		}
		Color color2 = (dark ? Colors.White : Colors.Black);
		for (int i = 1; i <= 20; i++)
		{
			Color color3 = Mix(color, color2, (double)i * 0.035);
			if (ContrastRatio(color3, paper) >= minRatio)
			{
				return color3;
			}
		}
		return color2;
	}

	private static Color ContrastForeground(Color bg)
	{
		Color black = Colors.Black;
		Color white = Colors.White;
		if (!(ContrastRatio(bg, white) >= ContrastRatio(bg, black)))
		{
			return black;
		}
		return white;
	}

	private static double ContrastRatio(Color a, Color b)
	{
		double val = RelativeLuminance(a);
		double val2 = RelativeLuminance(b);
		double num = Math.Max(val, val2);
		double num2 = Math.Min(val, val2);
		return (num + 0.05) / (num2 + 0.05);
	}

	private static double RelativeLuminance(Color c)
	{
		return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
		static double Ch(byte v)
		{
			double num = (double)(int)v / 255.0;
			if (!(num <= 0.04045))
			{
				return Math.Pow((num + 0.055) / 1.055, 2.4);
			}
			return num / 12.92;
		}
	}

	internal static string? NormalizeColor(string? hex)
	{
		string text = hex?.Trim() ?? "";
		if (text.Length == 9 && text[0] == '#' && text.Substring(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase))
		{
			text = "#" + text.Substring(3);
		}
		if (text.Length != 7 || text[0] != '#')
		{
			return null;
		}
		for (int i = 1; i < 7; i++)
		{
			if (!Uri.IsHexDigit(text[i]))
			{
				return null;
			}
		}
		return text.ToUpperInvariant();
	}

	private static bool IsValidColor(string? hex)
	{
		return NormalizeColor(hex) != null;
	}

	private static Color Mix(Color a, Color b, double t)
	{
		return Color.FromRgb((byte)((double)(int)a.R + (double)(b.R - a.R) * t), (byte)((double)(int)a.G + (double)(b.G - a.G) * t), (byte)((double)(int)a.B + (double)(b.B - a.B) * t));
	}

	private static bool GetBool(Dictionary<string, JsonElement> s, string k, bool def = false)
	{
		JsonElement value;
		bool flag = s.TryGetValue(k, out value);
		if (flag)
		{
			flag = (int)(value.ValueKind - 5) <= 1;
		}
		if (!flag)
		{
			return def;
		}
		return value.GetBoolean();
	}

	private static int GetInt(Dictionary<string, JsonElement> s, string k, int def)
	{
		if (!s.TryGetValue(k, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
		{
			return def;
		}
		return value2;
	}

	private static string GetStr(Dictionary<string, JsonElement> s, string k, string def = "")
	{
		if (!s.TryGetValue(k, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return def;
		}
		return value.GetString() ?? def;
	}

	private static int Int(string? s, int fallback)
	{
		if (!int.TryParse(s?.Trim(), out var result) || result < 0)
		{
			return fallback;
		}
		return result;
	}

	private static FrameworkElement BuildAboutCard()
	{
		TextBlock thanks = Ui.Muted("@368****168 \u2014 " + I18n.L("帮忙测试了正版（微软）验证"));
		thanks.TextWrapping = TextWrapping.Wrap;
		return Ui.Fields(Ui.Section(I18n.L("关于"), I18n.L("HelL 启动器")), Ui.Field(I18n.L("版本"), Ui.Muted(HelL.Core.Launcher.AppInfo.APP_VERSION)), Ui.Field(I18n.L("致谢"), thanks, I18n.L("感谢每一位帮忙测试的朋友")));
	}
}
