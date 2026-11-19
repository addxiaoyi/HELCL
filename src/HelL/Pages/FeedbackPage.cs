using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class FeedbackPage : PageBase
{
	private static readonly string[] Categories = new string[4]
	{
		I18n.L("问题反馈"),
		I18n.L("功能建议"),
		I18n.L("崩溃报告"),
		I18n.L("其它")
	};

	private readonly ComboBox _cat = Ui.Combo(Categories);

	private readonly TextBox _title = Ui.Input(I18n.L("一句话说清问题"));

	private readonly TextBox _body = Ui.Multi(I18n.L("详细描述：怎么触发的、期望是什么"), "", 130.0);

	private readonly TextBox _contact = Ui.Input(I18n.L("联系方式（可空）"));

	private readonly CheckBox _sys = Ui.Check(I18n.L("附带系统信息（显卡 / 内存 / Java）"), value: true);

	private readonly SPanel _history = Ui.V(6.0);

	private readonly SPanel _articles = Ui.V(6.0);

	private readonly SPanel _attachList = Ui.V(4.0);

	private readonly TextBox _search = Ui.Input(I18n.L("搜索帮助文章"));

	private readonly List<string> _attachments = new List<string>();

	public override string Title => I18n.L("反馈与帮助");

	public FeedbackPage()
	{
		Button button = Ui.Btn(I18n.L("提交反馈"), BtnKind.Primary, null, "\ue724");
		button.Click += delegate
		{
			PageBase.RunOnce(button, SubmitAsync, I18n.L("提交失败"));
		};
		Button button2 = Ui.Btn(I18n.L("查看系统信息"), BtnKind.Chip, delegate
		{
			PageBase.Run(SysInfoAsync);
		}, "\ue946");
		Button button3 = Ui.Btn(I18n.L("添加附件"), BtnKind.Chip, delegate
		{
			PageBase.Run(AttachAsync, I18n.L("附件没能上传"));
		}, "\ue8b5");
		FrameworkElement border = Ui.Fields(Ui.Section(I18n.L("提交反馈"), I18n.L("会发到开发者的反馈服务，不含账号密码")), Ui.Field(I18n.L("类型"), _cat, null, 74.0), Ui.Field(I18n.L("标题"), _title, null, 74.0), _body, Ui.Field(I18n.L("联系方式"), _contact, null, 74.0), _sys, _attachList, Ui.H(8.0, button, button3, button2));
		FrameworkElement border2 = Ui.Fields(Ui.Section(I18n.L("我提交过的")), _history);
		_search.TextChanged += delegate
		{
			PageBase.Run(() => LoadArticlesAsync(_search.Text));
		};
		FrameworkElement el = Ui.Fields(Ui.Section(I18n.L("常见问题"), I18n.L("启动、Java、模组、账号、联机的快速说明（点击标题展开）")), _search, _articles);
		Grid grid = new AdaptiveColumns(Ui.V(14, border, border2), el.VTop(), 340, 900);
		base.Content = PageBase.ScrollBody(Ui.Section(I18n.L("反馈与帮助"), I18n.L("遇到问题先看帮助，解决不了再提反馈")), grid);
	}

	protected override async Task LoadAsync()
	{
		await LoadHistoryAsync();
		await LoadArticlesAsync("");
	}

	private async Task LoadHistoryAsync()
	{
		List<FeedbackRow> list = (await PageBase.Api.TryCallAsync("feedback_history", null, new List<FeedbackRow>())) ?? new List<FeedbackRow>();
		_history.Children.Clear();
		if (list.Count == 0)
		{
			_history.Children.Add(Ui.Muted(I18n.L("还没有提交记录")));
			return;
		}
		foreach (FeedbackRow item in list.Take(20))
		{
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.V(2.0, Ui.Txt(item.Title, 12.5).Trim(), Ui.Small(DisplayLabels.Of(item.Category) + " · " + item.Time)).VCenter(), 0);
			grid.Add(Ui.Tag(string.IsNullOrWhiteSpace(item.Status) ? I18n.L("已提交") : DisplayLabels.Of(item.Status)).VCenter(), 0, 1);
			_history.Children.Add(grid);
		}
	}

	private async Task LoadArticlesAsync(string query)
	{
		List<HelpArticle> list = (await PageBase.Api.TryCallAsync("help_articles", new
		{
			query = (query ?? "")
		}, new List<HelpArticle>())) ?? new List<HelpArticle>();
		_articles.Children.Clear();
		if (list.Count == 0)
		{
			_articles.Children.Add(Ui.Muted(I18n.L("没有匹配的文章")));
			return;
		}
		foreach (HelpArticle item in list.Take(30))
		{
			HelpArticle article = item;
			TextBlock body = Ui.Muted("").Wrap().M(8.0, 0.0, 8.0, 4.0);
			body.Visibility = Visibility.Collapsed;
			Button button = Ui.Btn(item.Title, BtnKind.Ghost, delegate
			{
				PageBase.Run(() => ToggleArticleAsync(article, body));
			});
			button.HorizontalContentAlignment = HorizontalAlignment.Left;
			button.Padding = new Thickness(8.0, 6.0, 8.0, 7.0);
			_articles.Children.Add(Ui.V(2.0, button.Stretch(), body));
		}
		Motion.Stagger(_articles, 14.0, 180.0, 6.0);
	}

	private async Task ToggleArticleAsync(HelpArticle a, TextBlock body)
	{
		if (body.Visibility == Visibility.Visible)
		{
			body.Visibility = Visibility.Collapsed;
			return;
		}
		if (string.IsNullOrEmpty(a.Body))
		{
			HelpArticle helpArticle = await PageBase.Api.TryCallAsync<HelpArticle>("help_article", new
			{
				article_id = a.Id
			});
			if (helpArticle != null)
			{
				a.Body = helpArticle.Body;
			}
		}
		body.Text = (string.IsNullOrEmpty(a.Body) ? I18n.L("暂无内容") : a.Body);
		body.Visibility = Visibility.Visible;
	}

	private async Task SysInfoAsync()
	{
		using (Dlg.Busy(I18n.L("正在收集系统信息…")))
		{
			object info = await PageBase.Api.TryCallAsync<object>("collect_sysinfo", new
			{
				force = false,
				scan_system_java = true
			});
			string body = (await PageBase.Api.TryCallAsync("sysinfo_text", new { info }, "")) ?? "";
			await Dlg.Alert(I18n.L("系统信息"), body, 640.0);
		}
	}

	private async Task AttachAsync()
	{
		string[] array = Dlg.PickFiles(I18n.L("截图 / 日志 (*.png;*.jpg;*.log;*.txt)|*.png;*.jpg;*.log;*.txt|全部文件|*.*"), I18n.L("添加附件"));
		if (array == null || array.Length == 0)
		{
			return;
		}
		using (Dlg.Busy(I18n.L("正在上传附件…")))
		{
			string[] array2 = array;
			string[] array3 = array2;
			foreach (string f in array3)
			{
				string name = Path.GetFileName(f);
				if (File.Exists(f) && new FileInfo(f).Length > 25 * 1024 * 1024)
				{
					PageBase.Toast(I18n.L("文件过大"), I18n.L("{0} 超过 25MB，请压缩后再添加", name), ToastKind.Warning);
					continue;
				}
				string text;
				try
				{
					text = await Task.Run(() => Convert.ToBase64String(File.ReadAllBytes(f)));
				}
				catch (Exception ex)
				{
					PageBase.Toast(I18n.L("读不了这个文件"), I18n.L("{0}：{1}", name, ex.Message), ToastKind.Warning);
					continue;
				}
				if (text.Length == 0)
				{
					PageBase.Toast(I18n.L("跳过空文件"), name, ToastKind.Warning);
					continue;
				}
				string text2 = await PageBase.Api.CallAsync<string>("stash_upload", new
				{
					name = name,
					data = text
				});
				if (!string.IsNullOrEmpty(text2))
				{
					_attachments.Add(text2);
				}
			}
		}
		RenderAttachments();
	}

	private void RenderAttachments()
	{
		_attachList.Children.Clear();
		if (_attachments.Count == 0)
		{
			return;
		}
		_attachList.Children.Add(Ui.Small(I18n.L("已附 {0} 个文件", _attachments.Count)));
		foreach (string attachment in _attachments)
		{
			string path = attachment;
			Grid grid = Ui.G(null, "*,Auto");
			grid.Add(Ui.Small(Path.GetFileName(path)).Trim().VCenter(), 0);
			grid.Add(Ui.Btn(I18n.L("移除"), BtnKind.Ghost, delegate
			{
				_attachments.Remove(path);
				RenderAttachments();
			}).VCenter(), 0, 1);
			_attachList.Children.Add(grid);
		}
	}

	private async Task SubmitAsync()
	{
		if (string.IsNullOrWhiteSpace(_title.Text))
		{
			PageBase.Toast(I18n.L("标题不能为空"), "", ToastKind.Warning);
			Motion.Shake(_title);
			return;
		}
		string text = PrivacyMasker.Mask(_body.Text ?? "");
		if (_attachments.Count > 0)
		{
			text = text.TrimEnd() + I18n.L("\n\n附件：\n") + string.Join("\n", _attachments);
		}
		using (Dlg.Busy(I18n.L("正在提交…")))
		{
			OpResult opResult = await PageBase.Api.TryCallAsync<OpResult>("submit_feedback", new
			{
				category = _cat.Str(),
				title = _title.Text.Trim(),
				body = text,
				contact = (_contact.Text?.Trim() ?? ""),
				include_sysinfo = (_sys.IsChecked == true)
			});
			PageBase.Toast((opResult != null && opResult.Ok) ? I18n.L("已提交") : I18n.L("提交失败"), opResult?.Message ?? "", (opResult != null && opResult.Ok) ? ToastKind.Success : ToastKind.Error);
			if (opResult != null && opResult.Ok)
			{
				_title.Clear();
				_body.Clear();
				_attachments.Clear();
				RenderAttachments();
				await LoadHistoryAsync();
			}
		}
	}
}
