using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HelL.Services;

public static partial class Wallpaper
{
	private sealed class Playlist
	{
		private string _folder = "";

		private bool _shuffle;

		private List<string> _files = new List<string>();

		private string _current = "";

		private readonly Random _rng = new Random();

		public bool SetFolder(string folder, bool shuffle)
		{
			folder = (folder ?? "").Trim();
			_shuffle = shuffle;
			if (folder != _folder)
			{
				_folder = folder;
				_current = "";
			}
			Scan();
			return _files.Count > 0;
		}

		private void Scan()
		{
			if (_folder.Length == 0 || !Directory.Exists(_folder))
			{
				_files = new List<string>();
				return;
			}
			try
			{
				_files = Directory.EnumerateFiles(_folder).Where(FileKinds.IsWallpaper).OrderBy<string, string>((string p) => p, StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
			catch
			{
				_files = new List<string>();
			}
		}

		public string Current()
		{
			if (_current.Length <= 0 || !File.Exists(_current))
			{
				return Advance();
			}
			return _current;
		}

		public string Advance()
		{
			Scan();
			if (_files.Count == 0)
			{
				return _current = "";
			}
			if (_files.Count == 1)
			{
				return _current = _files[0];
			}
			if (_shuffle)
			{
				string text;
				do
				{
					text = _files[_rng.Next(_files.Count)];
				}
				while (text == _current);
				return _current = text;
			}
			int num = _files.IndexOf(_current);
			return _current = _files[((num >= 0) ? (num + 1) : 0) % _files.Count];
		}
	}

	private static Grid? _host;
    private static readonly BlurEffect SharedVideoBlur = new() { KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
    internal static bool SharedVideoFrost => _video != null && _optimizeVideo && SideFrost.Enabled && Backdrop.EffectsEnabled && Glass.Frost > 0;

	private static Border? _chromeBar;

	private static Border? _side;

	private static Rectangle? _wash;
    private static WallpaperMaterial? _pageMaterial;
    private static bool _materialWallpaper;
    private static WallpaperMaterial? _chromeMaterial;

	private static readonly Image _still = new Image
	{
		Stretch = Stretch.UniformToFill
	};

	private static MediaElement? _video;

	private static readonly Rectangle _dim = new Rectangle();

	private static readonly DispatcherTimer _rotate = new DispatcherTimer();

	private static readonly Playlist _playlist = new Playlist();

	private static string _source = "";

	private static int _blur;

	private static int _dimPercent;

	private static volatile bool _windowActive = true;
    private static volatile bool _windowMoving;
    private static bool _rotateEnabled;
    public static bool PlaybackAllowed => _windowActive && !_windowMoving;

	public static string Error { get; private set; } = "";

	public static bool Active => !string.IsNullOrEmpty(_source);

	public static void Attach(Grid host, Border chromeBar, Border side, Rectangle wash)
	{
		_host = host;
		_chromeBar = chromeBar;
		_side = side;
		_wash = wash;
        if (wash.Parent is Grid page) _pageMaterial = new WallpaperMaterial(page, host);
        if (chromeBar.Child is UIElement chrome)
        {
            chromeBar.Child = null;
            var layer = new Grid { ClipToBounds = true };
            _chromeMaterial = new WallpaperMaterial(layer, host);
            var tint = new Rectangle { IsHitTestVisible = false };
            tint.SetResourceReference(Shape.FillProperty, "B.GlassStrong");
            tint.Tag = "chrome-material-tint";
            layer.Children.Add(tint); layer.Children.Add(chrome); chromeBar.Child = layer;
        }
		_still.Visibility = Visibility.Collapsed;
		_dim.Visibility = Visibility.Collapsed;
		_dim.IsHitTestVisible = false;
		_still.IsHitTestVisible = false;
		RenderOptions.SetBitmapScalingMode(_still, BitmapScalingMode.HighQuality);
		host.Children.Add(_still);
		host.Children.Add(_dim);
		App.ThemeVisualsChanged -= OnThemeChanged;
		App.ThemeVisualsChanged += OnThemeChanged;
		_rotate.Tick += delegate
		{
			Advance();
		};
	}

	public static void SetWindowActive(bool active)
	{
		_windowActive = active;
        SyncActivity();
	}

    public static void SetWindowMoving(bool moving)
    {
        _windowMoving = moving;
        SyncActivity();
    }

    private static void SyncActivity()
    {
        bool active = PlaybackAllowed;
        if (_engineCapture != null) _engineCapture.Active = active;
        SyncPlayback();
        if (active && _rotateEnabled) _rotate.Start(); else _rotate.Stop();
        if (active && (_syncMode == "engine" || _syncMode == "windows")) _sync.Start(); else _sync.Stop();
    }

	public static async Task ReloadAsync()
	{
		if (_host != null && AppServices.Ready)
		{
			BridgeClient api = AppServices.Client;
            int transparency = await api.CallAsync<int>("get_setting", new { key = "ui_glass_transparency", @default = 100 });
            int frost = await api.CallAsync<int>("get_setting", new { key = "ui_glass_frost", @default = 30 });
            int cards = await api.CallAsync<int>("get_setting", new { key = "ui_glass_cards", @default = -1 });
            int controls = await api.CallAsync<int>("get_setting", new { key = "ui_glass_controls", @default = -1 });
            int popups = await api.CallAsync<int>("get_setting", new { key = "ui_glass_popups", @default = -1 });
            Glass.SetMaterial(transparency, frost, cards, controls, popups);
            int cacheMiB = await api.CallAsync<int>("get_setting", new { key = "ui_wallpaper_cache_mib", @default = 512 });
            int cacheDays = await api.CallAsync<int>("get_setting", new { key = "ui_wallpaper_cache_days", @default = 0 });
            SetCachePolicy(new HelL.Core.Launcher.WallpaperCachePolicy(Math.Max(256, Math.Min(4096, cacheMiB)), Math.Max(0, Math.Min(365, cacheDays))));
            try { await Task.Run(() => new HelL.Core.Launcher.WallpaperVideoCache(System.IO.Path.Combine(HelL.Core.Launcher.Utils.ROOT, "cache", "wallpaper-video"), CachePolicy).EnforcePolicy()); }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException) { Smoke.Note("wallpaper-cache-policy", ex.ToString()); }
            bool optimize = await api.CallAsync<bool>("get_setting", new { key = "ui_wallpaper_video_optimize", @default = true });
            if (optimize != _optimizeVideo) { _optimizeVideo = optimize; _videoPolicyChanged = true; }
            string mode = await api.TryCallAsync("get_setting", new { key = "ui_wallpaper_mode", @default = "local" }, "local") ?? "local";
            Backdrop.SetClearDesktop(mode == "transparent");
            if (mode == "transparent" && AppServices.Window?.AllowsTransparency != true)
            {
                await ConfigureSync(); return;
            }
            if (mode == "material" || mode == "transparent")
            {
                _rotateEnabled = false; _rotate.Stop(); SetSource(""); await ConfigureSync(); return;
            }
			string image = ((await api.TryCallAsync("get_setting", new
			{
				key = "ui_background",
				@default = ""
			}, "")) ?? "").Trim();
			string folder = ((await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_folder",
				@default = ""
			}, "")) ?? "").Trim();
			bool shuffle = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_shuffle",
				@default = false
			}, fallback: false);
			int interval = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_interval",
				@default = 10
			}, 10);
			int blur = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_blur",
				@default = 0
			}, 0);
			int dim = await api.TryCallAsync("get_setting", new
			{
				key = "ui_background_dim",
				@default = 0
			}, 0);
			string source;
			if (folder.Length > 0 && _playlist.SetFolder(folder, shuffle))
			{
				source = _playlist.Current();
				_rotate.Interval = TimeSpan.FromMinutes(Clamp.Of(interval, 1, 1440));
                _rotateEnabled = true;
				if (PlaybackAllowed) _rotate.Start();
			}
			else
			{
                _rotateEnabled = false;
				_rotate.Stop();
				source = (FileKinds.IsWallpaper(image) ? image : "");
			}
			SetEffects(blur, dim);
			SetSource(source);
			await ConfigureSync();
		}
	}

	private static void Advance()
	{
		string text = _playlist.Advance();
		if (text.Length > 0)
		{
			SetSource(text);
		}
	}

	public static void SetSource(string path)
	{
		path = (path ?? "").Trim();
		if (path == _source && Error.Length == 0 && !_videoPolicyChanged)
		{
			return;
		}
		_source = path;
        _videoPolicyChanged = false;
		Error = "";
		CancelVideoPreparation();
		StopVideo();
		_still.Source = null;
		_still.Visibility = Visibility.Collapsed;
		if (path.Length == 0)
		{
			ApplyChrome(on: false);
			return;
		}
		if (!File.Exists(path))
		{
			Error = I18n.L("文件不存在：") + path;
			_source = "";
			ApplyChrome(on: false);
			return;
		}
		if (FileKinds.IsVideo(path))
		{
			_ = PrepareVideo(path);
		}
		else
		{
			BitmapSource bitmapSource = LoadImage(path);
			if (bitmapSource == null)
			{
				Error = I18n.L("图片读不出来：") + path;
				_source = "";
				ApplyChrome(on: false);
				return;
			}
			_still.Source = bitmapSource;
			_still.Visibility = Visibility.Visible;
		}
		ApplyChrome(on: true);
	}

	public static void SetEffects(int blur, int dim)
	{
		_blur = Clamp.Of(blur, 0, 40);
		_dimPercent = Clamp.Of(dim, 0, 80);
		Effect effect = ((_blur > 0 && !Glass.ClearWallpaper) ? new BlurEffect
		{
			Radius = _blur,
			KernelType = KernelType.Gaussian,
			RenderingBias = RenderingBias.Performance
		} : null);
		_still.Effect = effect;
		if (_video != null)
		{
			_video.Effect = SharedVideoFrost ? null : effect;
		}
        if (_host != null)
        {
            double radius = SharedVideoFrost ? Math.Sqrt(Glass.Frost * Glass.Frost + _blur * _blur) : 0;
            SharedVideoBlur.Radius = radius;
            _host.Effect = radius > 0 ? SharedVideoBlur : null;
            // Overscan keeps a single blur pass from sampling transparent pixels at the window edge.
            _host.Margin = radius > 0 ? new Thickness(-Math.Ceiling(radius * 2)) : new Thickness(0);
        }
		Color color = ((Application.Current?.TryFindResource("C.Canvas") is Color color2) ? color2 : (App.IsDark ? Color.FromRgb(22, 22, 22) : Color.FromRgb(245, 245, 245)));
		_dim.Fill = new SolidColorBrush(color)
		{
			Opacity = Glass.ClearWallpaper ? 0 : (double)_dimPercent / 100.0
		};
		_dim.Visibility = ((_dimPercent <= 0 || !Active) ? Visibility.Collapsed : Visibility.Visible);
	}

	private static void StartVideo(string path, TimeSpan? position = null, string? fallback = null)
	{
        if (fallback != null) _videoPlaybackLease = new HelL.Core.Launcher.WallpaperVideoCache(System.IO.Path.GetDirectoryName(path)!).HoldPlayback(path);
		MediaElement media = new MediaElement
		{
			LoadedBehavior = MediaState.Manual,
			UnloadedBehavior = MediaState.Manual,
			Stretch = Stretch.UniformToFill,
			IsMuted = true,
			IsHitTestVisible = false,
			ScrubbingEnabled = false
		};
		media.MediaFailed += delegate(object _, ExceptionRoutedEventArgs e)
		{
            if (_video != media) return;
            if (fallback != null)
            {
                Smoke.Note("wallpaper-video-cache-playback", e.ErrorException?.ToString() ?? "Cached video failed to decode");
                InvalidateVideoCache(path);
                StopVideo(); StartVideo(fallback); VideoStatus = "缓存播放失败，已恢复原视频"; StatusChanged?.Invoke(); return;
            }
			Error = I18n.L("视频解码失败（系统可能缺 H.264 解码器）：") + e.ErrorException?.Message;
			StopVideo();
			ApplyChrome(on: false);
			StatusChanged?.Invoke();
		};
		media.MediaEnded += delegate
		{
			if (_video != media) return;
			media.Position = TimeSpan.Zero;
			SyncPlayback();
		};
		if (_blur > 0 && !Glass.ClearWallpaper)
		{
			media.Effect = new BlurEffect
			{
				Radius = _blur,
				KernelType = KernelType.Gaussian,
				RenderingBias = RenderingBias.Performance
			};
		}
		_video = media;
        SetEffects(_blur, _dimPercent);
		VideoReady = false;
		media.MediaOpened += (_, _) => { if (_video == media) { if (position.HasValue) media.Position = position.Value; VideoReady = true; SyncPlayback(); StatusChanged?.Invoke(); } };
		_host.Children.Insert(0, media);
		media.Source = new Uri(System.IO.Path.GetFullPath(path));
		SyncPlayback();
	}

	private static void StopVideo()
	{
		MediaElement video = _video;
		_video = null;
		VideoReady = false;
		if (video != null)
		{
			try
			{
				video.Stop();
				video.Close();
				video.Source = null;
			}
			catch (Exception ex) { Smoke.Note("wallpaper-close", ex.ToString()); }
			_host?.Children.Remove(video);
		}
        _videoPlaybackLease?.Dispose(); _videoPlaybackLease = null;
        SetEffects(_blur, _dimPercent);
	}

	private static void SyncPlayback()
	{
		if (_video == null)
		{
			return;
		}
		try
		{
			if (PlaybackAllowed)
			{
				_video.Play();
			}
			else
			{
				_video.Pause();
			}
		}
		catch (Exception ex) { Smoke.Note("wallpaper-playback", ex.ToString()); }
	}

	private static BitmapSource? LoadImage(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bitmapImage.DecodePixelWidth = 1920;
			bitmapImage.UriSource = new Uri(System.IO.Path.GetFullPath(path));
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	private static void ApplyChrome(bool on)
	{
		if (_materialWallpaper != on)
        {
            _materialWallpaper = on;
            Glass.Refresh();
        }
		bool dimUp = !on || _dimPercent <= 0;
		if (_chromeBar != null && _side != null && _wash != null)
		{
			_dim.Visibility = (dimUp ? Visibility.Collapsed : Visibility.Visible);
			bool frosted = on && Backdrop.EffectsEnabled && !SharedVideoFrost;
            _pageMaterial?.Update(frosted, Glass.Frost);
            _chromeMaterial?.Update(frosted, Glass.Frost);
            bool nativeChrome = Backdrop.NativeAvailable && !on;
            _chromeBar.Background = Brushes.Transparent;
            if (_chromeBar.Child is Grid chromeLayer)
            {
                foreach (var tint in chromeLayer.Children.OfType<Rectangle>().Where(rectangle => rectangle.Tag as string == "chrome-material-tint"))
                {
                    tint.Visibility = Visibility.Visible;
                    tint.SetResourceReference(Shape.FillProperty, nativeChrome ? "B.NativeChrome" : Backdrop.EffectsEnabled ? "B.GlassStrong" : "B.Side");
                }
            }
			_chromeBar.SetResourceReference(Border.BorderBrushProperty, "B.GlassBorder");
			_side.SetResourceReference(Border.BackgroundProperty, on ? "B.Glass" : "B.Side");
			_side.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
			_wash.SetResourceReference(Shape.FillProperty, on ? "B.WallpaperPageWash" : "B.PageWash");
			_wash.Opacity = 1;
			SideFrost.Refresh();
		}
	}

	public static void OnThemeChanged()
	{
		SetEffects(_blur, _dimPercent);
		ApplyChrome(Active);
	}

	public static void Shutdown()
	{
		CancelVideoPreparation();
		_rotate.Stop();
		_syncGeneration++;
		_sync.Stop();
		StopEngineCapture();
		StopVideo();
	}
}
