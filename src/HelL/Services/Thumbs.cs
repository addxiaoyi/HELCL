using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace HelL.Services;

public static class Thumbs
{
	private const int MaxParallel = 8;

	private static readonly ConcurrentDictionary<string, BitmapSource> _mem = new ConcurrentDictionary<string, BitmapSource>();

	private static readonly ConcurrentDictionary<string, DateTime> _failed = new ConcurrentDictionary<string, DateTime>();

	private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> _inflight = new ConcurrentDictionary<string, Task<BitmapSource>>();

	private static readonly SemaphoreSlim _gate = new SemaphoreSlim(8, 8);

	private static readonly TimeSpan FailCooldown = TimeSpan.FromMinutes(5.0);

	private static string Key(string url, int px)
	{
		return url + "|" + px;
	}

	public static BitmapSource? Cached(string url, int px)
	{
		if (!string.IsNullOrEmpty(url))
		{
			return _mem.GetValueOrDefault(Key(url, px));
		}
		return null;
	}

	public static Task<BitmapSource?> LoadAsync(string url, int px)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return Task.FromResult<BitmapSource>(null);
		}
		string key = Key(url, px);
		if (_mem.TryGetValue(key, out BitmapSource value))
		{
			return Task.FromResult(value);
		}
		if (_failed.TryGetValue(url, out var value2))
		{
			if (DateTime.UtcNow - value2 < FailCooldown)
			{
				return Task.FromResult<BitmapSource>(null);
			}
			_failed.TryRemove(url, out var _);
		}
		return _inflight.GetOrAdd(key, (string _) => FetchAsync(url, px, key));
	}

	private static async Task<BitmapSource?> FetchAsync(string url, int px, string key)
	{
		_ = 4;
		try
		{
			string path = (await AppServices.Client.TryCallAsync("thumb_path", new { url }, "")) ?? "";
			BitmapSource bitmapSource = await DecodeAsync(path, px);
			if (bitmapSource == null)
			{
				await _gate.WaitAsync().ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					path = (await AppServices.Client.TryCallAsync("ensure_thumb", new { url }, "")) ?? "";
				}
				finally
				{
					_gate.Release();
				}
				bitmapSource = await DecodeAsync(path, px);
			}
			if (bitmapSource == null)
			{
				_failed[url] = DateTime.UtcNow;
				return null;
			}
			_mem[key] = bitmapSource;
			if (_mem.Count > 400)
			{
				Trim();
			}
			return bitmapSource;
		}
		catch
		{
			_failed[url] = DateTime.UtcNow;
			return null;
		}
		finally
		{
			_inflight.TryRemove(key, out Task<BitmapSource> _);
		}
	}

	private static Task<BitmapSource?> DecodeAsync(string path, int px)
	{
		return Task.Run(delegate
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return (BitmapSource)null;
			}
			try
			{
				byte[] array = File.ReadAllBytes(path);
				if (array.Length == 0)
				{
					return (BitmapSource)null;
				}
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
				bitmapImage.DecodePixelWidth = Math.Max(16, px * 2);
				bitmapImage.StreamSource = new MemoryStream(array);
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				return bitmapImage;
			}
			catch
			{
				return (BitmapSource)null;
			}
		});
	}

	private static void Trim()
	{
		foreach (string item in _mem.Keys.Take(_mem.Count - 300))
		{
			_mem.TryRemove(item, out BitmapSource _);
		}
	}
}
