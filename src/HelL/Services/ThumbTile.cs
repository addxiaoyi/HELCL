using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HelL.Services;

public sealed class ThumbTile : Border
{
	private readonly Image _img = new Image
	{
		Stretch = Stretch.UniformToFill
	};

	private readonly TextBlock _letter;

	private int _token;

	public ThumbTile(string text, double size = 42.0, double radius = 10.0)
	{
		base.Width = (base.Height = size);
		base.CornerRadius = new CornerRadius(radius);
		base.ClipToBounds = true;
		SetResourceReference(Border.BackgroundProperty, "B.AccentSoft");
		_letter = Ui.Txt(string.IsNullOrEmpty(text) ? "?" : text.Substring(0, 1).ToUpperInvariant(), Math.Max(12.0, size * 0.4), bold: true, "B.AccentDeep").Center();
		_letter.VerticalAlignment = VerticalAlignment.Center;
		Grid child = new Grid
		{
			Children = 
			{
				(UIElement)_letter,
				(UIElement)_img
			}
		};
		_img.Visibility = Visibility.Collapsed;
		Child = child;
	}

	public void SetUrl(string? url)
	{
		int token = ++_token;
		int px = (int)Math.Max(16.0, base.Width);
		if (string.IsNullOrWhiteSpace(url))
		{
			Show(null);
			return;
		}
		BitmapSource bitmapSource = Thumbs.Cached(url, px);
		if (bitmapSource != null)
		{
			Show(bitmapSource);
			return;
		}
		Show(null);
		_ = LoadAsync(url, px, token);
	}

	private async Task LoadAsync(string url, int px, int token)
	{
		try
		{
			BitmapSource bmp = await Thumbs.LoadAsync(url, px);
			if (bmp == null || token != _token)
			{
				return;
			}
			await base.Dispatcher.InvokeAsync(delegate
			{
				if (token == _token)
				{
					Show(bmp);
					Motion.Fade(_img, 1.0, 180.0);
				}
			});
		}
		catch (Exception ex)
		{
			// 缩略图是装饰性的，加载失败只留痕，不打扰用户
			Smoke.Note("thumb", ex.ToString());
		}
	}

	private void Show(BitmapSource? bmp)
	{
		_img.Source = bmp;
		_img.Opacity = ((bmp != null) ? 1 : 0);
		_img.Visibility = ((bmp == null) ? Visibility.Collapsed : Visibility.Visible);
		_letter.Visibility = ((bmp != null) ? Visibility.Collapsed : Visibility.Visible);
	}
}
