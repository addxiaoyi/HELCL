using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace HelL;

/// <summary>
/// 玻璃皮肤参数。
/// 资源字典 Themes/Skin.*.xaml 里的 S.* 键在运行时逐个灌进这里；
/// 缺少的键就用属性初始化器里的默认值——macOS 字典因此可以只声明它要改的那几项。
/// </summary>
public sealed class GlassSkin
{
	public const string Win11 = "win11";

	public const string MacOS = "macos";

	private const string Prefix = "S.";

	/// <summary>标题栏高度。</summary>
	public double Top { get; set; } = 40.0;

	/// <summary>窗体外圆角，0 = 交给系统。</summary>
	public double Radius { get; set; } = 18.0;

	/// <summary>玻璃面板离底座的缝。</summary>
	public double Frame { get; set; } = 0.0;

	/// <summary>内容区右侧 / 底部的留白。</summary>
	public double Gap { get; set; } = 0.0;

	/// <summary>dock 的左右内缩。</summary>
	public double DockInset { get; set; } = 16.0;

	/// <summary>卡片的整体不透明度。</summary>
	public double CardAlpha { get; set; } = 0.68;

	/// <summary>侧栏的不透明度。</summary>
	public double SideAlpha { get; set; } = 0.56;

	/// <summary>标题栏的不透明度。</summary>
	public double ChromeAlpha { get; set; } = 0.82;

	/// <summary>弹出层的不透明度。</summary>
	public double PopupAlpha { get; set; } = 0.92;

	/// <summary>铺自己壁纸时的压淡量（浅色）。</summary>
	public double WallDimLight { get; set; } = 0.1;

	/// <summary>铺自己壁纸时的压暗量（深色）。</summary>
	public double WallDimDark { get; set; } = 0.16;

	/// <summary>着色强度，0–100。</summary>
	public double Tint { get; set; } = 62.0;

	/// <summary>Mica 拿不到时退到 Acrylic。</summary>
	public bool Acrylic { get; set; } = true;

	/// <summary>"bar" Win11 码表；"dot" macOS 红黄绿。</summary>
	public string Caption { get; set; } = "bar";

	/// <summary>"bar" 左侧竖条；"capsule" 整条胶囊。</summary>
	public string Nav { get; set; } = "bar";

	/// <summary>导航项是否显示图标。</summary>
	public bool NavIcon { get; set; } = false;

	/// <summary>换页是否有动画。</summary>
	public bool Fly { get; set; } = true;

	/// <summary>导航项左右内距。胶囊皮肤要更宽，文字才不贴圆角。</summary>
	public double PadX { get; set; } = 8.0;

	/// <summary>拖拽条在侧栏面板左右两侧露出的宽度。</summary>
	public double Rest { get; set; } = 4.0;

	/// <summary>标题栏左端距。mac 皮肤要和悬浮侧栏的图标对齐。</summary>
	public double Lead { get; set; } = 14.0;

	/// <summary>侧栏面板和内容区之间的间隙。</summary>
	public double Tab { get; set; } = 0.0;

	/// <summary>侧栏磨砂层的模糊半径。</summary>
	public double FrostRound { get; set; } = 30.0;

	private static readonly HashSet<string> KnownKeys = new HashSet<string>(StringComparer.Ordinal)
	{
		"Top", "Radius", "Frame", "Gap", "DockInset", "CardAlpha", "SideAlpha", "ChromeAlpha", "PopupAlpha",
		"WallDimLight", "WallDimDark", "Tint", "Acrylic", "Caption", "Nav", "NavIcon", "Fly",
		"PadX", "Rest", "Lead", "Tab", "FrostRound"
	};

	public static GlassSkin Read(string keyFound)
	{
		GlassSkin skin = new GlassSkin();
		try
		{
			ResourceDictionary dictionary = new ResourceDictionary
			{
				Source = new Uri("Themes/Skin." + keyFound + ".xaml", UriKind.Relative)
			};
			foreach (object obj in dictionary.Keys)
			{
				if (obj is not string text || !text.StartsWith(Prefix, StringComparison.Ordinal))
				{
					continue;
				}
				string name = text.Substring(2);
				if (!KnownKeys.Contains(name))
				{
					continue;
				}
				try
				{
					object value = dictionary[obj];
					var prop = typeof(GlassSkin).GetProperty(name);
					if (prop == null || !prop.CanWrite)
					{
						continue;
					}
					if (prop.PropertyType == typeof(double) && value is double d)
					{
						prop.SetValue(skin, d);
					}
					else if (prop.PropertyType == typeof(double) && (value is int || value is long || value is float || value is decimal || value is double) && double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d2))
					{
						prop.SetValue(skin, d2);
					}
					else if (prop.PropertyType == typeof(bool) && value is bool b)
					{
						prop.SetValue(skin, b);
					}
					else if (prop.PropertyType == typeof(bool) && bool.TryParse(Convert.ToString(value), out bool b2))
					{
						prop.SetValue(skin, b2);
					}
					else if (prop.PropertyType == typeof(string) && value is string s)
					{
						prop.SetValue(skin, s);
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return skin;
	}
}
