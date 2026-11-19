using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public static class CrashUi
{
	public static async Task<bool> ShowAsync(CrashReport report, bool gameError = false)
	{
		BridgeClient api = AppServices.Client;
		string head = (string.IsNullOrWhiteSpace(report.Headline) ? report.Title : report.Headline);
		if (string.IsNullOrWhiteSpace(head))
		{
			head = I18n.L("游戏异常退出");
		}
		SPanel sPanel = Ui.V(12.0);
		sPanel.MinWidth = 520.0;
		sPanel.Children.Add(Ui.Txt(head, 15.0, bold: true).Wrap());
		if (!string.IsNullOrWhiteSpace(report.Summary))
		{
			sPanel.Children.Add(Ui.Muted(report.Summary));
		}
		int? exitCode = report.ExitCode;
		if (exitCode.HasValue)
		{
			int valueOrDefault = exitCode.GetValueOrDefault();
			string text = (string.IsNullOrWhiteSpace(report.ExitHint) ? "" : (" · " + report.ExitHint));
			sPanel.Children.Add(Ui.Small(I18n.L("退出码 {0}{1}", valueOrDefault, text)));
		}
		if (!string.IsNullOrWhiteSpace(report.Help))
		{
			sPanel.Children.Add(Ui.Muted(report.Help));
		}
		var localDiagnosis = CrashLogExpert.Analyze(report.Detail, report.ExitCode);
		if (localDiagnosis.IsIdentified)
		{
			Border diagCard = Ui.Card(Ui.V(4.0,
				Ui.H(6.0, Ui.Glyph("\ue7ba", 16.0, "B.AccentDeep"), Ui.Txt(I18n.L("智能诊断"), 13.0, bold: true, "B.AccentDeep")),
				Ui.Txt(localDiagnosis.Reason, 12.5).Wrap(),
				Ui.Muted(localDiagnosis.Recommendation).Wrap()
			), 12.0);
			diagCard.Margin = new Thickness(0.0, 4.0, 0.0, 4.0);
			sPanel.Children.Add(diagCard);
		}
		if (!string.IsNullOrWhiteSpace(report.Detail))
		{
			TextBox element = new TextBox
			{
				Style = Ui.S("Input.Log"),
				Text = report.Detail,
				Height = 190.0,
				TextWrapping = TextWrapping.Wrap
			};
			sPanel.Children.Add(element);
		}
		WrapPanel actionRow = new WrapPanel();
		sPanel.Children.Add(actionRow);
		foreach (CrashAction act2 in report.Actions ?? new List<CrashAction>())
		{
			Button b = Ui.Btn(act2.Label, BtnKind.Soft);
			b.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
			b.Click += delegate
			{
				PageBase.Run(async delegate
				{
					b.IsEnabled = false;
					try
					{
						OpResult opResult = await api.CallAsync<OpResult>("apply_crash_action", new
						{
							action = act2,
							report = report
						});
						AppServices.Toast((opResult != null && opResult.Ok) ? I18n.L("已处理") : I18n.L("没能处理"), opResult?.Message ?? "", (opResult != null && opResult.Ok) ? ToastKind.Success : ToastKind.Warning);
					}
					finally
					{
						b.IsEnabled = true;
					}
				}, I18n.L("处理失败"));
			};
			actionRow.Children.Add(b);
		}
		if (gameError)
		{
			Button button = Ui.Btn(I18n.L("交给 AI 修复"), BtnKind.Primary, null, "\ue99a");
			button.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
			button.ToolTip = I18n.L("把这份报错直接交给 AI 助手诊断和修复");
			button.Click += delegate
			{
				new AiFixWindow(report).Show();
			};
			actionRow.Children.Add(button);
		}
		Extra(I18n.L("打开日志文件"), "\ue8a7", async delegate
		{
			string text2 = await api.CallAsync<string>("open_crash_file", new
			{
				task_id = report.TaskId,
				path = report.DirectFile
			});
			AppServices.Toast(I18n.L("已打开"), text2 ?? "", ToastKind.Success);
		});
		Extra(I18n.L("导出报告"), "\uede1", async delegate
		{
			string dest = Dlg.SaveFile(I18n.L("文本 (*.txt)|*.txt"), "tipcl-crash.txt", I18n.L("导出崩溃报告"));
			if (dest != null)
			{
				string text2 = await api.CallAsync<string>("export_crash_report", new
				{
					task_id = report.TaskId,
					dest = dest
				});
				AppServices.Toast(I18n.L("已导出"), text2 ?? dest, ToastKind.Success);
			}
		});
		Extra(I18n.L("上传给开发者"), "\ue724", async delegate
		{
			string text2 = await api.CallAsync<string>("submit_crash_report", new
			{
				task_id = report.TaskId
			});
			AppServices.Toast(I18n.L("已上传"), text2 ?? "", ToastKind.Success);
		});
		Extra(I18n.L("附说明反馈"), "\ue8bd", async delegate
		{
			TextBox note = Ui.Multi(I18n.L("崩溃前在做什么？装了什么模组？（可空）"), "", 110.0);
			if (await Dlg.Ask(I18n.L("反馈这次崩溃"), Ui.V(8.0, Ui.Muted(I18n.L("会把崩溃报告连同下面这段说明发给开发者，不含账号密码。")), note), I18n.L("发送")))
			{
				OpResult opResult = await api.CallAsync<OpResult>("submit_crash_feedback", new
				{
					report = report,
					extra = (note.Text?.Trim() ?? "")
				});
				AppServices.Toast((opResult != null && opResult.Ok) ? I18n.L("已反馈") : I18n.L("反馈失败"), opResult?.Message ?? "", (opResult != null && opResult.Ok) ? ToastKind.Success : ToastKind.Error);
			}
		});
		Extra(I18n.L("复制全文"), "\ue8c8", delegate
		{
			string raw = head + "\n" + report.Summary + "\n\n" + report.Detail;
			Ui.Copy(PrivacyMasker.Mask(raw));
			return Task.CompletedTask;
		});
		return await Dlg.Choose(I18n.L("启动出问题了"), sPanel, new string[2]
		{
			I18n.L("关闭"),
			I18n.L("重新启动")
		}, 760.0) == 1;
		void Extra(string text2, string glyph, Func<Task> work)
		{
			Button button2 = Ui.Btn(text2, BtnKind.Chip, null, glyph);
			button2.Margin = new Thickness(0.0, 0.0, 7.0, 7.0);
			button2.Click += delegate
			{
				PageBase.Run(work, I18n.L("失败"));
			};
			actionRow.Children.Add(button2);
		}
	}
}
