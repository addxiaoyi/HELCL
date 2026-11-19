using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HelL.Pages;

internal static class SkinPng
{
	public static byte[]? Decode(string? dataUrl)
	{
		if (string.IsNullOrWhiteSpace(dataUrl))
		{
			return null;
		}
		string s = (dataUrl.StartsWith("data:", StringComparison.Ordinal) ? dataUrl.Substring(dataUrl.IndexOf(',') + 1) : dataUrl);
		try
		{
			return Convert.FromBase64String(s);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	public static BitmapSource? FrontView(byte[] png, bool slim, HelL.Services.SkinLayerToggles toggles = null)
	{
		toggles ??= new HelL.Services.SkinLayerToggles();
		BitmapSource src = Load(png);
		if (src == null)
		{
			return null;
		}
		double scale = (double)src.PixelWidth / 64.0;
		if (scale <= 0.0)
		{
			return null;
		}
		bool flag = src.PixelHeight * 2 <= src.PixelWidth;
		int num = (slim ? 3 : 4);
		DrawingVisual drawingVisual = new DrawingVisual();
		RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.NearestNeighbor);
		DrawingContext dc = drawingVisual.RenderOpen();
		try
		{
			Part(8, 8, 8, 8, 4.0, 0.0);
			Part(20, 20, 8, 12, 4.0, 8.0);
			Part(44, 20, num, 12, 4 - num, 8.0);
			if (flag)
			{
				Part(44, 20, num, 12, 12.0, 8.0, mirror: true);
			}
			else
			{
				Part(36, 52, num, 12, 12.0, 8.0);
			}
			Part(4, 20, 4, 12, 4.0, 20.0);
			if (flag)
			{
				Part(4, 20, 4, 12, 8.0, 20.0, mirror: true);
			}
			else
			{
				Part(20, 52, 4, 12, 8.0, 20.0);
			}
			if (toggles.Hat)
			{
				Part(40, 8, 8, 8, 4.0, 0.0);
			}
			if (!flag)
			{
				if (toggles.Jacket)
				{
					Part(20, 36, 8, 12, 4.0, 8.0);
				}
				if (toggles.RightSleeve)
				{
					Part(44, 36, num, 12, 4 - num, 8.0);
				}
				if (toggles.LeftSleeve)
				{
					Part(52, 52, num, 12, 12.0, 8.0);
				}
				if (toggles.RightPants)
				{
					Part(4, 36, 4, 12, 4.0, 20.0);
				}
				if (toggles.LeftPants)
				{
					Part(4, 52, 4, 12, 8.0, 20.0);
				}
			}
		}
		finally
		{
			if (dc != null)
			{
				((IDisposable)dc).Dispose();
			}
		}
		DrawingVisual drawingVisual2 = new DrawingVisual();
		RenderOptions.SetBitmapScalingMode(drawingVisual2, BitmapScalingMode.NearestNeighbor);
		using (DrawingContext drawingContext = drawingVisual2.RenderOpen())
		{
			drawingContext.PushTransform(new ScaleTransform(8.0, 8.0));
			drawingContext.DrawDrawing(drawingVisual.Drawing);
			drawingContext.Pop();
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(128, 256, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual2);
		renderTargetBitmap.Freeze();
		return renderTargetBitmap;
		void Part(int sx, int sy, int sw, int sh, double dx, double dy, bool mirror = false)
		{
			BitmapSource bitmapSource = Crop(src, sx, sy, sw, sh, scale);
			if (bitmapSource != null)
			{
				Rect rectangle = new Rect(dx, dy, sw, sh);
				if (mirror)
				{
					dc.PushTransform(new ScaleTransform(-1.0, 1.0, dx + (double)sw / 2.0, 0.0));
					dc.DrawImage(bitmapSource, rectangle);
					dc.Pop();
				}
				else
				{
					dc.DrawImage(bitmapSource, rectangle);
				}
			}
		}
	}

	public static BitmapSource? BackView(byte[] png, bool slim, HelL.Services.SkinLayerToggles toggles = null)
	{
		toggles ??= new HelL.Services.SkinLayerToggles();
		BitmapSource src = Load(png);
		if (src == null)
		{
			return null;
		}
		double scale = (double)src.PixelWidth / 64.0;
		if (scale <= 0.0)
		{
			return null;
		}
		bool flag = src.PixelHeight * 2 <= src.PixelWidth;
		int num = (slim ? 3 : 4);
		DrawingVisual drawingVisual = new DrawingVisual();
		RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.NearestNeighbor);
		DrawingContext dc = drawingVisual.RenderOpen();
		try
		{
			Part(24, 8, 8, 8, 4.0, 0.0);
			Part(32, 20, 8, 12, 4.0, 8.0);
			Part(52, 20, num, 12, 4 - num, 8.0);
			if (flag)
			{
				Part(52, 20, num, 12, 12.0, 8.0, mirror: true);
			}
			else
			{
				Part(44, 52, num, 12, 12.0, 8.0);
			}
			Part(12, 20, 4, 12, 4.0, 20.0);
			if (flag)
			{
				Part(12, 20, 4, 12, 8.0, 20.0, mirror: true);
			}
			else
			{
				Part(28, 52, 4, 12, 8.0, 20.0);
			}
			if (toggles.Hat)
			{
				Part(56, 8, 8, 8, 4.0, 0.0);
			}
			if (!flag)
			{
				if (toggles.Jacket)
				{
					Part(32, 36, 8, 12, 4.0, 8.0);
				}
				if (toggles.RightSleeve)
				{
					Part(52, 36, num, 12, 4 - num, 8.0);
				}
				if (toggles.LeftSleeve)
				{
					Part(60, 52, num, 12, 12.0, 8.0);
				}
				if (toggles.RightPants)
				{
					Part(12, 36, 4, 12, 4.0, 20.0);
				}
				if (toggles.LeftPants)
				{
					Part(12, 52, 4, 12, 8.0, 20.0);
				}
			}
		}
		finally
		{
			if (dc != null)
			{
				((IDisposable)dc).Dispose();
			}
		}
		DrawingVisual drawingVisual2 = new DrawingVisual();
		RenderOptions.SetBitmapScalingMode(drawingVisual2, BitmapScalingMode.NearestNeighbor);
		using (DrawingContext drawingContext = drawingVisual2.RenderOpen())
		{
			drawingContext.PushTransform(new ScaleTransform(8.0, 8.0));
			drawingContext.DrawDrawing(drawingVisual.Drawing);
			drawingContext.Pop();
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(128, 256, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual2);
		renderTargetBitmap.Freeze();
		return renderTargetBitmap;
		void Part(int sx, int sy, int sw, int sh, double dx, double dy, bool mirror = false)
		{
			BitmapSource bitmapSource = Crop(src, sx, sy, sw, sh, scale);
			if (bitmapSource != null)
			{
				Rect rectangle = new Rect(dx, dy, sw, sh);
				if (mirror)
				{
					dc.PushTransform(new ScaleTransform(-1.0, 1.0, dx + (double)sw / 2.0, 0.0));
					dc.DrawImage(bitmapSource, rectangle);
					dc.Pop();
				}
				else
				{
					dc.DrawImage(bitmapSource, rectangle);
				}
			}
		}
	}

	private static BitmapSource? Load(byte[] png)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bitmapImage.StreamSource = new MemoryStream(png);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return (bitmapImage.PixelWidth >= 64) ? bitmapImage : null;
		}
		catch
		{
			return null;
		}
	}

	private static BitmapSource? Crop(BitmapSource src, int x, int y, int w, int h, double scale)
	{
		Int32Rect sourceRect = new Int32Rect((int)((double)x * scale), (int)((double)y * scale), (int)((double)w * scale), (int)((double)h * scale));
		if (sourceRect.X < 0 || sourceRect.Y < 0 || sourceRect.Width <= 0 || sourceRect.Height <= 0 || sourceRect.X + sourceRect.Width > src.PixelWidth || sourceRect.Y + sourceRect.Height > src.PixelHeight)
		{
			return null;
		}
		CroppedBitmap croppedBitmap = new CroppedBitmap(src, sourceRect);
		croppedBitmap.Freeze();
		return croppedBitmap;
	}
}
