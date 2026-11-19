using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Services;

namespace HelL.Pages;

public abstract class PageBase : UserControl
{
	private bool _loaded;

	private Task? _inflight;

	public string Id { get; set; } = "";

	public virtual string Title => "";

	protected static BridgeClient Api => AppServices.Client;

	protected static MainWindow? Win => AppServices.Window;

	protected PageBase()
	{
		base.Focusable = false;
		base.HorizontalAlignment = HorizontalAlignment.Stretch;
		base.VerticalAlignment = VerticalAlignment.Stretch;
	}

	public Task EnsureLoadedAsync()
	{
		if (_loaded)
		{
			return Task.CompletedTask;
		}
		return _inflight ?? (_inflight = LoadOnceAsync());
	}

	private async Task LoadOnceAsync()
	{
		try
		{
			await LoadAsync();
			_loaded = true;
		}
		catch (Exception ex)
		{
			Smoke.Note(string.IsNullOrEmpty(Id) ? GetType().Name : Id, ex.ToString());
			Toast(I18n.L("加载失败"), ex.Message, ToastKind.Error);
		}
		finally
		{
			_inflight = null;
		}
	}

	protected virtual Task LoadAsync()
	{
		return Task.CompletedTask;
	}

	public virtual Task RefreshAsync()
	{
		if (!_loaded)
		{
			return Task.CompletedTask;
		}
		return LoadAsync();
	}

	public virtual void OnEvent(BridgeEvent ev)
	{
	}

	public virtual void OnShown()
	{
	}

	public virtual void OnHidden()
	{
	}

	protected static void Toast(string title, string body = "", ToastKind kind = ToastKind.Info)
	{
		AppServices.Window?.Toast(title, body, kind);
	}

	public static async void Run(Func<Task> work, string? failTitle = null)
	{
		long op = Smoke.OperationStarted();
		try
		{
			await work();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Smoke.Note("handler", ex2.ToString());
			AppServices.Window?.Toast(failTitle ?? I18n.L("操作失败"), ex2.Message, ToastKind.Error);
		}
		finally
		{
			Smoke.OperationFinished(op);
		}
	}

	public static async void RunOnce(UIElement target, Func<Task> work, string? failTitle = null)
	{
		if (!target.IsEnabled)
		{
			return;
		}
		target.IsEnabled = false;
		long op = Smoke.OperationStarted();
		try
		{
			await work();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Smoke.Note("handler", ex2.ToString());
			AppServices.Window?.Toast(failTitle ?? I18n.L("操作失败"), ex2.Message, ToastKind.Error);
		}
		finally
		{
			target.IsEnabled = true;
			Smoke.OperationFinished(op);
		}
	}

	public static async void Fire(Func<Task> work)
	{
		try
		{
			await work();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			Smoke.Note("fire", ex2.ToString());
		}
	}

	protected void EnableDrop(Func<FileDrop.DropContext> context)
	{
		base.AllowDrop = true;
		base.PreviewDragOver += delegate(object _, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effects = DragDropEffects.Copy;
				e.Handled = true;
			}
		};
		base.PreviewDrop += delegate(object _, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				object data = e.Data.GetData(DataFormats.FileDrop);
				string[] paths = data as string[];
				if (paths != null && paths.Length != 0)
				{
					e.Handled = true;
					MainWindow win = Win;
					if (win != null)
					{
						Run(() => FileDrop.HandleAsync(win, paths, context()), I18n.L("拖放处理失败"));
					}
				}
			}
		};
	}

	protected static Panel Body(params UIElement?[] kids)
	{
		SPanel sPanel = Ui.V(UiMetrics.SectionGap, kids);
		sPanel.Margin = UiMetrics.PageMargin;
		return sPanel;
	}

	protected static SmoothScroll ScrollBody(params UIElement?[] kids)
	{
		SPanel sPanel = Ui.V(UiMetrics.SectionGap, kids);
		sPanel.Margin = UiMetrics.PageMargin;
		return Ui.Scroll(sPanel);
	}
}
