using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

internal sealed class AskCard : Border
{
	private readonly List<AskBlock> _blocks = new List<AskBlock>();

	public AskCard(string title, JsonElement questions, Action<object?> submit)
	{
		AskCard askCard = this;
		SPanel body = Ui.V(10.0);
		if (!string.IsNullOrWhiteSpace(title))
		{
			body.Children.Add(Ui.Txt(title, 13.0, bold: true).Wrap());
		}
		foreach (JsonElement item in questions.EnumerateArray())
		{
			AskBlock askBlock = new AskBlock(item);
			_blocks.Add(askBlock);
			body.Children.Add(askBlock.Root);
		}
		Button ok = Ui.Btn(I18n.L("确定"), BtnKind.Primary, null, "\ue73e");
		Button skip = Ui.Btn(I18n.L("跳过"));
		body.Children.Add(Ui.H(8.0, ok, skip));
		ok.Click += delegate
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>();
			foreach (AskBlock block in askCard._blocks)
			{
				object obj = block.Collect();
				if (obj == null)
				{
					AppServices.Toast(I18n.L("还没选"), block.Prompt, ToastKind.Warning);
					return;
				}
				dictionary[block.Id] = obj;
			}
			Freeze(I18n.L("已提交"));
			submit(dictionary);
		};
		skip.Click += delegate
		{
			Freeze(I18n.L("已跳过"));
			submit(null);
		};
		base.CornerRadius = new CornerRadius(10.0);
		base.Padding = new Thickness(12.0, 10.0, 12.0, 11.0);
		base.BorderThickness = new Thickness(1.0);
		base.MaxWidth = 760.0;
		base.HorizontalAlignment = HorizontalAlignment.Left;
		Child = body;
		SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		SetResourceReference(Border.BorderBrushProperty, "B.Accent");
		void Freeze(string note)
		{
			Button button = ok;
			bool flag = (skip.IsEnabled = false);
			bool isEnabled = flag;
			button.IsEnabled = isEnabled;
			foreach (AskBlock block2 in askCard._blocks)
			{
				block2.Root.IsEnabled = false;
			}
			body.Children.Add(Ui.Small(note));
		}
	}
}
