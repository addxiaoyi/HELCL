using System;
using System.Collections.Generic;
using System.Windows.Threading;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class AccountPage : PageBase
{
	private readonly SPanel _list = Ui.V(10.0);

	private readonly TextBlock _count = Ui.Muted("");
	private readonly SPanel _loginActions;

	private readonly ThumbTile _heroAvatar = new ThumbTile("?", 96.0, 14.0);

	private readonly Image _heroBody = new Image
	{
		Stretch = Stretch.Uniform,
		Height = 190.0
	};

	private byte[]? _currentSkinBytes;
	private bool _currentSkinSlim;
	private bool _isSkinBackView;
	private readonly Button _skinAngleBtn = Ui.Btn(AccountAndJavaUtils.GetSkinAngleLabel(false), BtnKind.Chip);

	private readonly TextBlock _heroName = Ui.Txt(I18n.L("未登录"), 15.0, bold: true).Trim();

	private readonly TextBlock _heroKind = Ui.Small("");

	private readonly Border _hero;

	private string? _loginTask;

	private string? _authlibTask;

	private string? _nide8Task;

	private Dlg.Layer? _loginLayer;

	private TextBlock? _loginHint;

	private TextBlock? _loginCode;

	private string _loginUri = "";

	public override string Title => I18n.L("账号");

	public AccountPage()
	{
		Button button = Ui.Btn(I18n.L("添加离线账号"), BtnKind.Primary, null, "\ue710");
		button.Click += delegate
		{
			PageBase.RunOnce(button, AddOfflineAsync, I18n.L("添加失败"));
		};
		Button button2 = Ui.Btn(I18n.L("微软登录"), BtnKind.Soft, null, "\ue77b");
		button2.Click += delegate
		{
			PageBase.RunOnce(button2, MicrosoftLoginAsync, I18n.L("微软登录失败"));
		};
		Button button3 = Ui.Btn(I18n.L("皮肤站登录"), BtnKind.Chip, null, "\uea18");
		button3.Click += delegate
		{
			PageBase.RunOnce(button3, AuthlibLoginAsync, I18n.L("皮肤站登录失败"));
		};
		Button button4 = Ui.Btn(I18n.L("统一通行证"), BtnKind.Chip, null, "\ue71b");
		button4.Click += delegate
		{
			PageBase.RunOnce(button4, Nide8LoginAsync, I18n.L("统一通行证登录失败"));
		};
		Button button5 = Ui.IconBtn("\ue72c", I18n.L("刷新"), delegate
		{
			PageBase.Run(LoadAsync);
		});
		_loginActions = Ui.H(8, button4, button3, button2, button);
		_loginActions.Wrap = true;
		Grid grid = Ui.Section(I18n.L("账号"), I18n.L("启动游戏时使用标记为「当前」的账号"), Ui.H(8.0, _count.VCenter(), button5));
		Grid grid2 = Ui.G(null, "Auto,*");
		grid2.Add(_heroAvatar, 0);
		grid2.Add(Ui.V(2.0, _heroName, _heroKind).VCenter().M(10.0, 0.0, 0.0, 0.0), 0, 1);
		_skinAngleBtn.Visibility = Visibility.Collapsed;
		_skinAngleBtn.Click += delegate { ToggleSkinAngle(); };
		_heroBody.Cursor = System.Windows.Input.Cursors.Hand;
		_heroBody.MouseLeftButtonUp += delegate { ToggleSkinAngle(); };
		_hero = Ui.Card(Ui.V(10.0, _heroBody, _skinAngleBtn, grid2));
		_hero.Width = 240.0;
		_heroBody.Visibility = Visibility.Collapsed;
		RenderOptions.SetBitmapScalingMode(_heroBody, BitmapScalingMode.NearestNeighbor);
		base.Content = PageBase.ScrollBody(grid, Ui.V(16, _loginActions, BuildAccountFilters(), new AdaptiveColumns(_hero, _list, 240, 780, fixedFirst: true, gap: 14)));
	}

	protected override async Task LoadAsync()
	{
		(List<AccountRow> rows, string err) = await PageBase.Api.TryCallAsyncOrError("get_account_rows", null, new List<AccountRow>());
		List<AccountRow> list = rows ?? new List<AccountRow>();
        _accountRows = list;
		_list.Children.Clear();
		_count.Text = I18n.L("共 {0} 个", list.Count);
		_loginActions.Visibility = list.Count > 0 || err != null ? Visibility.Visible : Visibility.Collapsed;
		if (list.Count == 0)
		{
			_hero.Visibility = Visibility.Collapsed;
			if (err != null)
			{
				_list.Children.Add(Ui.EmptyError(I18n.L("读取账号失败"), err, delegate
				{
					PageBase.Run(LoadAsync);
				}));
			}
			else
			{
				_list.Children.Add(BuildWelcome());
			}
			await RenderHeroAsync(null);
			return;
		}
		_hero.Visibility = Visibility.Visible;
        _accountRows = list;
        RenderAccountRows();
		Motion.Stagger(_list, 22.0, 200.0, 10.0);
		await RenderHeroAsync(list.FirstOrDefault((AccountRow r) => r.Active) ?? list[0]);
	}

	private UIElement BuildWelcome()
	{
		Button Login(string label, BtnKind kind, System.Func<Task> action)
		{
			Button button = Ui.Btn(label, kind);
			button.Click += (_, _) => PageBase.RunOnce(button, action, I18n.L("添加账号失败"));
			return button;
		}
		SPanel primary = Ui.H(10,
			Login(I18n.L("微软账号登录"), BtnKind.Primary, MicrosoftLoginAsync),
			Login(I18n.L("添加离线账号"), BtnKind.Normal, AddOfflineAsync));
		primary.Wrap = true;
		SPanel other = Ui.H(10,
			Login(I18n.L("皮肤站登录"), BtnKind.Ghost, AuthlibLoginAsync),
			Login(I18n.L("统一通行证"), BtnKind.Ghost, Nide8LoginAsync));
		other.Wrap = true;
		SPanel content = Ui.V(20, Ui.Glyph("\ue77b", 32, "B.AccentText"),
			Ui.V(8, Ui.H1(I18n.L("选择你的游戏账号")), Ui.Muted(I18n.L("登录微软账号，或创建离线角色。添加后可随时切换。"))),
			primary, Ui.Sep(), Ui.V(6, Ui.Small(I18n.L("其他登录方式")), other));
		Border welcome = new Border { Child = content, Padding = new Thickness(16, 24, 16, 24) };
		welcome.MaxWidth = 560;
		welcome.HorizontalAlignment = HorizontalAlignment.Center;
		welcome.Margin = new Thickness(0, 38, 0, 24);
		return welcome;
	}

	private void ToggleSkinAngle()
	{
		if (_currentSkinBytes == null) return;
		_isSkinBackView = !_isSkinBackView;
		_heroBody.Source = _isSkinBackView ? SkinPng.BackView(_currentSkinBytes, _currentSkinSlim) : SkinPng.FrontView(_currentSkinBytes, _currentSkinSlim);
		_skinAngleBtn.Content = AccountAndJavaUtils.GetSkinAngleLabel(_isSkinBackView);
	}

	private async Task RenderHeroAsync(AccountRow? acc)
	{
		_currentSkinBytes = null;
		_isSkinBackView = false;
		_skinAngleBtn.Visibility = Visibility.Collapsed;
		_heroBody.Source = null;
		_heroBody.Visibility = Visibility.Collapsed;
		if (acc == null)
		{
			_heroName.Text = I18n.L("未登录");
			_heroKind.Text = I18n.L("先添加一个账号");
			_heroAvatar.SetUrl(null);
			return;
		}
		_heroName.Text = acc.Name;
		_heroKind.Text = TypeLabel(acc);
		if (!string.IsNullOrEmpty(acc.SkinFile))
		{
			AccountSkin accountSkin = await PageBase.Api.TryCallAsync<AccountSkin>("get_account_skin", new
			{
				name = acc.Name
			});
			byte[] array = SkinPng.Decode(accountSkin?.DataUrl);
			if (array != null)
			{
				_currentSkinBytes = array;
				_currentSkinSlim = accountSkin.Slim;
				_heroBody.Source = SkinPng.FrontView(array, accountSkin.Slim);
				_heroBody.Visibility = Visibility.Visible;
				_skinAngleBtn.Content = AccountAndJavaUtils.GetSkinAngleLabel(false);
				_skinAngleBtn.Visibility = Visibility.Visible;
				_heroAvatar.SetUrl(null);
				return;
			}
		}
		SkinUrls skinUrls = await PageBase.Api.TryCallAsync<SkinUrls>("skin_urls", new
		{
			account_name = acc.Name
		});
		_heroAvatar.SetUrl(skinUrls?.Avatar ?? acc.Avatar);
		string text = skinUrls?.Body ?? acc.Body;
		if (!string.IsNullOrWhiteSpace(text))
		{
			BitmapSource bitmapSource = await Thumbs.LoadAsync(text, 190);
			if (bitmapSource != null)
			{
				_heroBody.Source = bitmapSource;
				_heroBody.Visibility = Visibility.Visible;
			}
		}
	}

	private static string TypeLabel(AccountRow acc)
	{
		return acc.Type switch
		{
			"microsoft" => I18n.L("微软"), 
			"offline" => I18n.L("离线"), 
			"authlib" => I18n.L("皮肤站"), 
			"nide8" => I18n.L("统一通行证"), 
			_ => (!string.IsNullOrEmpty(acc.Api)) ? I18n.L("皮肤站") : (string.IsNullOrEmpty(acc.Type) ? I18n.L("其他") : acc.Type), 
		};
	}

	private UIElement Row(AccountRow acc)
	{
		ThumbTile thumbTile = new ThumbTile(acc.Name, 40.0, 20.0);
		thumbTile.SetUrl(string.IsNullOrEmpty(acc.SkinFile) ? acc.Avatar : null);
		string text = TypeLabel(acc);
		SPanel sPanel = Ui.H(7.0, Ui.Txt(acc.Name, 13.5, bold: true).Trim().VCenter(), acc.Active ? Ui.Tag(I18n.L("当前"), "B.OnAccentStrong", "B.AccentStrong") : null, Ui.Tag(text), string.IsNullOrEmpty(acc.SkinFile) ? null : Ui.Tag(I18n.L("自定义皮肤"), "B.OnOk", "B.Ok"));
		string text2 = (string.IsNullOrEmpty(acc.Uuid) ? (text + I18n.L("账号")) : ("UUID " + acc.Uuid));

		SPanel sPanel2 = Ui.H(6.0, Ui.Btn(I18n.L("皮肤"), BtnKind.Chip, delegate
		{
			PageBase.Run(() => EditSkinAsync(acc), I18n.L("皮肤没能保存"));
		}, "\ueb9f"));
		if (!acc.Active)
		{
			sPanel2.Children.Add(Ui.Btn(I18n.L("设为当前"), BtnKind.Soft, delegate
			{
				PageBase.Run(() => SetActiveAsync(acc.Name), I18n.L("切换失败"));
			}, "\ue73e"));
		}
		sPanel2.Children.Add(Ui.Btn(I18n.L("删除"), BtnKind.Ghost, delegate
		{
			PageBase.Run(() => DeleteAsync(acc), I18n.L("删除失败"));
		}));
        sPanel2.Wrap = true;
        if (!string.IsNullOrWhiteSpace(acc.Uuid)) sPanel2.Children.Add(Ui.Btn("复制 UUID", BtnKind.Ghost, (_, _) => Ui.Copy(acc.Uuid, "已复制", "账号 UUID 已复制")));
        sPanel.Wrap = true;
        Grid grid = Ui.G(null, "Auto,*");
        grid.Add(thumbTile.M(0, 0, 12).VTop(), 0);
        grid.Add(new PageHeader(Ui.V(3, sPanel, Ui.Small(text2).Wrap()), sPanel2) { MinimumHeadingWidth = 170 }, 0, 1);
		Border border = Ui.RowCard(grid);
		if (!acc.Active)
		{
			border.ToolTip = I18n.L("双击设为当前账号");
			border.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
			{
				if (e.ClickCount == 2 && !Ui.FromButton(e.OriginalSource as DependencyObject))
				{
					e.Handled = true;
					PageBase.Run(() => SetActiveAsync(acc.Name), I18n.L("切换失败"));
				}
			};
		}
		return border;
	}

	private async Task SetActiveAsync(string name)
	{
		await PageBase.Api.CallAsync<object>("set_active_account", new { name });
		PageBase.Toast(I18n.L("已切换"), I18n.L("当前账号：{0}", name), ToastKind.Success);
		await LoadAsync();
	}

	private async Task DeleteAsync(AccountRow acc)
	{
		string body = (acc.Type == "microsoft") ? I18n.L("将删除「{0}」。微软账号的刷新令牌也会一并丢失，下次需要重新走设备码登录。", acc.Name) : (string.IsNullOrEmpty(acc.Api) ? I18n.L("将删除本地角色「{0}」，游戏内存档不受影响。", acc.Name) : I18n.L("将删除「{0}」。下次使用时需要重新登录该皮肤站。", acc.Name));
		if (await Dlg.Confirm(I18n.L("删除账号"), body, I18n.L("删除"), I18n.L("取消"), danger: true))
		{
			await PageBase.Api.CallAsync<object>("remove_account", new
			{
				name = acc.Name
			});
			PageBase.Toast(I18n.L("已删除"), acc.Name, ToastKind.Success);
			await LoadAsync();
		}
	}

	private async Task AddOfflineAsync()
	{
		TextBox name = Ui.Input(I18n.L("例如 Steve"));
		ComboBox skin = Ui.Combo(new string[3]
		{
			I18n.L("默认"),
			"Steve",
			"Alex"
		});
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("离线模式，无正版验证")), Ui.Field(I18n.L("角色名"), name, labelWidth: 78), Ui.Field(I18n.L("默认皮肤"), skin, null, 78.0));
		bool added = await Dlg.EditAsync(I18n.L("添加离线账号"), body, async () =>
		{
			Dlg.Require(!string.IsNullOrWhiteSpace(name.Text), name, I18n.L("请填写离线角色名"));
			BridgeClient api = PageBase.Api;
			string username = name.Text.Trim();
			string text = skin.Str();
			string skin2 = ((text == "Steve") ? "steve" : ((!(text == "Alex")) ? "default" : "alex"));
			await api.CallAsync<object>("add_offline_account", new
			{
				username = username,
				skin = skin2
			});
		}, I18n.L("添加"));
		if (added)
		{
			PageBase.Toast(I18n.L("已添加"), name.Text.Trim(), ToastKind.Success);
			await LoadAsync();
		}
	}

	private async Task EditSkinAsync(AccountRow acc)
	{
		string text = await EditSkinCoreAsync(acc, "");
		if (!string.IsNullOrEmpty(text))
		{
			PageBase.Toast(I18n.L("皮肤已更新"), text, ToastKind.Success);
		}
		await LoadAsync();
	}

	internal static async Task<string> EditSkinExternalAsync(AccountRow acc, string presetPath)
	{
		string message = await EditSkinCoreAsync(acc, presetPath);
		if (AppServices.Window?.GetPage("account") is AccountPage accountPage)
		{
			await accountPage.RefreshAsync();
		}
		return message;
	}

	private static async Task<string> EditSkinCoreAsync(AccountRow acc, string presetPath)
	{
		BridgeClient Api = AppServices.Client;
		AccountSkin current = (await Api.TryCallAsync<AccountSkin>("get_account_skin", new
		{
			name = acc.Name
		})) ?? new AccountSkin();
		string picked = presetPath ?? "";
		bool clear = false;
		Image preview = new Image
		{
			Height = 200.0,
			Stretch = Stretch.Uniform
		};
		RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
		TextBlock previewHint = Ui.Muted(I18n.L("还没设皮肤")).Center();
		TextBlock fileLabel = Ui.Small(string.IsNullOrEmpty(current.SkinFile) ? I18n.L("游戏默认皮肤") : current.SkinFile).Center();
		ComboBox model = Ui.Combo(new string[2]
		{
			I18n.L("宽臂（Steve）"),
			I18n.L("细臂（Alex）")
		}, current.Slim ? I18n.L("细臂（Alex）") : I18n.L("宽臂（Steve）"));
		Button clearBtn = Ui.Btn(I18n.L("清除，用游戏默认"), BtnKind.Chip, null, "\ue74d");
		clearBtn.IsEnabled = !string.IsNullOrEmpty(current.SkinFile) || picked.Length > 0;
		if (picked.Length > 0)
		{
			fileLabel.Text = picked;
		}
		Button button = Ui.Btn(I18n.L("选择 PNG"), BtnKind.Primary, delegate
		{
			string text2 = Dlg.PickFile(I18n.L("PNG 图片 (*.png)|*.png"), I18n.L("选择皮肤 PNG"));
			if (text2 != null)
			{
				picked = text2;
				clear = false;
				clearBtn.IsEnabled = true;
				fileLabel.Text = text2;
				Refresh();
			}
		}, "\ueb9f");
		clearBtn.Click += delegate
		{
			picked = "";
			clear = true;
			clearBtn.IsEnabled = false;
			fileLabel.Text = I18n.L("保存后清除，恢复游戏默认皮肤");
			Refresh();
		};
		model.SelectionChanged += delegate
		{
			Refresh();
		};
		Refresh();
		Border border = Ui.Pane(new Grid
		{
			Children = 
			{
				(UIElement)previewHint,
				(UIElement)preview
			}
		}, "B.Paper2", 10.0, 10.0);
		border.Width = 150.0;
		SPanel body = Ui.V(10.0, Ui.Muted(I18n.L("64x64 或 64x32 的 PNG。进游戏后由启动器自带的本地皮肤服务发给游戏，不需要联网。")).Wrap(), border.Center(), fileLabel, Ui.H(8.0, button, clearBtn), Ui.Field(I18n.L("手臂模型"), model, null, 78.0));
		if (!(await Dlg.Ask(I18n.L("「{0}」的皮肤", acc.Name), body, I18n.L("保存"), I18n.L("取消"), danger: false, 460.0)))
		{
			return "";
		}
		if (clear)
		{
			await Api.CallAsync<object>("set_account_skin", new
			{
				name = acc.Name
			});
			return I18n.L("已清除，回到游戏默认皮肤");
		}
		string text = picked;
		if (text.Length == 0 && current.SkinFile.Length == 0)
		{
			return "";
		}
		await Api.CallAsync<object>("set_account_skin", new
		{
			name = acc.Name,
			path = text,
			model = ((model.SelectedIndex == 1) ? "slim" : "classic"),
			data = ((text.Length == 0) ? current.DataUrl : "")
		});
		return I18n.L("下次启动游戏时生效");
		void Refresh()
		{
			if (clear)
			{
				preview.Source = null;
				previewHint.Text = I18n.L("保存后清除，恢复游戏默认皮肤");
				previewHint.Visibility = Visibility.Visible;
			}
			else
			{
				byte[] array = SourcePng();
				BitmapSource bitmapSource = ((array == null) ? null : SkinPng.FrontView(array, model.SelectedIndex == 1));
				preview.Source = bitmapSource;
				previewHint.Visibility = ((bitmapSource != null) ? Visibility.Collapsed : Visibility.Visible);
				previewHint.Text = ((array == null) ? I18n.L("还没设皮肤") : I18n.L("这张图读不出来"));
			}
		}
		byte[]? SourcePng()
		{
			if (picked.Length > 0)
			{
				try
				{
					return File.ReadAllBytes(picked);
				}
				catch
				{
					return null;
				}
			}
			return SkinPng.Decode(current.DataUrl);
		}
	}

	private async Task MicrosoftLoginAsync()
	{
		if (_loginLayer != null)
		{
			return;
		}
		TextBlock textBlock = Ui.Muted(I18n.L("正在获取登录代码…"));
		textBlock.TextWrapping = TextWrapping.Wrap;
		TextBlock textBlock2 = new TextBlock
		{
			FontSize = 26.0,
			FontWeight = FontWeights.Bold,
			Text = "------"
		};
		textBlock2.SetResourceReference(TextBlock.ForegroundProperty, "B.AccentText");
		_loginHint = textBlock;
		_loginCode = textBlock2;
		_loginUri = "";
		Button button = Ui.Btn(I18n.L("复制代码"), BtnKind.Chip, delegate
		{
			TextBlock loginCode = _loginCode;
			if (loginCode != null)
			{
				Ui.Copy(loginCode.Text, I18n.L("已复制"), I18n.L("代码已放进剪贴板"));
			}
		}, "\ue8c8");
		Button button2 = Ui.Btn(I18n.L("打开浏览器"), BtnKind.Primary, delegate
		{
			if (!string.IsNullOrEmpty(_loginUri))
			{
				Ui.OpenUrl(_loginUri);
			}
		}, "\ue71b");
		        _copyLoginCode = button; _openLoginBrowser = button2;
        button.IsEnabled = button2.IsEnabled = false;
        _loginExpiry.Text = "有效期将在获取验证码后显示。";
        _renewLoginCode = Ui.Btn("重新获取验证码", BtnKind.Chip, (_, _) => PageBase.Run(RestartMicrosoftLogin));
        _renewLoginCode.Visibility = Visibility.Collapsed;
        SPanel body = Ui.V(10, textBlock, textBlock2, _loginExpiry, Ui.H(8, button, button2, _renewLoginCode));
		_loginLayer = Dlg.Panel(I18n.L("微软账号登录"), body, 460.0, delegate
		{
			_loginLayer = null;
            _loginClock.Stop();
			if (_loginTask != null)
			{
				PageBase.Fire(() => PageBase.Api.TryCallAsync<object>("cancel_task", new
				{
					task_id = _loginTask
				}));
			}
		}, dismissable: false);
		_loginTask = await PageBase.Api.StartTaskAsync("start_microsoft_login");
	}

	private async Task AuthlibLoginAsync()
	{
		List<AuthlibPreset> presets = (await PageBase.Api.TryCallAsync("authlib_presets", null, new List<AuthlibPreset>())) ?? new List<AuthlibPreset>();
		TextBox api = Ui.Input("https://littleskin.cn/api/yggdrasil", presets.FirstOrDefault()?.Api ?? "");
		TextBox user = Ui.Input(I18n.L("邮箱 / 用户名"));
		PasswordBox pw = Ui.Pw();
		ComboBox pick = Ui.Combo(presets.Select((AuthlibPreset p) => p.Name));
		pick.SelectionChanged += delegate
		{
			AuthlibPreset authlibPreset = presets.FirstOrDefault((AuthlibPreset p) => p.Name == pick.Str());
			if (authlibPreset != null)
			{
				api.Text = authlibPreset.Api;
			}
		};
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("选择皮肤站，或直接填 Yggdrasil API 地址")), pick, api, user, pw);
        await Dlg.EditAsync(I18n.L("皮肤站登录"), body, async () =>
        {
            Dlg.Require(!string.IsNullOrWhiteSpace(api.Text), api, "请输入皮肤站 API 地址");
            Dlg.Require(!string.IsNullOrWhiteSpace(user.Text), user, "请输入邮箱或用户名");
            Dlg.Require(!string.IsNullOrWhiteSpace(pw.Password), pw, "请输入密码");
            string cleanApi;
            try { cleanApi = AuthlibUrlParser.NormalizeAuthlibUrl(api.Text); }
            catch (Exception) { Dlg.Require(false, api, "API 地址无效，请检查皮肤站地址。"); throw; }
            _authlibTask = await PageBase.Api.StartTaskAsync("start_authlib_login", new { api = cleanApi, username = user.Text.Trim(), password = pw.Password });
            PageBase.Toast("正在登录", "皮肤站验证中，结果会显示在任务与账号列表中。");
        }, I18n.L("登录"));
	}

	private async Task Nide8LoginAsync()
	{
		TextBox sid = Ui.Input(I18n.L("服务器 ID / 链接"));
		TextBox user = Ui.Input(I18n.L("用户名"));
		PasswordBox pw = Ui.Pw();
		SPanel body = Ui.V(8.0, Ui.Muted(I18n.L("填 32 位服务器 ID，或把含该 ID 的链接贴进来")), sid, user, pw);
        await Dlg.EditAsync(I18n.L("统一通行证登录"), body, async () =>
        {
            Dlg.Require(!string.IsNullOrWhiteSpace(sid.Text), sid, "请输入统一通行证服务器 ID 或可识别链接");
            Dlg.Require(!string.IsNullOrWhiteSpace(user.Text), user, "请输入用户名");
            Dlg.Require(!string.IsNullOrWhiteSpace(pw.Password), pw, "请输入密码");
            _nide8Task = await PageBase.Api.StartTaskAsync("start_nide8_login", new { server_id = sid.Text.Trim(), username = user.Text.Trim(), password = pw.Password });
            PageBase.Toast("正在登录", "统一通行证验证中，结果会显示在任务与账号列表中。");
        }, I18n.L("登录"));
	}

	public override void OnEvent(BridgeEvent ev)
	{
		switch (ev.Event)
		{
		case "login_code":
			if (_loginLayer != null)
			{
				_loginUri = ev.Uri;
                SetLoginExpiry(ev.ExpiresIn);
                if (_copyLoginCode != null) _copyLoginCode.IsEnabled = !string.IsNullOrWhiteSpace(ev.Code);
                if (_openLoginBrowser != null) _openLoginBrowser.IsEnabled = System.Uri.TryCreate(ev.Uri, UriKind.Absolute, out var loginAddress) && loginAddress.Scheme == "https";
				if (_loginCode != null)
				{
					_loginCode.Text = ev.Code;
				}
				if (_loginHint != null)
				{
					_loginHint.Text = I18n.L("在浏览器打开下面的地址并输入代码：\n") + ev.Uri;
				}
			}
			break;
		case "login_status":
			if (_loginHint != null)
			{
				_loginHint.Text = ev.Text;
			}
			break;
		case "finished":
			if (ev.TaskId == _loginTask)
			{
				_loginTask = null;
                _loginClock.Stop();
				if (ev.Success)
				{
					_loginLayer?.Close();
					_loginLayer = null;
					PageBase.Toast(I18n.L("登录成功"), I18n.L("微软账号已加入列表"), ToastKind.Success);
					PageBase.Run(LoadAsync);
				}
				else if (_loginHint != null)
				{
					_loginHint.Text = ev.Message;
                    if (_renewLoginCode != null) _renewLoginCode.Visibility = Visibility.Visible;
				}
			}
			else if (ev.TaskId == _authlibTask)
			{
				_authlibTask = null;
				if (ev.Success)
				{
					PageBase.Toast(I18n.L("登录成功"), I18n.L("皮肤站账号已加入列表"), ToastKind.Success);
					PageBase.Run(LoadAsync);
				}
				else
				{
					PageBase.Toast(I18n.L("登录失败"), ev.Message, ToastKind.Error);
				}
			}
			else if (ev.TaskId == _nide8Task)
			{
				_nide8Task = null;
				if (ev.Success)
				{
					PageBase.Toast(I18n.L("登录成功"), I18n.L("统一通行证账号已加入列表"), ToastKind.Success);
					PageBase.Run(LoadAsync);
				}
				else
				{
					PageBase.Toast(I18n.L("登录失败"), ev.Message, ToastKind.Error);
				}
			}
			break;
		}
	}
}
