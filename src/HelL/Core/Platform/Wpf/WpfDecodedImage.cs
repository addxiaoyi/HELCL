using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HelL.Core.Platform.Wpf;

internal sealed class WpfDecodedImage : IDecodedImage, IDisposable
{
	private BitmapSource _src;

	private readonly bool _alpha;

	public int Width => _src.PixelWidth;

	public int Height => _src.PixelHeight;

	public bool HasAlpha => _alpha;

	internal WpfDecodedImage(BitmapSource src, bool alpha)
	{
		_src = src;
		_alpha = alpha;
	}

	public IDecodedImage Resize(int width, int height)
	{
		BitmapSource src = _src;
		bool alpha = _alpha;
		return WpfImageCodec.RunSta((Func<IDecodedImage>)delegate
		{
			DrawingVisual drawingVisual = new DrawingVisual();
			RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.HighQuality);
			using (DrawingContext drawingContext = drawingVisual.RenderOpen())
			{
				drawingContext.DrawImage(src, new Rect(0.0, 0.0, width, height));
			}
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(width, height, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap.Render(drawingVisual);
			renderTargetBitmap.Freeze();
			BitmapSource src2 = renderTargetBitmap;
			if (!alpha)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(renderTargetBitmap, PixelFormats.Bgr24, null, 0.0);
				formatConvertedBitmap.Freeze();
				src2 = formatConvertedBitmap;
			}
			return new WpfDecodedImage(src2, alpha);
		});
	}

	public byte[] EncodePng()
	{
		BitmapSource src = _src;
		return WpfImageCodec.RunSta(delegate
		{
			PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder
			{
				Interlace = PngInterlaceOption.Off
			};
			pngBitmapEncoder.Frames.Add(BitmapFrame.Create(src));
			using MemoryStream memoryStream = new MemoryStream();
			pngBitmapEncoder.Save(memoryStream);
			return memoryStream.ToArray();
		});
	}

	public byte[] EncodeJpeg(int quality)
	{
		BitmapSource src = _src;
		int q = Math.Max(1, Math.Min(100, quality));
		return WpfImageCodec.RunSta(delegate
		{
			BitmapSource bitmapSource = src;
			if (bitmapSource.Format != PixelFormats.Bgr24 && bitmapSource.Format != PixelFormats.Gray8)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgr24, null, 0.0);
				formatConvertedBitmap.Freeze();
				bitmapSource = formatConvertedBitmap;
			}
			JpegBitmapEncoder jpegBitmapEncoder = new JpegBitmapEncoder
			{
				QualityLevel = q
			};
			jpegBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmapSource));
			using MemoryStream memoryStream = new MemoryStream();
			jpegBitmapEncoder.Save(memoryStream);
			return memoryStream.ToArray();
		});
	}

	public void Dispose()
	{
		_src = null;
	}
}
