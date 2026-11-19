using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class LaunchPage
{
    private readonly Border _recentHost = new Border { Visibility = Visibility.Collapsed };
    private RecentPlayInfo? _recent;
    private bool _resuming;
    private int _recentLoad;

    private async Task LoadRecentAsync()
    {
        int generation = ++_recentLoad;
        try
        {
            var recent = await Api.CallAsync<RecentPlayInfo>("recent_play");
            if (generation != _recentLoad) return;
            _recent = recent;
            _recentHost.Visibility = recent?.Available == true ? Visibility.Visible : Visibility.Collapsed;
            if (recent?.Available != true) return;
            var picture = new LocalPicture(92, 58, I18n.L("最近游玩"));
            string imagePath = recent.Visual.Path ?? "";
            _ = picture.SetPath(imagePath, imagePath.Length == 0 ? "" : Path.GetDirectoryName(imagePath) ?? "", I18n.L("最近游玩的游戏画面"));
            var label = Ui.V(3, Ui.Txt(I18n.L("最近游玩"), 11, false, "B.InkMuted"),
                Ui.Txt(recent.Instance, 14, true).Wrap(),
                Ui.Small(recent.Version + " · " + DateTimeOffset.FromUnixTimeSeconds(recent.Ended).LocalDateTime.ToString("MM-dd HH:mm")).Wrap());
            var resume = Ui.Btn(I18n.L("继续游戏"), BtnKind.Chip, (_, _) => Run(ResumeRecentAsync), "\ue768");
            var row = Ui.G(null, "Auto,*,Auto");
            row.Add(picture.M(0, 0, 12, 0), 0); row.Add(label.VCenter(), 0, 1); row.Add(resume.VCenter().M(12, 0, 0, 0), 0, 2);
            _recentHost.Child = row;
            _recentHost.Padding = new Thickness(0, 12, 0, 12);
            _recentHost.BorderThickness = new Thickness(0, 1, 0, 0);
            _recentHost.SetResourceReference(Border.BorderBrushProperty, "B.LineSoft");
        }
        catch (Exception ex) { _recentHost.Visibility = Visibility.Collapsed; Smoke.Note("recent-play", ex.Message); }
    }

    private async Task<bool> SelectRecentAsync()
    {
        var recent = _recent;
        if (recent?.Available != true) return false;
        var instances = await Api.CallAsync<System.Collections.Generic.List<InstanceInfo>>("get_instances");
        if (instances?.Any(instance => instance.Name == recent.Instance) != true)
            throw new InvalidOperationException(I18n.L("最近游玩的实例已不存在，请重新选择。"));
        var versions = await Api.CallAsync<System.Collections.Generic.List<string>>("get_installed_versions", new { instance = recent.Instance });
        if (versions?.Contains(recent.Version) != true)
            throw new InvalidOperationException(I18n.L("最近游玩的版本已被移除，请重新选择。"));
        _syncing = true;
        try { _inst.Fill(instances.Select(instance => instance.Name), recent.Instance); _ver.Fill(versions, recent.Version); }
        finally { _syncing = false; }
        await ReloadJavaAsync(scan: false);
        if (_inst.Str() != recent.Instance || _ver.Str() != recent.Version)
            throw new InvalidOperationException(I18n.L("启动选择已发生变化，请确认版本后再启动。"));
        SyncBanner();
        _launchBtn.IsEnabled = _taskId == null;
        return true;
    }

    private async Task ResumeRecentAsync()
    {
        if (_resuming || _taskId != null) return;
        _resuming = true;
        _recentHost.IsEnabled = false;
        try
        {
            if (await SelectRecentAsync())
            {
                _launchBtn.IsEnabled = false;
                await LaunchAsync();
            }
        }
        finally { _resuming = false; _recentHost.IsEnabled = true; _launchBtn.IsEnabled = _taskId == null && !string.IsNullOrEmpty(_ver.Str()); }
    }
}
