using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Diagnostics;

namespace HelL.Services;

// Both styles share a flush window shell. DWM owns the outer corners and shadow.

internal static class SideSkin
{
	private static Border? _side;

	private static ScrollViewer? _scroll;

	private static ColumnDefinition? _sideCol;

	private static ColumnDefinition? _gripCol;

	private static Border? _shell;

	private static Thumb? _grip;

	private static double _sideW = 196.0;

    public const double CompactWidth = 68;
    public static bool Collapsed { get; private set; }
    public static double ExpandedWidth => _sideW;
	public static double VisibleWidth => Collapsed ? CompactWidth : _sideW;

    public static void SetCollapsed(bool collapsed)
    {
        Collapsed = collapsed;
        Attachment();
    }

	/// <summary>侧栏面板内部左右各让出的宽度。</summary>
	public static double PadX => Glass.Skin.PadX;

	/// <summary>侧栏两列加起来占掉的总宽度，内容区布局要用它。</summary>
	public static double ColumnWidth => VisibleWidth;

	public static bool IsMac => Glass.IsMac;

	public static double MinWidth => 140.0;

	public static double MaxWidth => 320.0;

	/// <summary>把外部改过的栏宽（恢复偏好、后端设置）并进唯一口径，再整体重排。</summary>
	public static void Adopt(double width)
	{
		_sideW = Clamp.Of(width, MinWidth, MaxWidth);
		Attachment();
	}

	/// <summary>拖动拖拽条：只挪面板宽，列宽由 Apply 现算。</summary>
	public static void Resize(double delta)
	{
		if (delta == 0.0)
		{
			return;
		}
		_sideW = Clamp.Of(_sideW + delta, MinWidth, MaxWidth);
		Attachment();
	}

	public static void Attach(Border side, ScrollViewer scroll, ColumnDefinition sideCol, Thumb grip, ColumnDefinition gripCol, Border shell)
	{
		_side = side;
		_scroll = scroll;
		_sideCol = sideCol;
		_grip = grip;
		_gripCol = gripCol;
		_shell = shell;
		_sideW = Clamp.Of(sideCol.Width.Value, MinWidth, MaxWidth);
		App.ThemeVisualsChanged -= OnPaint;
		App.ThemeVisualsChanged += OnPaint;
		if (_shell != null)
		{
			_shell.SizeChanged += delegate
			{
				ClipShell();
			};
		}
		Attachment();
	}

	private static void Attachment()
	{
		Apply();
		SyncRail();
		SideFrostEx.LayoutNow();
	}

	private static void OnPaint()
	{
		try
		{
			Attachment();
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[SideSkin] OnPaint " + ex);
		}
	}

	/// <summary>让拖拽条落到侧栏的那一列：mac 在面板内，Win11 在面板右边（GripCol 占位为 0 时退回面板列）。</summary>
	public static void SyncRail()
	{
		if (_grip == null || _gripCol == null)
		{
			return;
		}
		bool mac = Glass.IsMac;
		double tab = (mac ? Math.Max(0.0, Glass.Skin.Tab) : Math.Max(0.0, _gripCol.Width.Value));
		_gripCol.Width = new GridLength(tab);
		Grid.SetColumn(_grip, (mac || tab > 0.0) ? 1 : 0);
	}

	public static void Apply()
	{
		if (_side == null || _scroll == null || _sideCol == null || _grip == null)
		{
			return;
		}
		try
		{
			GlassSkin skin = Glass.Skin;
			bool mac = Glass.IsMac;
            double inset = 0, tab = 0, gap = 0;
			_sideCol.Width = new GridLength(VisibleWidth + inset);
			_sideCol.MinWidth = 0.0;
			_side.Margin = new Thickness(inset, gap, tab, gap);
			SideFrost.SetInset(inset);
			SideFrost.SetRadius((int)Math.Round(skin.FrostRound));
			_side.BorderThickness = new Thickness(0, 0, 1, 0);
			_side.CornerRadius = new CornerRadius(0);
			_scroll.Padding = Collapsed ? new Thickness(0, 4, 0, 14) : (mac ? new Thickness(skin.PadX, 8.0, skin.PadX, 16.0) : new Thickness(0.0, 6.0, 0.0, 16.0));
			_grip.Visibility = Collapsed ? Visibility.Collapsed : Visibility.Visible;
			_grip.Width = (mac ? skin.Rest : 4.0);
			_grip.HorizontalAlignment = HorizontalAlignment.Right;
			_side.Effect = null;
			SideFrostEx.LayoutNow();
		}
		catch (Exception ex)
		{
            Debug.WriteLine("[SideSkin] Cannot apply sidebar layout: " + ex);
		}
	}

	public static void ClipShell()
	{
		if (_shell == null)
		{
			return;
		}
        _shell.Clip = null;
	}
}
