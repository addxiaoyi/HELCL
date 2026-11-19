using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL.Pages;

public sealed class WallpaperCachePanel : Border
{
    private readonly WallpaperVideoCache _cache = new(Path.Combine(Utils.ROOT, "cache", "wallpaper-video"));
    private readonly TextBlock _usage = Ui.Muted("正在统计缓存…").Wrap();
    private readonly TextBlock _status = Ui.Small("").Wrap();
    private readonly Button _clear = Ui.Btn("清理缓存", BtnKind.Ghost);
    private readonly Button _refresh = Ui.Btn("刷新", BtnKind.Ghost);
    private readonly TextBlock _policy = Ui.Small("").Wrap();
    private bool _busy;
    private readonly DispatcherTimer _refreshDelay = new() { Interval = TimeSpan.FromSeconds(1) };

    public WallpaperCachePanel()
    {
        var policy = Ui.Btn("缓存策略…", BtnKind.Ghost, (_, _) => PageBase.Run(WallpaperCachePolicyDialog.Show));
        var actions = Ui.H(6, policy, _refresh, _clear); actions.Wrap = true;
        Child = Ui.V(8, new PageHeader(Ui.V(4, Ui.Txt("视频壁纸缓存", 14, true), _usage), actions),
            _policy, Ui.Small("清理未使用的播放缓存，正在播放或生成的文件会保留。原壁纸文件不受影响。").Wrap(), _status);
        Padding = new Thickness(12); CornerRadius = new CornerRadius(6); BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "B.SurfaceInset"); SetResourceReference(BorderBrushProperty, "B.SurfaceEdge");
        AutomationProperties.SetName(this, "视频壁纸缓存"); AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _clear.IsEnabled = false; _status.Visibility = Visibility.Collapsed;
        _clear.Click += (_, _) => PageBase.Run(() => Execute(clear: true));
        _refresh.Click += (_, _) => { _status.Visibility = Visibility.Collapsed; PageBase.Run(() => Execute(clear: false)); };
        _refreshDelay.Tick += (_, _) => { _refreshDelay.Stop(); PageBase.Run(() => Execute(clear: false)); };
        Loaded += (_, _) => { Wallpaper.StatusChanged += QueueRefresh; PageBase.Run(() => Execute(clear: false)); };
        Unloaded += (_, _) => { Wallpaper.StatusChanged -= QueueRefresh; _refreshDelay.Stop(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) PageBase.Run(() => Execute(clear: false)); };
    }

    private void QueueRefresh() { if (IsVisible && !_refreshDelay.IsEnabled) _refreshDelay.Start(); }

    private async Task Execute(bool clear)
    {
        if (_busy) return; _busy = true; _clear.IsEnabled = _refresh.IsEnabled = false;
        try
        {
            if (clear)
            {
                _status.Visibility = Visibility.Visible; _status.Text = "正在清理…";
                var cleanup = await Task.Run(() => _cache.ClearUnused());
                _status.Text = "已释放 " + Bytes(cleanup.FreedBytes) + (cleanup.SkippedFiles > 0 ? "，正在使用的缓存已保留。" : "。");
                if (cleanup.Errors.Length > 0) { Smoke.Note("wallpaper-cache-clean", string.Join(Environment.NewLine, cleanup.Errors)); _status.Text += " 部分文件未能清理，可稍后重试。"; }
            }
            var usage = await Task.Run(_cache.Inspect);
            var policy = Wallpaper.CachePolicy;
            _usage.Text = (usage.Videos + usage.Partials) + " 项 · " + Bytes(usage.Bytes) + " / " + policy.CapacityMiB + " MiB · 可清理 " + Bytes(usage.ReclaimableBytes) + (usage.InUseBytes > 0 ? " · 使用中 " + Bytes(usage.InUseBytes) : "");
            _policy.Text = policy.KeepDays == 0 ? "保留策略：仅按容量清理" : "保留策略：" + policy.KeepDays + " 天未使用后清理";
            _clear.IsEnabled = usage.ReclaimableBytes > 0;
            _clear.ToolTip = usage.InUseBytes > 0 ? "正在使用的缓存会保留，切换为图片或系统材质后即可清理。" : "只清理生成的播放缓存和未完成文件。";
        }
        catch (Exception ex)
        {
            Smoke.Note("wallpaper-cache-panel", ex.ToString()); _status.Text = "缓存操作未完成：" + ex.Message; _status.Visibility = Visibility.Visible;
        }
        finally { _busy = false; _refresh.IsEnabled = true; }
    }

    private static string Bytes(long bytes) => bytes >= 1024 * 1024 ? (bytes / 1048576d).ToString("0.0") + " MiB" : bytes >= 1024 ? (bytes / 1024d).ToString("0.0") + " KiB" : bytes + " B";
}
