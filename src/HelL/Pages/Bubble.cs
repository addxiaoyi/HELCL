using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HelL.Pages.Ai;
using HelL.Services;

namespace HelL.Pages;

internal sealed class Bubble : Border
{
	private readonly MdView? _md;

	private readonly TextBlock? _plainBody;

	private readonly SPanel _thinking;

	private readonly TextBlock _thinkingLabel;

	private readonly ThinkFold? _think;

	private readonly bool _mine;

	private readonly bool _error;

	private readonly SPanel _actions;

	private readonly Button? _rewindBtn;

	private readonly Button? _retryBtn;

	private readonly Button? _editBtn;

	private readonly Border? _userBody;

	private readonly SPanel? _editor;

	private readonly TextBox? _editBox;

	/// <summary>发给后端的原文（不含"附图"等显示用后缀），编辑后重发时用来在对话里定位。</summary>
	internal string OrigText { get; set; }

	/// <summary>回复中插队发的消息，撤回逻辑不把它当一轮。</summary>
	internal bool IsSteer { get; set; }

	internal bool Editing => _editor != null && _editor.Visibility == Visibility.Visible;

	private string _plain;

	private string _answer;

	private bool _live;

	internal ThinkFold? Think => _think;

	internal string Answer => _answer;

	internal string BodyText
	{
		get
		{
			if (_md == null)
			{
				return _plainBody?.Text ?? "";
			}
			return _md.Markdown;
		}
	}

	internal bool BodyShown => ((FrameworkElement)(((object)_md) ?? ((object)_plainBody))).Visibility == Visibility.Visible;

	internal bool IsMine => _mine;

	internal Action? Rewind
	{
		set
		{
			if (_rewindBtn != null)
			{
				_rewindBtn.Tag = value;
				_rewindBtn.Visibility = ((value == null) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	/// <summary>设置后显示「编辑」按钮；确认编辑时用新内容回调。</summary>
	internal Action<Bubble, string>? Edit
	{
		set
		{
			if (_editBtn != null)
			{
				_editBtn.Tag = value;
				_editBtn.Visibility = ((value == null) ? Visibility.Collapsed : Visibility.Visible);
				if (value == null)
				{
					CancelEdit();
				}
			}
		}
	}

	internal Action? Retry
	{
		set
		{
			if (_retryBtn != null)
			{
				_retryBtn.Tag = value;
				_retryBtn.Visibility = ((value == null) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	internal static (List<string> Parts, string Answer) SplitThink(string? text)
	{
		List<string> list = new List<string>();
		StringBuilder stringBuilder = new StringBuilder();
		string text2 = text ?? "";
		while (true)
		{
			int num = text2.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
			if (num < 0)
			{
				break;
			}
			stringBuilder.Append(text2, 0, num);
			int num2 = text2.IndexOf("</think>", num + 7, StringComparison.OrdinalIgnoreCase);
			if (num2 < 0)
			{
				list.Add(text2.Substring(num + 7));
				text2 = "";
				break;
			}
			list.Add(text2.Substring(num + 7, num2 - num - 7));
			text2 = text2.Substring(num2 + 8);
		}
		stringBuilder.Append(text2);
		return (Parts: list, Answer: stringBuilder.ToString().Trim());
	}

	private static bool ThinkOpen(string text)
	{
		return Count(text, "<think>") > Count(text, "</think>");
		static int Count(string s, string tag)
		{
			int num = 0;
			int startIndex = 0;
			while ((startIndex = s.IndexOf(tag, startIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
			{
				num++;
				startIndex += tag.Length;
			}
			return num;
		}
	}

	public Bubble(string role, string text, Action<string> resend)
	{
		Bubble bubble = this;
		_plain = text ?? "";
		_answer = _plain;
		OrigText = _plain;
		_mine = role == "user";
		_error = role == "error";
		_thinkingLabel = Ui.Txt("", 12.5, bold: false, "B.InkMuted");
		_thinking = Ui.H(7.0, AiIcons.Icon("M12 18V5 M15 13a4.17 4.17 0 0 1-3-4 4.17 4.17 0 0 1-3 4 M17.598 6.5A3 3 0 1 0 12 5a3 3 0 1 0-5.598 1.5 M17.997 5.125a4 4 0 0 1 2.526 5.77 M18 18a4 4 0 0 0 2-7.464 M19.967 17.483A4 4 0 1 1 12 18a4 4 0 1 1-7.967-.517 M6 18a4 4 0 0 1-2-7.464 M6.003 5.125a4 4 0 0 0-2.526 5.77", 13.0).VCenter(), _thinkingLabel.VCenter());
		_thinking.Visibility = Visibility.Collapsed;
		_actions = Ui.H(2.0);
		if (_mine)
		{
			_plainBody = new TextBlock
			{
				Text = _plain,
				TextWrapping = TextWrapping.Wrap,
				FontSize = 13.5,
				LineHeight = 22.0
			};
			_plainBody.SetResourceReference(TextBlock.ForegroundProperty, "B.Ink");
			Border border = new Border
			{
				CornerRadius = new CornerRadius(14.0, 14.0, 4.0, 14.0),
				Padding = new Thickness(13.0, 8.0, 13.0, 9.0),
				Child = _plainBody,
				HorizontalAlignment = HorizontalAlignment.Right
			};
			border.SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
			border.ContextMenu = CopyMenu();
			_userBody = border;
			var editInput = new ChatInput { Tag = "" };
			editInput.Submitted += _ => bubble.ConfirmEdit();
			_editBox = editInput;
			_editBox.AcceptsReturn = true;
			_editBox.MinWidth = 320.0;
			_editBox.PreviewKeyDown += delegate(object _, System.Windows.Input.KeyEventArgs e)
			{
				if (e.Key == System.Windows.Input.Key.Escape)
				{
					bubble.CancelEdit();
					e.Handled = true;
				}
			};
			Button ok = Ui.Btn(I18n.L("发送"), BtnKind.Primary, delegate
			{
				bubble.ConfirmEdit();
			});
			Button cancel = Ui.Btn(I18n.L("取消"), BtnKind.Ghost, delegate
			{
				bubble.CancelEdit();
			});
			SPanel editBtns = Ui.H(6.0, Ui.Small(I18n.L("会撤回到这句话之前再重新发送")).VCenter(), cancel, ok);
			editBtns.Wrap = true;
			editBtns.HorizontalAlignment = HorizontalAlignment.Right;
			_editor = Ui.V(6.0, _editBox, editBtns);
			_editor.Visibility = Visibility.Collapsed;
			_actions.Children.Add(AiIcons.Flat("M21 12a9 9 0 1 1-3-6.7L21 8 M21 3v5h-5", I18n.L("重新发送"), I18n.L("把这句话再发一次"), delegate
			{
				resend(bubble._plain);
			}, "B.InkMuted", 12.0, pill: true));
			_rewindBtn = AiIcons.Flat("M3 7v6h6 M21 17a9 9 0 0 0-15-6.7L3 13", I18n.L("撤回这一轮"), I18n.L("回退到这句话之前，并尽量还原这一轮改过的文件"), delegate
			{
				(bubble._rewindBtn?.Tag as Action)?.Invoke();
			}, "B.Warn", 12.0, pill: true);
			_rewindBtn.Visibility = Visibility.Collapsed;
			_actions.Children.Add(_rewindBtn);
			_editBtn = AiIcons.Flat("M12 20h9 M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z", I18n.L("编辑"), I18n.L("改这句话，撤回到它之前再重新发送"), delegate
			{
				bubble.BeginEdit();
			}, "B.InkMuted", 12.0, pill: true);
			_editBtn.Visibility = Visibility.Collapsed;
			_actions.Children.Insert(0, _editBtn);
			_actions.HorizontalAlignment = HorizontalAlignment.Right;
			_actions.Opacity = 0.0;
			_actions.Margin = new Thickness(0.0, 4.0, 0.0, 0.0);
			base.HorizontalAlignment = HorizontalAlignment.Right;
			base.MaxWidth = 600.0;
			base.Background = Brushes.Transparent;
			Child = Ui.V(0.0, border, _editor, _actions);
			base.MouseEnter += delegate
			{
				bubble._actions.Opacity = 1.0;
			};
			base.MouseLeave += delegate
			{
				bubble._actions.Opacity = 0.0;
			};
			return;
		}
		_think = new ThinkFold
		{
			Visibility = Visibility.Collapsed
		};
		_md = new MdView();
		if (_error)
		{
			Grid grid = Ui.G(null, "Auto,*");
			grid.Add(AiIcons.Icon("M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M15 9l-6 6 M9 9l6 6", 14.0, "B.Danger").VCenter(), 0);
			grid.Add(Ui.Txt(I18n.L("出错了"), 12.5, bold: true, "B.Danger").M(7.0, 0.0, 0.0, 0.0).VCenter(), 0, 1);
			_actions.Children.Add(AiIcons.Flat("M11 9h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2z M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1", null, I18n.L("复制"), Copy, "B.InkMuted", 13.0));
			_retryBtn = AiIcons.Flat("M21 12a9 9 0 1 1-3-6.7L21 8 M21 3v5h-5", I18n.L("重试"), I18n.L("再试一次"), delegate
			{
				(bubble._retryBtn?.Tag as Action)?.Invoke();
			}, "B.InkMuted", 12.0);
			_retryBtn.Visibility = Visibility.Collapsed;
			_actions.Children.Add(_retryBtn);
			base.CornerRadius = new CornerRadius(10.0);
			base.Padding = new Thickness(12.0, 9.0, 12.0, 8.0);
			base.BorderThickness = new Thickness(1.0);
			SetResourceReference(Border.BackgroundProperty, "B.DangerSoft");
			SetResourceReference(Border.BorderBrushProperty, "B.Danger");
			Child = Ui.V(4.0, grid, _md, _actions);
		}
		else
		{
			_actions.Children.Add(AiIcons.Flat("M11 9h9a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-9a2 2 0 0 1 2-2z M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1", null, I18n.L("复制这条"), Copy, "B.InkFaint"));
			_actions.Children.Add(Vote("M7 10v12 M15 5.9L14 10h5.8a2 2 0 0 1 2 2.3l-1.4 8a2 2 0 0 1-2 1.7H7V10l4-8a3 3 0 0 1 4 3.9z", I18n.L("有帮助")));
			_actions.Children.Add(Vote("M17 14V2 M9 18.1L10 14H4.2a2 2 0 0 1-2-2.3l1.4-8A2 2 0 0 1 5.6 2H17v12l-4 8a3 3 0 0 1-4-3.9z", I18n.L("没帮助")));
			_retryBtn = AiIcons.Flat("M21 12a9 9 0 1 1-3-6.7L21 8 M21 3v5h-5", null, I18n.L("重新生成这条回复"), delegate
			{
				(bubble._retryBtn?.Tag as Action)?.Invoke();
			}, "B.InkFaint");
			_retryBtn.Visibility = Visibility.Collapsed;
			_actions.Children.Add(_retryBtn);
			_actions.Margin = new Thickness(-4.0, 2.0, 0.0, 0.0);
			base.Background = Brushes.Transparent;
			base.Padding = new Thickness(0.0);
			Child = Ui.V(6.0, _thinking, _think, _md, _actions);
		}
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		base.MaxWidth = 760.0;
		Render(live: false);
	}

	internal void BeginEdit()
	{
		if (_editor == null || _editBox == null || _userBody == null)
		{
			return;
		}
		_editBox.Text = OrigText;
		_userBody.Visibility = Visibility.Collapsed;
		_actions.Visibility = Visibility.Collapsed;
		_editor.Visibility = Visibility.Visible;
		base.MaxWidth = 760.0;
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		_editBox.Focus();
		_editBox.CaretIndex = _editBox.Text.Length;
	}

	internal void CancelEdit()
	{
		if (_editor == null || _userBody == null || _editor.Visibility != Visibility.Visible)
		{
			return;
		}
		_editor.Visibility = Visibility.Collapsed;
		_userBody.Visibility = Visibility.Visible;
		_actions.Visibility = Visibility.Visible;
		base.MaxWidth = 600.0;
		base.HorizontalAlignment = HorizontalAlignment.Right;
	}

	private void ConfirmEdit()
	{
		string text = _editBox?.Text?.Trim() ?? "";
		Action<Bubble, string> action = _editBtn?.Tag as Action<Bubble, string>;
		if (text.Length == 0 || action == null)
		{
			return;
		}
		CancelEdit();
		action(this, text);
	}

	private Button Vote(string icon, string tip)
	{
		return AiIcons.Flat(icon, null, tip, delegate
		{
			foreach (Button item in _actions.Children.OfType<Button>())
			{
				item.Opacity = 1.0;
			}
			AppServices.Toast(I18n.L("谢谢反馈"), "", ToastKind.Success);
		}, "B.InkFaint");
	}

	private ContextMenu CopyMenu()
	{
		ContextMenu contextMenu = new ContextMenu();
		MenuItem menuItem = new MenuItem
		{
			Header = I18n.L("复制")
		};
		menuItem.Click += delegate
		{
			Copy();
		};
		contextMenu.Items.Add(menuItem);
		return contextMenu;
	}

	public void SetText(string text, bool live = false)
	{
		_plain = text ?? "";
		_live = live;
		if (_thinking.Visibility == Visibility.Visible)
		{
			Lucide.Shimmer(_thinkingLabel, on: false, "B.InkMuted");
			_thinking.Visibility = Visibility.Collapsed;
			if (_md != null)
			{
				_md.Visibility = Visibility.Visible;
			}
		}
		Render(live);
	}

	private void Render(bool live)
	{
		if (_mine || _think == null || _md == null)
		{
			_answer = _plain;
			if (_plainBody != null)
			{
				_plainBody.Text = _plain;
			}
			else
			{
				_md?.SetMarkdown(_plain);
			}
			return;
		}
		(List<string>, string) tuple = SplitThink(_plain);
		List<string> item = tuple.Item1;
		string text = (_answer = tuple.Item2);
		if (item.Count > 0)
		{
			bool flag = ThinkOpen(_plain);
			_think.SetThink(string.Join("\n\n", from p in item
				select p.Trim() into p
				where p.Length > 0
				select p), live, live && flag);
			_think.Visibility = Visibility.Visible;
			_md.Visibility = ((live && text.Length == 0) ? Visibility.Collapsed : Visibility.Visible);
			_md.SetMarkdown((!live && text.Length == 0) ? "…" : text, live);
		}
		else
		{
			_think.Visibility = Visibility.Collapsed;
			if (_thinking.Visibility != Visibility.Visible)
			{
				_md.Visibility = Visibility.Visible;
			}
			_md.SetMarkdown(text, live);
		}
		_actions.Visibility = ((live || text.Length == 0 || text == "…") ? Visibility.Collapsed : Visibility.Visible);
	}

	public void SetThinking(string text)
	{
		_plain = "";
		_answer = "";
		_md?.SetMarkdown("");
		_thinkingLabel.Text = text ?? "";
		_thinking.Visibility = Visibility.Visible;
		if (_md != null)
		{
			_md.Visibility = Visibility.Collapsed;
		}
		if (_think != null)
		{
			_think.Visibility = Visibility.Collapsed;
		}
		_actions.Visibility = Visibility.Collapsed;
		Lucide.Shimmer(_thinkingLabel, on: true, "B.InkMuted");
	}

	private void Copy()
	{
		Ui.Copy(string.IsNullOrEmpty(_answer) ? _plain : _answer);
	}
}
