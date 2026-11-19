using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HelL.Services;

namespace HelL.Pages;

internal sealed class ChatInput : TextBox
{
	private const double MinH = 48.0;

	private const double MaxH = 140.0;

	private bool _composing;
	private bool _resizePending;

	public event Action<string>? Submitted;

	/// <summary>在发送逻辑之前看按键（斜杠命令候选列表用）；返回 true 表示已处理。</summary>
	public Func<Key, bool>? KeyIntercept { get; set; }

	public ChatInput()
	{
		base.Style = Ui.S("Input.Multi");
		base.Tag = I18n.L("问我要下什么、哪报错、模组怎么配…  Enter 发送，Shift+Enter 换行");
		base.AcceptsReturn = true;
		base.TextWrapping = TextWrapping.Wrap;
		base.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
		base.Height = 48.0;
		base.TextChanged += (_, _) => QueueResize();
		SizeChanged += (_, e) => { if (e.WidthChanged) QueueResize(); };
		AddHandler(TextCompositionManager.TextInputStartEvent, new TextCompositionEventHandler(OnComposition), true);
		AddHandler(TextCompositionManager.TextInputUpdateEvent, new TextCompositionEventHandler(OnComposition), true);
		// TextBox consumes committed IME text before ordinary bubbling handlers run.
		AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler(OnCompositionEnd), true);
		base.LostFocus += delegate
		{
			_composing = false;
		};
	}

	private void OnComposition(object sender, TextCompositionEventArgs e)
	{
		_composing = true;
	}

	private void OnCompositionEnd(object sender, TextCompositionEventArgs e)
	{
		_composing = false;
	}

	protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty) QueueResize();
	}

	private void QueueResize()
	{
		if (_resizePending) return;
		_resizePending = true;
		Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
		{
			_resizePending = false;
			if (IsLoaded) Grow();
		}));
	}

	private void Grow()
	{
		// Measure after wrapping settles; the last line includes fallback-font metrics.
		Rect lastLine = GetRectFromCharacterIndex(Text.Length, trailingEdge: true);
		if (lastLine.IsEmpty) return;
		double height = Clamp.Of(lastLine.Bottom + VerticalOffset + Padding.Bottom, MinH, MaxH);
		if (Math.Abs(Height - height) > .5) Height = height;
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
		if (!_composing && e.Key != Key.ImeProcessed && e.ImeProcessedKey == Key.None && !shift && KeyIntercept != null && KeyIntercept(e.Key))
		{
			e.Handled = true;
			return;
		}
		if (ImeGuard.ShouldSend(e.Key, e.ImeProcessedKey, shift, _composing ? "composing" : ""))
		{
			string text = base.Text?.Trim() ?? "";
			if (text.Length > 0)
			{
				this.Submitted?.Invoke(text);
			}
			e.Handled = true;
		}
		else
		{
			base.OnPreviewKeyDown(e);
		}
	}
}
