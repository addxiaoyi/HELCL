using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL.Pages;

internal static class WallpaperCachePolicyDialog
{
    internal static async Task Show()
    {
        int size = await AppServices.Client.CallAsync<int>("get_setting", new { key = "ui_wallpaper_cache_mib", @default = 512 });
        int days = await AppServices.Client.CallAsync<int>("get_setting", new { key = "ui_wallpaper_cache_days", @default = 0 });
        var sizes = new[] { 256, 512, 1024, 2048, 4096, Math.Max(256, Math.Min(4096, size)) }.Distinct().OrderBy(value => value).ToList();
        var periods = new[] { 0, 7, 30, 90, Math.Max(0, Math.Min(365, days)) }.Distinct().OrderBy(value => value).ToList();
        var capacity = Ui.Combo(sizes.Select(value => value + " MiB")); capacity.SelectedIndex = sizes.IndexOf(size);
        var retention = Ui.Combo(periods.Select(value => value == 0 ? "仅按容量清理" : value + " 天未使用后清理")); retention.SelectedIndex = periods.IndexOf(days);
        if (capacity.SelectedIndex < 0) capacity.SelectedIndex = sizes.IndexOf(512);
        if (retention.SelectedIndex < 0) retention.SelectedIndex = 0;
        capacity.Width = retention.Width = double.NaN;
        var status = Ui.Muted("").Wrap(); status.Visibility = Visibility.Collapsed;
        var save = Ui.Btn("保存策略", BtnKind.Primary); var cancel = Ui.Btn("取消", BtnKind.Ghost);
        var fields = Ui.V(14, Ui.Field("容量上限", capacity, "达到上限时，优先清理最久未使用的缓存。", 100),
            Ui.Field("保留策略", retention, "按最后播放时间计算；正在播放和生成的缓存会保留。", 100),
            Ui.Small("策略立即保存并清理符合条件的闲置缓存。原视频保持不变，单个播放缓存最多 256 MiB。").Wrap(), status);
        bool saving = false; var actions = Ui.H(8, cancel, save).Right();
        var layer = Dlg.Panel("视频缓存策略", fields, 520, footer: actions, canDismiss: () => !saving);
        cancel.Click += (_, _) => layer.Dismiss();
        save.Click += async (_, _) =>
        {
            if (saving) return; saving = true; fields.IsEnabled = actions.IsEnabled = false;
            try
            {
                var policy = new WallpaperCachePolicy(sizes[capacity.SelectedIndex], periods[retention.SelectedIndex]);
                await AppServices.Client.CallAsync("save_settings", new { data = new Dictionary<string, object> { ["ui_wallpaper_cache_mib"] = policy.CapacityMiB, ["ui_wallpaper_cache_days"] = policy.KeepDays } });
                Wallpaper.SetCachePolicy(policy);
                try
                {
                    var cleanup = await Task.Run(() => new WallpaperVideoCache(Path.Combine(Utils.ROOT, "cache", "wallpaper-video"), policy).EnforcePolicy());
                    if (cleanup.Errors.Length > 0) throw new IOException(string.Join(Environment.NewLine, cleanup.Errors));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                {
                    Smoke.Note("wallpaper-cache-policy-clean", ex.ToString());
                    AppServices.Toast("缓存策略已保存", "部分缓存暂时无法清理，会在下次加载壁纸时重试。", ToastKind.Warning);
                }
                layer.Close();
            }
            catch (Exception ex) { Smoke.Note("wallpaper-cache-policy-save", ex.ToString()); status.Text = "策略未能保存：" + ex.Message; status.Visibility = Visibility.Visible; }
            finally { saving = false; fields.IsEnabled = actions.IsEnabled = true; }
        };
    }
}
