using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HelL.Services;

/// <summary>
/// 玻璃拟态的运行时：持有当前皮肤（GlassSkin），把皮肤参数翻译成画刷。
/// 颜色都基于主题色 B.Accent 和纸色 C.Paper 现算，所以换主题色、换深浅色之后
/// 只要再跑一次 <see cref="Refresh"/> 就整体跟上，不需要另存一份配色表。
/// </summary>
public static class Glass
{
	private static readonly string[] FontCandidates = new string[4] { "Segoe UI Variable Text", "Segoe UI Variable Display", "Microsoft YaHei UI", "Segoe UI" };

	private static GlassSkin _skin = GlassSkin.Read(GlassSkin.Win11);

	private static string _key = GlassSkin.Win11;

	private static int _tintOverride = -1;

	public static GlassSkin Skin => _skin;

	public static string Key => _key;

	public static bool IsMac => _key == GlassSkin.MacOS;
    public static int Transparency { get; private set; } = 100;
    public static int Frost { get; private set; } = 30;
    public static int CardTransparency { get; private set; } = 100;
    public static int ControlTransparency { get; private set; } = 100;
    public static int PopupTransparency { get; private set; } = 100;
    public static bool ClearWallpaper => Transparency == 100 && CardTransparency == 100 && ControlTransparency == 100 && Frost == 0;

    public static void SetMaterial(int transparency, int frost, int cards = -1, int controls = -1, int popups = -1)
    {
        Transparency = Math.Max(0, Math.Min(100, transparency)); Frost = Math.Max(0, Math.Min(60, frost));
        CardTransparency = Math.Max(0, Math.Min(100, cards < 0 ? Transparency : cards));
        ControlTransparency = Math.Max(0, Math.Min(100, controls < 0 ? Transparency : controls));
        PopupTransparency = Math.Max(0, Math.Min(100, popups < 0 ? Transparency : popups));
        Refresh();
    }

	/// <summary>当前生效的着色强度，0–100。用户拖过滑杆就用滑杆值，否则是皮肤默认值。</summary>
	public static int Tint
	{
		get
		{
			if (_tintOverride >= 0)
			{
				return _tintOverride;
			}
			return (int)Num(_skin.Tint, 0.0, 100.0);
		}
	}

	/// <summary>切皮肤。返回是否真的变了。</summary>
	public static bool Use(string? key)
	{
		string text = (key == GlassSkin.MacOS) ? GlassSkin.MacOS : GlassSkin.Win11;
		bool result = text != _key;
		_key = text;
		_skin = GlassSkin.Read(text);
		return result;
	}

	/// <summary>用户拖滑杆：-1 表示回到皮肤默认值。</summary>
	public static void SetTint(int percent)
	{
		_tintOverride = ((percent < 0) ? (-1) : ((int)Num(percent, 0.0, 100.0)));
	}

	/// <summary>
	/// 把玻璃相关的画刷写进调色板。调用点统一在 SettingsPage.ApplyVisuals，
	/// 也就是"主题色 / 深浅色 / 皮肤 任一变化"之后都会跑一遍。
	/// 这里只写玻璃本身的键；强调色系列仍由 ApplyAccent 负责。
	/// </summary>
	public static void Refresh()
	{
		ResourceDictionary palette = Palette;
		if (palette == null)
		{
			return;
		}
		bool dark = App.IsDark;
		Color paper = Read(palette, "C.Paper") is Color c ? c : (dark ? Color.FromRgb(30, 30, 30) : Colors.White);
		Color accent = Read(palette, "C.Accent") is Color a ? a : Color.FromRgb(169, 154, 255);
		Color canvas = Read(palette, "C.Canvas") is Color background ? background : paper;
		bool material = Backdrop.EffectsEnabled && (Backdrop.NativeAvailable || Wallpaper.Active);
        double Alpha(int transparency) => material ? 1 - transparency / 100d : 1;
        double page = Alpha(Transparency), card = Alpha(CardTransparency), control = Alpha(ControlTransparency), popup = Alpha(PopupTransparency);
		Color glassPaper = Mix(canvas, paper, dark ? .64 : .86);
		Color sideSurface = Mix(canvas, paper, dark ? .30 : .92);
		Color chromeSurface = Mix(canvas, paper, dark ? .40 : .96);
		Color tinted = Mix(glassPaper, accent, Tint / 100.0 * 0.035);
		Set(palette, "B.WindowBase", Backdrop.NativeAvailable ? Brushes.Transparent : Brush(Base(dark), 1));
        // Zero-alpha pixels on a layered HWND pass mouse input to the window behind it.
        Set(palette, "B.WindowInput", Backdrop.ClearAvailable ? Brush(Base(dark), DesktopTransparency.InputOpacity) : Brushes.Transparent);
		Set(palette, "B.Atmosphere", Brush(Base(dark), page));
		Set(palette, "B.Glass", Brush(tinted, page));
		Set(palette, "B.GlassStrong", Brush(Mix(tinted, paper, .16), page));
        Set(palette, "B.NativeChrome", Brush(Base(dark), page));
		Color controlTone = dark ? Mix(paper, Colors.White, 0.055) : Mix(paper, canvas, 0.85);
		Set(palette, "B.GlassBorder", Brush(dark ? Colors.White : Color.FromRgb(30, 28, 42), dark ? 0.08 : 0.08));
		Set(palette, "B.GlassEdge", Brush(Colors.White, dark ? 0.06 : 0.45));
		Set(palette, "B.GlassSheen", Brushes.Transparent);
		Set(palette, "B.ControlFill", Brush(controlTone, control));
		Set(palette, "B.Popup", Brush(paper, popup));
        Set(palette, "B.PopupInput", Brush(paper, DesktopTransparency.InputOpacity));
        Set(palette, "B.PopupShadow", Brush(paper, popup >= .999 ? 1 : 0));
        Set(palette, "B.SurfaceInset", Brush(Mix(paper, canvas, .55), card));
        Set(palette, "B.SurfaceEdge", Brush(dark ? Colors.White : Colors.Black, dark ? .04 : .035));
		Set(palette, "B.PopupBorder", Brush(dark ? Color.FromRgb(42, 48, 69) : Color.FromRgb(228, 226, 239), 1));
		Set(palette, "B.Card", Brush(paper, card));
		Set(palette, "B.CardBorder", Brush(dark ? Colors.White : Colors.Black, dark ? .04 : .04));
		Set(palette, "B.Sel", Brush(dark ? Colors.White : Colors.Black, dark ? 0.075 : 0.055));
		Set(palette, "B.Seam", Brush(dark ? Colors.White : Colors.Black, dark ? 0.04 : 0.04));
		Set(palette, "SEL.Alpha", dark ? 0.16 : 0.18);
		Set(palette, "B.SkinLight", new SolidColorBrush(Mix(paper, accent, 0.04)));
		Set(palette, "B.SkinDark", new SolidColorBrush(Mix(Color.FromRgb(20, 20, 20), accent, 0.06)));
        Set(palette, "B.PageWash", Brush(canvas, page));
        Set(palette, "B.WallpaperPageWash", Brush(Mix(canvas, paper, dark ? .24 : .72), page));
        Set(palette, "B.Canvas", Brush(canvas, page));
        Set(palette, "B.Side", Brush(Mix(sideSurface, accent, Tint / 100d * .012), page));
        Set(palette, "B.Chrome", Brush(Mix(chromeSurface, accent, Tint / 100d * .012), page));
        Set(palette, "B.Paper", Brush(paper, card));
        Set(palette, "B.Paper2", Brush(Mix(paper, canvas, .65), card));
        Set(palette, "B.AccentSoft", Brush(Mix(paper, accent, dark ? .20 : .10), card));
        Set(palette, "B.AccentSoft2", Brush(Mix(paper, accent, dark ? .28 : .17), card));
        Set(palette, "B.AccentTint", Brush(Mix(paper, accent, .07), card));
        Set(palette, "B.Mask", Brush(Colors.Black, (dark ? .54 : .34) * popup));
        Set(palette, "B.Hover", Brush(dark ? Colors.White : Colors.Black, dark ? .065 : .045));
        Set(palette, "B.Pressed", Brush(dark ? Colors.White : Colors.Black, dark ? .10 : .08));
			Set(palette, "B.LineSoft", material
			? Brush(dark ? Color.FromRgb(200, 195, 240) : Color.FromRgb(35, 31, 58), dark ? .07 : .05)
			: Brush(dark ? Color.FromRgb(28, 33, 48) : Color.FromRgb(236, 234, 245), 1));
        // A crisp text halo provides separation on a clear image or transparent dialog without adding a surface tint.
        palette["E.TextContrast"] = material && (Transparency >= 80 || PopupTransparency >= 70) && (Wallpaper.Active || Backdrop.ClearAvailable)
            ? new DropShadowEffect { Color = dark ? Colors.Black : Colors.White, BlurRadius = 3.5, ShadowDepth = 0, Opacity = .98, RenderingBias = RenderingBias.Quality }
            : null;
        RefreshInk(palette, Wallpaper.Active && material);
		App.RaiseThemeVisualsChanged();
	}

    private static void RefreshInk(ResourceDictionary palette, bool overWallpaper)
    {
        var surfaces = new[] { "B.WallpaperPageWash", "B.Glass", "B.GlassStrong" };
        foreach (string role in new[] { "Ink", "InkSoft", "InkMuted", "InkFaint", "AccentText", "AccentDeep" })
        {
            if (Read(palette, "C." + role) is not Color original) continue;
            Color ink = original;
            if (overWallpaper)
            {
                // The wallpaper can be a moving video; protect text against both extreme frames.
                Color limit = App.IsDark ? Colors.White : Colors.Black;
                for (int step = 0; step <= 100; step++)
                {
                    ink = Mix(original, limit, step / 100d);
                    if (surfaces.All(key => ContrastOn((SolidColorBrush)palette[key], ink) >= 4.6)) break;
                }
            }
            Set(palette, "B." + role, Brush(ink, 1));
        }
    }

    private static double ContrastOn(SolidColorBrush surface, Color ink)
    {
        double alpha = surface.Opacity * surface.Color.A / 255d;
        double contrast = double.MaxValue;
        foreach (Color backdrop in new[] { Colors.Black, Colors.White })
        {
            double a = Luminance(ink), b = Luminance(Mix(backdrop, surface.Color, alpha));
            contrast = Math.Min(contrast, (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05));
        }
        return contrast;
    }

    private static double Luminance(Color color)
    {
        double Channel(byte value) { double part = value / 255d; return part <= .04045 ? part / 12.92 : Math.Pow((part + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }

	/// <summary>系统材质不可用时的窗口底色。</summary>
	public static Color Base(bool dark)
	{
		ResourceDictionary palette = Palette;
		return palette != null && Read(palette, "C.Canvas") is Color canvas
			? canvas
			: dark ? Color.FromRgb(16, 19, 30) : Color.FromRgb(243, 243, 243);
	}

	/// <summary>铺自己壁纸时的压淡量。</summary>
	public static double WallDim(bool dark)
	{
		return dark ? _skin.WallDimDark : _skin.WallDimLight;
	}

	/// <summary>挑一个本机有的无衬线字体。</summary>
	public static FontFamily Font
	{
		get
		{
			foreach (string item in FontCandidates)
			{
				FontFamily fontFamily = new FontFamily(item);
				if (fontFamily.FamilyNames.Count > 0)
				{
					return fontFamily;
				}
			}
			return new FontFamily("Segoe UI");
		}
	}

	private static ResourceDictionary Palette => App.PaletteDictionary;

	private static object? Read(ResourceDictionary palette, string key)
	{
		return palette.Contains(key) ? palette[key] : null;
	}

	private static void Set(ResourceDictionary palette, string key, object value)
	{
		palette[key] = value;
	}

	private static SolidColorBrush Brush(Color color, double alpha)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(color);
		solidColorBrush.Opacity = Num(alpha, 0.0, 1.0);
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static double Num(double value, double min, double max)
	{
		return (value < min) ? min : ((value > max) ? max : value);
	}

	private static Color Mix(Color a, Color b, double t)
	{
		t = Num(t, 0.0, 1.0);
		return Color.FromRgb((byte)((double)(int)a.R + (double)(b.R - a.R) * t), (byte)((double)(int)a.G + (double)(b.G - a.G) * t), (byte)((double)(int)a.B + (double)(b.B - a.B) * t));
	}
}
