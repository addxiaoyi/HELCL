using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelL.Core.Platform;

namespace HelL.Core.Launcher.Ai;

public static class Images
{
	public const int MAX_EDGE = 1568;

	public const long MAX_BYTES = 4194304L;

	public const int MAX_PER_TURN = 4;

	private static readonly int[] _QUALITY_LADDER = new int[4] { 85, 75, 65, 55 };

	private static bool StartsWith(byte[] d, params byte[] p)
	{
		if (d.Length < p.Length)
		{
			return false;
		}
		for (int i = 0; i < p.Length; i++)
		{
			if (d[i] != p[i])
			{
				return false;
			}
		}
		return true;
	}

	public static string SniffMime(byte[] data)
	{
		if (data == null || data.Length == 0)
		{
			return null;
		}
		if (StartsWith(data, 137, 80, 78, 71, 13, 10, 26, 10))
		{
			return "image/png";
		}
		if (StartsWith(data, 255, 216, 255))
		{
			return "image/jpeg";
		}
		if (StartsWith(data, 71, 73, 70, 56, 55, 97) || StartsWith(data, 71, 73, 70, 56, 57, 97))
		{
			return "image/gif";
		}
		if (StartsWith(data, 66, 77))
		{
			return "image/bmp";
		}
		if (data.Length > 12 && StartsWith(data, 82, 73, 70, 70) && data[8] == 87 && data[9] == 69 && data[10] == 66 && data[11] == 80)
		{
			return "image/webp";
		}
		return null;
	}

	private static (byte[], string) _shrink(byte[] data, string mime, int maxEdge, long maxBytes)
	{
		IImageCodec imageCodec = PlatformServices.ImageCodec;
		if (imageCodec == null)
		{
			return (data, mime);
		}
		IDecodedImage decodedImage;
		try
		{
			decodedImage = imageCodec.Decode(data);
		}
		catch (Exception ex)
		{
			throw new ImageError("图片无法解析：" + Py.ExcStr(ex), ex);
		}
		try
		{
			bool flag = false;
			int width = decodedImage.Width;
			int height = decodedImage.Height;
			if (Math.Max(width, height) > maxEdge)
			{
				double num = (double)maxEdge / (double)Math.Max(width, height);
				IDecodedImage decodedImage2 = decodedImage.Resize(Math.Max(1, (int)((double)width * num)), Math.Max(1, (int)((double)height * num)));
				decodedImage.Dispose();
				decodedImage = decodedImage2;
				flag = true;
			}
			if (decodedImage.HasAlpha)
			{
				byte[] array = decodedImage.EncodePng();
				if (array.Length <= maxBytes || !flag)
				{
					return (array.Length < data.Length || flag) ? (array, "image/png") : (data, mime);
				}
				return (array, "image/png");
			}
			byte[] array2 = null;
			int[] qUALITY_LADDER = _QUALITY_LADDER;
			foreach (int quality in qUALITY_LADDER)
			{
				byte[] array3 = decodedImage.EncodeJpeg(quality);
				if (array2 == null || array3.Length < array2.Length)
				{
					array2 = array3;
				}
				if (array3.Length <= maxBytes)
				{
					array2 = array3;
					break;
				}
			}
			if (array2 != null && (flag || array2.Length < data.Length))
			{
				return (array2, "image/jpeg");
			}
			return (data, mime);
		}
		finally
		{
			decodedImage.Dispose();
		}
	}

	public static string EncodeBytes(byte[] data, string mime = null, int maxEdge = 1568, long maxBytes = 4194304L)
	{
		if (data == null || data.Length == 0)
		{
			throw new ImageError("图片是空的");
		}
		mime = mime ?? SniffMime(data);
		if (mime == null)
		{
			throw new ImageError("不是支持的图片格式（支持 PNG / JPEG / GIF / WebP / BMP）");
		}
		if (data.Length > maxBytes || maxEdge != 0)
		{
			(data, mime) = _shrink(data, mime, maxEdge, maxBytes);
		}
		if (data.Length > maxBytes)
		{
			throw new ImageError("图片压缩后仍超过 " + maxBytes / 1048576 + "MB，请先裁剪或降低分辨率再发");
		}
		return "data:" + mime + ";base64," + Convert.ToBase64String(data);
	}

	public static string EncodeFile(string path)
	{
		byte[] data;
		try
		{
			data = File.ReadAllBytes(PPath.Abs(path));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			string text = ((ex is FileNotFoundException || ex is DirectoryNotFoundException) ? "No such file or directory" : ((ex is UnauthorizedAccessException) ? "Permission denied" : Py.ExcStr(ex)));
			throw new ImageError("读不了这张图（" + text + "）", ex);
		}
		return EncodeBytes(data, null, 1568, 4194304L);
	}

	public static PyDict MakeBlock(string url, string detail = null)
	{
		PyDict pyDict = PyDict.Of("url", url);
		if (!string.IsNullOrEmpty(detail))
		{
			pyDict["detail"] = detail;
		}
		return PyDict.Of("type", "image_url", "image_url", pyDict);
	}

	public static object BuildUserContent(string text, IEnumerable<object> images)
	{
		List<object> list = (images ?? new List<object>()).Where(Py.Truthy).ToList();
		text = text ?? "";
		if (list.Count == 0)
		{
			return text;
		}
		List<object> list2 = new List<object>();
		if (Py.Strip(text).Length > 0)
		{
			list2.Add(PyDict.Of("type", "text", "text", text));
		}
		foreach (object item in list)
		{
			list2.Add(MakeBlock(Py.Str(item)));
		}
		return list2;
	}

	public static object StripImages(object content)
	{
		if (!(content is List<object> list))
		{
			return content;
		}
		List<string> list2 = new List<string>();
		int num = 0;
		foreach (object item in list)
		{
			if (item is PyDict pyDict)
			{
				if (Py.Eq(pyDict.Get("type"), "text"))
				{
					list2.Add(Py.S(pyDict.Get("text")));
				}
				else if (Py.Eq(pyDict.Get("type"), "image_url"))
				{
					num++;
				}
			}
		}
		string text = string.Join("\n", list2.Where((string t) => Py.Strip(t).Length > 0));
		if (num > 0)
		{
			string text2 = "（此处原有 " + num + " 张图片，已省略）";
			text = ((text.Length > 0) ? Py.Strip(text + "\n" + text2) : text2);
		}
		return text;
	}

	public static object StripMessage(object m0)
	{
		if (!(m0 is PyDict pyDict))
		{
			return m0;
		}
		PyDict pyDict2 = pyDict.Copy();
		object obj = pyDict2.Pop("images");
		object obj2 = StripImages(pyDict2.Get("content"));
		if (Py.Truthy(obj) && obj2 is string text)
		{
			string text2 = "（此处原有 " + ((obj is List<object> list) ? list.Count : ((obj is PyDict pyDict3) ? pyDict3.Count : PyDifflib.PyLen(Py.Str(obj)))) + " 张图片，已省略）";
			obj2 = ((Py.Strip(text).Length > 0) ? Py.Strip(text + "\n" + text2) : text2);
		}
		pyDict2["content"] = obj2;
		return pyDict2;
	}
}
