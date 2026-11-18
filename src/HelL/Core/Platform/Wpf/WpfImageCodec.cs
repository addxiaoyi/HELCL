using System;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HelL.Core.Platform.Wpf;

public sealed class WpfImageCodec : IImageCodec
{
	internal static T RunSta<T>(Func<T> fn)
	{
		T result = default(T);
		Exception error = null;
		Thread thread = new Thread((ThreadStart)delegate
		{
			try
			{
				result = fn();
			}
			catch (Exception ex)
			{
				error = ex;
			}
		});
		thread.IsBackground = true;
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		if (error != null)
		{
			throw error;
		}
		return result;
	}

	public IDecodedImage Decode(byte[] data)
	{
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		return RunSta((Func<IDecodedImage>)delegate
		{
			ushort o = 1;
			BitmapSource src;
			using (MemoryStream bitmapStream = new MemoryStream(data, writable: false))
			{
				BitmapDecoder bitmapDecoder = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
				if (bitmapDecoder.Frames.Count == 0)
				{
					throw new InvalidDataException("cannot identify image file");
				}
				BitmapFrame bitmapFrame = bitmapDecoder.Frames[0];
				o = ReadOrientation(bitmapFrame.Metadata as BitmapMetadata);
				src = bitmapFrame;
			}
			bool alpha = DetectAlpha(src);
			src = ApplyOrientation(src, o);
			if (src.CanFreeze && !src.IsFrozen)
			{
				src.Freeze();
			}
			return new WpfDecodedImage(src, alpha);
		});
	}

	private static ushort ReadOrientation(BitmapMetadata md)
	{
		if (md == null)
		{
			return 1;
		}
		string[] array = new string[2] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" };
		foreach (string query in array)
		{
			try
			{
				if (md.ContainsQuery(query))
				{
					object query2 = md.GetQuery(query);
					if (query2 is ushort result)
					{
						return result;
					}
					if (query2 != null && ushort.TryParse(Convert.ToString(query2), out var result2))
					{
						return result2;
					}
				}
			}
			catch (Exception)
			{
			}
		}
		return 1;
	}

	private static BitmapSource ApplyOrientation(BitmapSource src, ushort o)
	{
		Transform newTransform;
		switch (o)
		{
		case 2:
			newTransform = new ScaleTransform(-1.0, 1.0);
			break;
		case 3:
			newTransform = new RotateTransform(180.0);
			break;
		case 4:
			newTransform = new ScaleTransform(1.0, -1.0);
			break;
		case 5:
			newTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)new RotateTransform(90.0),
					(Transform)new ScaleTransform(-1.0, 1.0)
				}
			};
			break;
		case 6:
			newTransform = new RotateTransform(90.0);
			break;
		case 7:
			newTransform = new TransformGroup
			{
				Children = 
				{
					(Transform)new RotateTransform(270.0),
					(Transform)new ScaleTransform(-1.0, 1.0)
				}
			};
			break;
		case 8:
			newTransform = new RotateTransform(270.0);
			break;
		default:
			return src;
		}
		TransformedBitmap transformedBitmap = new TransformedBitmap(src, newTransform);
		transformedBitmap.Freeze();
		return transformedBitmap;
	}

	private static bool DetectAlpha(BitmapSource src)
	{
		PixelFormat format = src.Format;
		if (format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Rgba64 || format == PixelFormats.Prgba64 || format == PixelFormats.Rgba128Float || format == PixelFormats.Prgba128Float)
		{
			return true;
		}
		BitmapPalette palette = src.Palette;
		if (palette != null)
		{
			foreach (Color color in palette.Colors)
			{
				if (color.A < byte.MaxValue)
				{
					return true;
				}
			}
		}
		return false;
	}
}
