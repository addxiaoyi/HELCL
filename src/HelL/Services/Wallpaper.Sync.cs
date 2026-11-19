using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using HelL.Core.Launcher;

namespace HelL.Services;

public static partial class Wallpaper
{
    private static readonly DispatcherTimer _sync = new() { Interval = TimeSpan.FromSeconds(5) };
    private static string _syncMode = "local", _engineConfig = "", _engineMonitor = "";
    private static bool _syncBusy;
    private static int _syncGeneration;
    private static bool _engineLive;
    private static EngineWallpaperWindow? _engineWindow;
    private static EngineCapture? _engineCapture;
    private static string _engineProject = "", _engineFailure = "";
    private static DateTime _engineRetry;
    private static int _engineFramePending;
    public static bool EngineLive => _engineCapture != null;
    public static string SyncStatus { get; private set; } = "";
    public static string CurrentSource => _source;
    public static bool VideoReady { get; private set; }
    public static event Action? StatusChanged;

    private static async Task ConfigureSync()
    {
        _syncGeneration++; _sync.Stop(); _sync.Tick -= SyncTick; SyncStatus = ""; StopEngineCapture(); _engineRetry = DateTime.MinValue;
        _syncMode = await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_mode", @default = "local" }) ?? "local";
        if (_syncMode == "local") return;
        if (_syncMode == "material") { SyncStatus = Backdrop.NativeAvailable ? "正在使用系统玻璃材质。" : "系统透明效果不可用，已使用实色外观。"; return; }
        if (_syncMode == "transparent") { SyncStatus = Backdrop.ClearAvailable ? "正在透出桌面，背景模糊已关闭。" : "已保存桌面直透模式，重启启动器后生效。"; return; }
        _engineConfig = await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_engine_config", @default = "" }) ?? "";
        _engineMonitor = await AppServices.Client.CallAsync<string>("get_setting", new { key = "ui_wallpaper_monitor", @default = "" }) ?? "";
        _engineLive = await AppServices.Client.CallAsync<bool>("get_setting", new { key = "ui_wallpaper_engine_live", @default = false });
        _rotateEnabled = false; _rotate.Stop(); await SyncExternal(); _sync.Tick += SyncTick; SyncActivity();
    }
    private static async void SyncTick(object? sender, EventArgs e) => await SyncExternal();
    private static async Task SyncExternal()
    {
        if (_syncBusy || !PlaybackAllowed || _syncMode == "local" || _syncMode == "material" || _syncMode == "transparent") return;
        _syncBusy = true; int generation = _syncGeneration; string mode = _syncMode, config = _engineConfig, monitor = _engineMonitor;
        try
        {
            var selected = await Task.Run(() =>
            {
                if (mode == "windows") return (File: Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", "") as string ?? "", Status: "正在同步 Windows 壁纸", Config: "", Project: "");
                string path = File.Exists(config) ? config : WallpaperEngine.DiscoverConfigs().FirstOrDefault() ?? "";
                if (path.Length == 0) throw new IOException("未找到 Wallpaper Engine，请启动它或手动选择配置文件。");
                var papers = WallpaperEngine.Read(path);
                var paper = monitor.Length == 0 ? papers.FirstOrDefault() : papers.FirstOrDefault(paper => paper.Monitor == monitor);
                if (paper == null) throw new IOException("所选显示器暂无壁纸，请在壁纸设置中重新选择。");
                if (paper.DisplayFile.Length == 0) throw new IOException(paper.Error);
                return (File: paper.DisplayFile, Status: paper.Monitor + " · " + paper.Title + "\n" + paper.Description, Config: path, Project: paper.Direct ? "" : paper.File);
            });
            if (generation != _syncGeneration || !PlaybackAllowed) return;
            if (selected.Config.Length > 0) _engineConfig = selected.Config;
            if (selected.File.Length == 0) throw new IOException("系统当前没有可读取的静态壁纸。");
            SetSource(selected.File); SyncStatus = Error.Length > 0 ? Error : selected.Status;
            if (_engineLive && selected.Project.Length > 0)
            {
                if (_engineProject != selected.Project) { StopEngineCapture(); _engineRetry = DateTime.MinValue; }
                if (_engineCapture == null && DateTime.UtcNow >= _engineRetry) await StartEngineCapture(selected.Config, selected.Project, generation);
                SyncStatus = _engineCapture != null ? selected.Status.Split('\n')[0] + "\n动态画面已连接 · 10 帧/秒" : selected.Status + "\n" + _engineFailure;
            }
            else StopEngineCapture();
        }
        catch (Exception ex)
        {
            if (generation != _syncGeneration) return;
            string message = "壁纸同步暂不可用：" + ex.Message + " 将自动重试。";
            if (SyncStatus != message) Smoke.Note("wallpaper-sync", ex.ToString()); SyncStatus = message;
        }
        finally { _syncBusy = false; StatusChanged?.Invoke(); }
    }

    public static async Task AuthorizeEngineAsync()
    {
        if (_syncBusy) throw new InvalidOperationException("壁纸正在同步，请稍后重试。");
        if (_syncMode != "engine") throw new InvalidOperationException("请先应用 Wallpaper Engine 模式。");
        if (!_engineLive) throw new InvalidOperationException("请先勾选「尝试网页 / 场景动态画面」并应用壁纸。");
        var paper = WallpaperEngine.Read(_engineConfig).FirstOrDefault(item => _engineMonitor.Length == 0 || item.Monitor == _engineMonitor);
        if (paper == null || paper.Direct) throw new InvalidOperationException("请选择网页或场景壁纸；图片与视频无需额外授权。");
        _sync.Stop(); StopEngineCapture(); int generation = ++_syncGeneration; _syncBusy = true;
        try
        {
            await StartEngineCapture(_engineConfig, paper.File, generation, true);
            SyncStatus = EngineLive ? paper.Title + " · 动态画面已授权" : _engineFailure;
        }
        finally { _syncBusy = false; SyncActivity(); StatusChanged?.Invoke(); }
    }
    private static async Task StartEngineCapture(string config, string project, int generation, bool authorize = false)
    {
        _engineProject = project; _engineRetry = DateTime.UtcNow.AddMinutes(2);
        EngineWallpaperWindow? window = null; EngineCapture? capture = null;
        try
        {
            string manifest = Path.Combine(Path.GetDirectoryName(project)!, "project.json");
            window = await EngineWallpaperWindow.Open(config, File.Exists(manifest) ? manifest : project, authorize);
            capture = authorize ? await EngineCapture.Pick(new System.Windows.Interop.WindowInteropHelper(AppServices.Window!).Handle, window.Title) : new EngineCapture(window.Handle);
            if (capture == null) { window.Dispose(); _engineFailure = "已取消动态壁纸授权，保留项目预览图。"; _engineRetry = DateTime.MaxValue; return; }
            var first = await capture.FirstFrame(); window.Park();
            if (generation != _syncGeneration) { capture.Dispose(); window.Dispose(); return; }
            _engineWindow = window; _engineCapture = capture; _engineFailure = "";
            _still.Source = first; _still.Visibility = System.Windows.Visibility.Visible; ApplyChrome(true);
            var active = capture;
            capture.Frame += frame =>
            {
                var dispatcher = _host?.Dispatcher;
                if (!PlaybackAllowed || dispatcher == null || dispatcher.HasShutdownStarted || System.Threading.Interlocked.CompareExchange(ref _engineFramePending, 1, 0) != 0) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { if (_engineCapture == active && PlaybackAllowed) _still.Source = frame; }
                    finally { System.Threading.Interlocked.Exchange(ref _engineFramePending, 0); }
                }), DispatcherPriority.Background);
            };
            capture.Failed += message => _host?.Dispatcher.BeginInvoke(new Action(() => { if (_engineCapture == active) { StopEngineCapture(); _engineFailure = "动态画面中断：" + message; SyncStatus = _engineFailure; StatusChanged?.Invoke(); } }));
            capture.Active = PlaybackAllowed;
        }
        catch (Exception ex)
        {
            capture?.Dispose(); window?.Dispose(); _engineFailure = ex is UnauthorizedAccessException ? authorize ? ex.Message : "Windows 拒绝自动捕获，已保留项目预览图。可尝试「授权已应用壁纸」，选择 HelL-Wallpaper 专用窗口。" : "动态模式暂不可用：" + ex.Message;
            if (ex is UnauthorizedAccessException) _engineRetry = DateTime.MaxValue;
            Smoke.Note("wallpaper-engine-live", ex.ToString());
        }
    }
    private static void StopEngineCapture()
    {
        var capture = _engineCapture; _engineCapture = null; capture?.Dispose();
        var window = _engineWindow; _engineWindow = null; window?.Dispose();
    }
}
