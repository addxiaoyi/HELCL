using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed partial class LaunchPage
{
    private readonly SPanel _homeLibrary = Ui.V(10);
    private List<DashboardInstance> _homeInstances = new();
    private int _libraryGeneration;
    private DashboardInstance? _homeLaunchTarget;

    private async Task LoadHomeLibrary()
    {
        int generation = ++_libraryGeneration;
        string selectedInstance = _inst.Str();
        if (_homeInstances.Count == 0) { _homeLibrary.Children.Clear(); _homeLibrary.Children.Add(Ui.Muted("正在读取最近的实例…")); }
        try
        {
            var instances = await Api.CallAsync<List<DashboardInstance>>("dashboard_instances", new { limit = 12 }) ?? new();
            if (generation != _libraryGeneration) return;
            _homeInstances = instances; RenderHomeLibrary();
            if (_ver.Str().Length == 0 && _inst.Str() == selectedInstance && _taskId == null)
            {
                var recent = instances.FirstOrDefault(row => row.Version.Length > 0);
                if (recent != null) await SelectHomeInstance(recent, selectedInstance);
            }
        }
        catch (Exception ex)
        {
            if (generation != _libraryGeneration) return;
            Smoke.Note("home-library", ex.ToString()); _homeLibrary.Children.Clear();
            if (_librarySection != null) _librarySection.Visibility = Visibility.Visible;
            _homeLibrary.Children.Add(Ui.V(8, Ui.Muted("实例暂时无法读取：" + ex.Message).Wrap(), Ui.Btn("重新读取", BtnKind.Ghost, (_, _) => Run(LoadHomeLibrary))));
        }
    }

    private void RenderHomeLibrary()
    {
        _homeLibrary.Children.Clear();
        var installed = _homeInstances.Where(row => row.Version.Length > 0).Take(4).ToList();
        if (_librarySection != null) _librarySection.Visibility = installed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (installed.Count == 0)
        {
            return;
        }
        foreach (var group in installed.GroupBy(row => row.Pinned))
        {
            _homeLibrary.Children.Add(Ui.Txt(group.Key ? "固定实例" : "最近游玩", 12, true, "B.InkMuted"));
            var entries = group.ToList();
            for (int index = 0; index < entries.Count; index += 2)
            {
                UIElement first = InstanceCard(entries[index]);
                if (index + 1 == entries.Count) { _homeLibrary.Children.Add(first); continue; }
                _homeLibrary.Children.Add(new AdaptiveColumns(first, InstanceCard(entries[index + 1]), 600, 500, gap: 12, fixedRatio: .48));
            }
        }
    }

    private UIElement InstanceCard(DashboardInstance instance)
    {
        var picture = new LocalPicture(double.NaN, 104, "为这个世界添加封面") { Radius = 0 };
        if (instance.Cover.Length > 0) _ = picture.SetPath(instance.Cover, Path.GetDirectoryName(instance.Cover)!, instance.Instance + "游戏画面");
        var coverFrame = new Border { Child = picture, CornerRadius = new CornerRadius(14, 14, 0, 0), ClipToBounds = true, BorderThickness = new Thickness(0) };
        string played = instance.LastPlayed > 0 ? "上次游玩 " + DateTimeOffset.FromUnixTimeSeconds(instance.LastPlayed).LocalDateTime.ToString("MM-dd HH:mm") : "尚未游玩";
        var name = Ui.Txt(instance.Instance, 14.5, true).Trim();
        var labels = Ui.V(3, name, Ui.Small(instance.Version + " · " + played).Trim());
        var start = Ui.Btn(instance.LastPlayed > 0 ? "继续游戏" : "开始游戏", BtnKind.Soft, (_, _) => Run(() => StartHomeInstance(instance)), "\ue768");
        start.HorizontalAlignment = HorizontalAlignment.Stretch;
        start.HorizontalContentAlignment = HorizontalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetHelpText(start, "启动 " + instance.Instance + " · " + instance.Version);
        var pin = Ui.IconBtn(instance.Pinned ? "\ue840" : "\ue718", instance.Pinned ? "取消固定实例" : "固定实例", (_, _) => Run(async () => { await Api.CallAsync("pin_instance", new { instance = instance.Instance, pinned = !instance.Pinned }); await LoadHomeLibrary(); }), 12);
        var heading = Ui.G(null, "*,Auto"); heading.Add(labels, 0); heading.Add(pin.VCenter(), 0, 1);
        var body = new Border { Child = Ui.V(10, heading, start), Padding = new Thickness(14, 11, 14, 14) };
        var shell = Ui.Pane(Ui.V(0, coverFrame, body), "B.Card", 14, 0);
        shell.ClipToBounds = true;
        return shell;
    }

    private async Task StartHomeInstance(DashboardInstance instance)
    {
        if (_resuming || _taskId != null || _launchPreparing) return;
        _resuming = true; _homeLaunchTarget = instance; _homeLibrary.IsEnabled = false;
        try
        {
            await SelectHomeInstance(instance); await LaunchAsync();
        }
        finally { _resuming = false; _homeLaunchTarget = null; _homeLibrary.IsEnabled = true; }
    }
    private void ValidateResumeTarget(string instance, string version)
    {
        if (!_resuming) return;
        string expectedInstance = _homeLaunchTarget?.Instance ?? _recent?.Instance ?? "";
        string expectedVersion = _homeLaunchTarget?.Version ?? _recent?.Version ?? "";
        if (instance != expectedInstance || version != expectedVersion) throw new InvalidOperationException("启动选择已发生变化，请确认版本后再启动。");
    }
    private async Task SelectHomeInstance(DashboardInstance instance, string? expectedSelection = null)
    {
        var names = await Api.CallAsync<List<InstanceInfo>>("get_instances") ?? new();
        if (!names.Any(row => row.Name == instance.Instance)) throw new InvalidOperationException("此实例已被移除，请刷新实例列表。");
        var versions = await Api.CallAsync<List<string>>("get_installed_versions", new { instance = instance.Instance }) ?? new();
        if (!versions.Contains(instance.Version)) throw new InvalidOperationException("该游戏版本已不存在，请重新安装或选择其他版本。");
        if (expectedSelection != null && (_inst.Str() != expectedSelection || _ver.Str().Length > 0 || _taskId != null)) return;
        _syncing = true;
        try { _inst.Fill(names.Select(row => row.Name), instance.Instance); _ver.Fill(versions, instance.Version); }
        finally { _syncing = false; }
        await ReloadJavaAsync(scan: false); SyncBanner();
    }
}
