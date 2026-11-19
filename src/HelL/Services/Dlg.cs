using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HelL.Services;

public static class Dlg
{
	public sealed class Layer
	{
		public required Grid Root { get; init; }

		public required Border Card { get; init; }

		public required Action Close { get; init; }

		public required Action Dismiss { get; init; }
	}

	private sealed class Scope : IDisposable
	{
		[CompilerGenerated]
		private Action _003Cclose_003EP;

		private int _done;

		public Scope(Action close)
		{
			_003Cclose_003EP = close;
		}

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _done, 1) == 0)
			{
				_003Cclose_003EP();
			}
		}
	}

	public static Grid? Host { get; set; }

	public static UIElement? BlurTarget { get; set; }

	public static bool AnyOpen
	{
		get
		{
			Grid host = Host;
			if (host != null)
			{
				UIElementCollection children = host.Children;
				if (children != null)
				{
					return children.Count > 0;
				}
			}
			return false;
		}
	}

	private static Layer Push(UIElement card, Action? onDismiss, bool dismissable = true, bool smokeAutoAnswer = true, Action? onDismissOnly = null, System.Windows.Controls.Button? ok = null, Func<bool>? canDismiss = null, bool dimBackground = true)
	{
		Grid host = Host ?? throw new InvalidOperationException(I18n.L("对话层未初始化"));
		IInputElement previousFocus = Keyboard.FocusedElement;
		bool closed = false;
		bool dismissing = false;
        var covered = new System.Collections.Generic.List<UIElement>();
        foreach (UIElement previous in host.Children) covered.Add(previous);
		Border mask = new Border
		{
			Opacity = 0.0
		};
        if (dimBackground) mask.SetResourceReference(Border.BackgroundProperty, "B.Mask");
        else mask.Background = Brushes.Transparent;
		Grid wrap = new Grid();
		// 弹窗内 Tab / 方向键不能跑到后面页面上
		KeyboardNavigation.SetTabNavigation(wrap, KeyboardNavigationMode.Cycle);
		KeyboardNavigation.SetDirectionalNavigation(wrap, KeyboardNavigationMode.Contained);
		wrap.Children.Add(mask);
		Grid holder = new Grid
		{
			HorizontalAlignment = card is Border { MaxWidth: < double.PositiveInfinity } ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
			MaxWidth = (card as Border)?.MaxWidth ?? double.PositiveInfinity,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(24.0)
		};
		holder.Children.Add(new PopupShadow { Child = card, CornerRadius = (card as Border)?.CornerRadius ?? new CornerRadius(8) });
		wrap.Children.Add(holder);
		host.Children.Add(wrap);
		host.Visibility = Visibility.Visible;
        void KeepDialogReadable()
        {
            OverlayReadability.Cover(wrap, Glass.PopupTransparency >= 70);
            OverlayReadability.CoverElements(holder, !dimBackground || Glass.PopupTransparency >= 70, covered);
        }
        App.ThemeVisualsChanged += KeepDialogReadable;
        KeepDialogReadable();
		// 整层模糊会让每帧重绘整页，弹窗遮挡只靠遮罩变暗足够。
		if (BlurTarget != null && host.Children.Count == 1)
		{
			BlurTarget.Effect = null;
		}
		Motion.Fade(mask, 1.0, 150.0);
		Motion.PopIn(holder);
		if (dismissable)
		{
			mask.MouseLeftButtonDown += delegate
			{
				Dismiss();
			};
		}
		wrap.Focusable = true;
		wrap.Loaded += delegate
		{
			wrap.Focus();
		};
		wrap.PreviewKeyDown += delegate(object _, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Escape && dismissable && IsTopmost())
			{
				var selector = OpenSelector(card);
				if (selector != null) { selector.IsDropDownOpen = false; selector.Focus(); e.Handled = true; return; }
				DependencyObject? source = Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject;
				if (HasOpenSelector(source)) return;
				Dismiss();
				e.Handled = true;
			}
		};
		if (ok != null)
		{
			wrap.PreviewKeyDown += delegate(object _, System.Windows.Input.KeyEventArgs e)
			{
				if (e.Key != Key.Return || Keyboard.Modifiers != ModifierKeys.None || !IsTopmost() || closed || !ok.IsEnabled || !ok.IsVisible)
				{
					return;
				}
				DependencyObject source = Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject;
				// Let focused buttons, multiline editors and open selectors own Enter.
				if (Ui.FromButton(source) || HasOpenSelector(source)) return;
				System.Windows.Controls.TextBox textBox = source as System.Windows.Controls.TextBox;
				if (textBox != null && textBox.AcceptsReturn)
				{
					return;
				}
				e.Handled = true;
				ok.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
			};
		}
		if (Smoke.Active && smokeAutoAnswer)
		{
			Smoke.AutoAnswerDialog(card, Dismiss);
		}
		return new Layer
		{
			Root = wrap,
			Card = ((card as Border) ?? new Border()),
			Close = Close,
			Dismiss = Dismiss
		};
		bool IsTopmost() => host.Children.Count > 0 && host.Children[host.Children.Count - 1] == wrap;
		void Dismiss()
		{
			if (closed || dismissing || !IsTopmost() || canDismiss?.Invoke() == false) return;
			dismissing = true;
			try
			{
				onDismissOnly?.Invoke();
				onDismiss?.Invoke();
			}
			finally { Close(); }
		}
		void Close()
		{
			if (closed) return;
			closed = true;
            App.ThemeVisualsChanged -= KeepDialogReadable;
			bool restoreFocus = IsTopmost();
			wrap.IsHitTestVisible = false;
			if (host.Children.Contains(wrap))
			{
				Motion.Fade(mask, 0.0, 130.0);
				Motion.PopOut(holder, delegate
				{
					bool stillTop = IsTopmost();
					host.Children.Remove(wrap);
                    OverlayReadability.Cover(wrap, false);
                    OverlayReadability.CoverElements(holder, false, covered);
                    App.RaiseThemeVisualsChanged();
					if (host.Children.Count == 0)
					{
						host.Visibility = Visibility.Collapsed;
						if (BlurTarget != null)
						{
							BlurTarget.Effect = null;
						}
					}
					if (restoreFocus && stillTop && previousFocus is UIElement target && target.IsVisible && target.IsEnabled)
						target.Focus();
				});
			}
		}
	}

	private static bool HasOpenSelector(DependencyObject? source)
	{
		while (source != null)
		{
			if (source is System.Windows.Controls.ComboBox combo && combo.IsDropDownOpen) return true;
			source = Ui.ParentOf(source);
		}
		return false;
	}

	private static ComboBox? OpenSelector(DependencyObject root)
	{
		if (root is ComboBox { IsDropDownOpen: true } combo) return combo;
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var open = OpenSelector(VisualTreeHelper.GetChild(root, i));
			if (open != null) return open;
		}
		return null;
	}

	private static UIElement? FindInput(DependencyObject root)
	{
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is System.Windows.Controls.TextBox textBox && textBox.Focusable && textBox.IsEnabled && textBox.IsVisible && !textBox.IsReadOnly)
			{
				return textBox;
			}
			UIElement uIElement = FindInput(child);
			if (uIElement != null)
			{
				return uIElement;
			}
		}
		return null;
	}

	private static void FocusOnLoad(Layer layer, UIElement card, UIElement fallback)
	{
		layer.Root.Loaded += delegate
		{
			UIElement target = FindInput(card) ?? fallback;
			target.Focus();
			if (target is System.Windows.Controls.TextBox textBox)
			{
				textBox.SelectAll();
			}
		};
	}

	private static Border Shell(string title, UIElement body, UIElement? footer, double width)
	{
		Grid grid = Ui.G(null, "*,Auto");
		grid.Add(Ui.H2(title).Wrap().VCenter(), 0);
		Grid content = Ui.G("Auto,*,Auto");
		content.Add(grid.M(0, 0, 0, 16), 0);
		content.Add(body is ScrollViewer ? body : Ui.Scroll(body), 1);
		if (footer != null)
		{
			Border footerBar = new Border
			{
				Child = footer,
				Padding = new Thickness(0, 14, 0, 0),
				Margin = new Thickness(0, 18, 0, 0),
				BorderThickness = new Thickness(0, 1, 0, 0)
			};
			footerBar.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
			content.Add(footerBar, 2);
		}
		Border border = new Border();
		border.CornerRadius = new CornerRadius(12);
		border.Padding = new Thickness(24, 20, 24, 20);
		border.BorderThickness = new Thickness(1.0);
		border.MaxWidth = width;
		border.MinWidth = Math.Min(width, 380.0);
		border.Child = content;
		border.SetResourceReference(Border.BackgroundProperty, "B.Popup");
		border.SetResourceReference(Border.BorderBrushProperty, "B.PopupBorder");
		System.Windows.Automation.AutomationProperties.SetName(border, title);
		return border;
	}

	public static Task<bool> Confirm(string title, string body, string? ok = null, string? cancel = null, bool danger = false)
	{
		return Ask(title, Ui.Muted(body).Wrap().MinW(320.0), ok, cancel, danger);
	}

	public static Task<bool> Ask(string title, UIElement body, string? ok = null, string? cancel = null, bool danger = false, double width = 520.0)
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		System.Windows.Controls.Button button = Ui.Btn(ok ?? I18n.L("确定"), danger ? BtnKind.Danger : BtnKind.Primary);
		System.Windows.Controls.Button button2 = Ui.Btn(cancel ?? I18n.L("取消"));
		SPanel footer = Ui.H(8.0, button2, button).Right();
		Border card = Shell(title, body, footer, width);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(result: false);
		}, ok: danger ? null : button);
		if (danger) layer.Root.Loaded += (_, _) => button2.Focus();
		else FocusOnLoad(layer, card, button);
		button.Click += delegate
		{
			tcs.TrySetResult(result: true);
			layer.Close();
		};
		button2.Click += delegate
		{
			tcs.TrySetResult(result: false);
			layer.Close();
		};
		return tcs.Task;
	}

	/// <summary>
	/// 三态确认：明确同意 true，明确拒绝 false，按 Esc / 点遮罩关掉 null。
	/// 要区分“拒绝了”和“没表态”的流程（如上传诊断数据的同意）用它，别用 Confirm。
	/// </summary>
	public static Task<bool?> ConfirmOrDismiss(string title, string body, string? ok = null, string? cancel = null)
	{
		TaskCompletionSource<bool?> tcs = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
		System.Windows.Controls.Button button = Ui.Btn(ok ?? I18n.L("确定"), BtnKind.Primary);
		System.Windows.Controls.Button button2 = Ui.Btn(cancel ?? I18n.L("取消"));
		SPanel footer = Ui.H(8.0, button2, button).Right();
		Border card = Shell(title, Ui.Muted(body).Wrap().MinW(320.0), footer, 520.0);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(null);
		}, ok: button);
		FocusOnLoad(layer, card, button);
		button.Click += delegate
		{
			tcs.TrySetResult(result: true);
			layer.Close();
		};
		button2.Click += delegate
		{
			tcs.TrySetResult(result: false);
			layer.Close();
		};
		return tcs.Task;
	}

	public static Task Alert(string title, string body, double width = 520.0)
	{
		TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		System.Windows.Controls.Button button = Ui.Btn(I18n.L("知道了"), BtnKind.Primary);
		System.Windows.Controls.TextBox textBox = new System.Windows.Controls.TextBox
		{
			Text = body,
			IsReadOnly = true,
			BorderThickness = new Thickness(0.0),
			Background = Brushes.Transparent,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 380.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			FontSize = 13.0
		};
		textBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "B.InkSoft");
		Border card = Shell(title, textBox, Ui.H(8.0, button).Right(), width);
		Layer layer = Push(card, delegate
		{
			tcs.TrySetResult(result: true);
		}, ok: button);
		FocusOnLoad(layer, card, button);
		button.Click += delegate
		{
			tcs.TrySetResult(result: true);
			layer.Close();
		};
		return tcs.Task;
	}

	public static Task<string?> Prompt(string title, string label, string initial = "", string placeholder = "")
	{
		TaskCompletionSource<string?> tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		System.Windows.Controls.TextBox input = Ui.Input(placeholder, initial).MinW(340.0);
		SPanel body = Ui.V(6.0, Ui.Muted(label), input);
		System.Windows.Controls.Button button = Ui.Btn(I18n.L("确定"), BtnKind.Primary);
		System.Windows.Controls.Button button2 = Ui.Btn(I18n.L("取消"));
		Border card = Shell(title, body, Ui.H(8.0, button2, button).Right(), 460.0);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(null);
		}, ok: button);
		button.Click += delegate
		{
			Ok();
		};
		button2.Click += delegate
		{
			tcs.TrySetResult(null);
			layer.Close();
		};
		input.Loaded += delegate
		{
			input.Focus();
			input.SelectAll();
		};
		return tcs.Task;
		void Ok()
		{
			tcs.TrySetResult(input.Text?.Trim() ?? "");
			layer.Close();
		}
	}

	public static Task<int> Choose(string title, UIElement body, string[] options, double width = 560.0, bool dismissable = true)
	{
		TaskCompletionSource<int> tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		SPanel sPanel = Ui.H(8.0).Right();
		KeyboardNavigation.SetDirectionalNavigation(sPanel, KeyboardNavigationMode.Contained);
		KeyboardNavigation.SetTabNavigation(sPanel, KeyboardNavigationMode.Cycle);
		Border card = Shell(title, body, sPanel, width);
		Layer layer = null;
		layer = Push(card, delegate
		{
			tcs.TrySetResult(-1);
		}, dismissable);
		System.Windows.Controls.Button first = null;
		for (int num = 0; num < options.Length; num++)
		{
			int idx = num;
			System.Windows.Controls.Button button = Ui.Btn(options[num], (num != options.Length - 1) ? BtnKind.Normal : BtnKind.Primary);
			button.Click += delegate
			{
				tcs.TrySetResult(idx);
				layer.Close();
			};
			if (first == null)
			{
				first = button;
			}
			sPanel.Children.Add(button);
		}
		sPanel.PreviewKeyDown += delegate(object _, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key != Key.Left && e.Key != Key.Right && e.Key != Key.Up && e.Key != Key.Down)
			{
				return;
			}
			if (sPanel.Children.Count == 0)
			{
				return;
			}
			int num2 = -1;
			for (int num3 = 0; num3 < sPanel.Children.Count; num3++)
			{
				if (sPanel.Children[num3].IsKeyboardFocusWithin)
				{
					num2 = num3;
					break;
				}
			}
			int num4 = ((e.Key == Key.Left || e.Key == Key.Up) ? (-1) : 1);
			int num5;
			if (num2 < 0)
			{
				num5 = ((num4 < 0) ? (sPanel.Children.Count - 1) : 0);
			}
			else
			{
				num5 = num2 + num4;
				if (num5 < 0)
				{
					num5 = 0;
				}
				else if (num5 >= sPanel.Children.Count)
				{
					num5 = sPanel.Children.Count - 1;
				}
			}
			sPanel.Children[num5].Focus();
			e.Handled = true;
		};
		if (first != null)
		{
			System.Windows.Controls.Button target = first;
			layer.Root.Loaded += delegate
			{
				target.Focus();
			};
		}
		return tcs.Task;
	}

	public static async Task<T?> Form<T>(string title, UIElement body, Func<T> collect, string? ok = null, double width = 560.0) where T : class
	{
		T? value = null;
		await EditAsync(title, body, () =>
		{
			value = collect();
			return Task.CompletedTask;
		}, ok, width);
		return value;
	}

	public static void Require(bool valid, UIElement field, string message)
	{
		if (!valid) throw new FieldValidationException(field, message);
	}

	public static Task<bool> EditAsync(string title, UIElement body, Func<Task> save, string? ok = null, double width = 560)
	{
		var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var submit = Ui.Btn(ok ?? I18n.L("保存"), BtnKind.Primary);
		var cancel = Ui.Btn(I18n.L("取消"));
		var error = Ui.Txt("", 12, fg: "B.Danger").Wrap();
		error.Visibility = Visibility.Collapsed;
		var footer = Ui.V(10, error, Ui.H(8, cancel, submit).Right());
		Border card = Shell(title, body, footer, width);
		bool saving = false;
		Layer layer = Push(card, () => completion.TrySetResult(false), ok: submit, canDismiss: () => !saving);
		FocusOnLoad(layer, card, submit);
		cancel.Click += (_, _) => layer.Dismiss();
		submit.Click += async (_, _) =>
		{
			if (saving) return;
			saving = true;
			submit.IsEnabled = cancel.IsEnabled = body.IsEnabled = false;
			object label = submit.Content;
			submit.Content = I18n.L("正在保存…");
			error.Visibility = Visibility.Collapsed;
			UIElement? invalidField = null;
			try
			{
				await save();
				completion.TrySetResult(true);
				layer.Close();
			}
			catch (FieldValidationException ex)
			{
				FieldState.SetError(ex.Field, ex.Message);
				error.Text = ex.Message;
				error.Visibility = Visibility.Visible;
				invalidField = ex.Field;
			}
			catch (Exception ex)
			{
				Smoke.Note("Dlg.EditAsync", ex.ToString());
				error.Text = I18n.L("保存失败：{0}。输入已保留，可以重试。", ex.Message);
				error.Visibility = Visibility.Visible;
			}
			finally
			{
				saving = false;
				submit.IsEnabled = cancel.IsEnabled = body.IsEnabled = true;
				submit.Content = label;
				invalidField?.Focus();
			}
		};
		return completion.Task;
	}

	public static Layer Panel(string title, UIElement body, double width = 720.0, Action? onClose = null, bool dismissable = true, Action? onDismiss = null, UIElement? footer = null, bool dimBackground = true, Func<bool>? canDismiss = null)
	{
		System.Windows.Controls.Button button = Ui.IconBtn("\ue711", I18n.L("关闭"));
		Border border2 = Shell(title, body, footer, width);
		Grid grid = (Grid)((Grid)border2.Child).Children[0];
		grid.Add(button, 0, 1);
		Layer layer = Push(border2, onClose, dismissable, smokeAutoAnswer: true, onDismiss, canDismiss: canDismiss, dimBackground: dimBackground);
		button.Click += delegate
		{
			layer.Dismiss();
		};
		return layer;
	}

	public static IDisposable Busy(string? text = null, Action? cancel = null)
	{
		if (text == null)
		{
			text = I18n.L("处理中…");
		}
		System.Windows.Controls.ProgressBar progressBar = Ui.Prog();
		progressBar.IsIndeterminate = true;
		progressBar.Width = 220.0;
		Border border = new Border();
		border.CornerRadius = new CornerRadius(8.0);
		border.Padding = new Thickness(24.0, 20.0, 24.0, 20.0);
		UIElement content = Ui.V(12.0, Ui.Txt(text).Center(), progressBar);
		if (cancel != null)
		{
			System.Windows.Controls.Button button = Ui.Btn(I18n.L("取消"), BtnKind.Ghost, delegate { cancel(); });
			content = Ui.V(12.0, Ui.Txt(text).Center(), progressBar, button.Center().M(0.0, 6.0, 0.0, 0.0));
		}
		border.Child = content;
		border.SetResourceReference(Border.BackgroundProperty, "B.Popup");
        border.BorderThickness = new Thickness(1);
        border.SetResourceReference(Border.BorderBrushProperty, "B.PopupBorder");
		return new Scope(Push(border, null, dismissable: false, smokeAutoAnswer: false).Close);
	}

	public static string? PickFile(string filter, string? title = null)
	{
		return Pick(FilePickerMode.Open, title ?? I18n.L("选择文件"), filter)?[0];
	}

	public static string[]? PickFiles(string filter, string? title = null)
	{
		return Pick(FilePickerMode.OpenMany, title ?? I18n.L("选择文件"), filter);
	}

	public static string? SaveFile(string filter, string name, string? title = null)
	{
		return Pick(FilePickerMode.Save, title ?? I18n.L("保存到"), filter, name)?[0];
	}

	public static string? PickFolder(string? title = null)
	{
		return Pick(FilePickerMode.Folder, title ?? I18n.L("选择文件夹"))?[0];
	}

	private static string[]? Pick(FilePickerMode mode, string title, string filter = "", string filename = "")
	{
		if (Smoke.Active)
		{
			Smoke.AutoCancelSystemDialog(title);
			return null;
		}
		var picker = new FilePickerWindow(mode, title, filter, filename)
		{
			Owner = Host == null ? System.Windows.Application.Current.MainWindow : Window.GetWindow(Host)
		};
		return picker.ShowDialog() == true ? picker.SelectedPaths : null;
	}
}
