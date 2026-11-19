using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HelL.Models;
using HelL.Services;

namespace HelL.Pages;

public sealed class ScreenshotPage
{
    public List<MediaRow> Items { get; set; } = new List<MediaRow>();
    public int Total { get; set; }
    public int Offset { get; set; }
}

public static class InstanceArtworkDialog
{
    public static async Task Show(string instance, Func<Task> changed)
    {
        var status = Ui.Txt("", 12, false, "B.InkMuted").Wrap();
        var tiles = new UniformGrid { Columns = 3 };
        tiles.SizeChanged += (_, _) => tiles.Columns = tiles.ActualWidth < 570 ? 2 : 3;
        var scroll = Ui.Scroll(tiles); scroll.MaxHeight = Math.Max(160, Math.Min(390, (AppServices.Window?.ActualHeight ?? 740) - 260));
        int offset = 0, total = 0;
        bool busy = false, closed = false;
        var pageText = Ui.Small("");
        var previous = Ui.Btn(I18n.L("上一页"), BtnKind.Ghost);
        var next = Ui.Btn(I18n.L("下一页"), BtnKind.Ghost);
        var upload = Ui.Btn(I18n.L("选择本地封面"), BtnKind.Chip);
        var reset = Ui.Btn(I18n.L("恢复自动封面"), BtnKind.Ghost);
        var refresh = Ui.Btn(I18n.L("刷新"), BtnKind.Ghost);
        var actions = Ui.H(8, upload, reset, refresh); actions.Wrap = true;
        Dlg.Panel(I18n.L("封面与截图 · {0}", instance), Ui.V(12, actions, status, scroll, Ui.H(8, previous, pageText.VCenter(), next).Right()), 790, onClose: () => closed = true);
        async Task Run(Func<Task> work)
        {
            if (busy || closed) return;
            busy = true; actions.IsEnabled = tiles.IsEnabled = previous.IsEnabled = next.IsEnabled = false;
            status.Text = I18n.L("正在处理…");
            try { await work(); if (!closed) status.Text = I18n.L("点击图片放大，或将它设为实例封面。"); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; actions.IsEnabled = tiles.IsEnabled = true; previous.IsEnabled = offset > 0; next.IsEnabled = offset + 24 < total; }
        }
        async Task Load()
        {
            var page = await AppServices.Client.CallAsync<ScreenshotPage>("instance_screenshots", new { instance, offset, limit = 24 }) ?? throw new IOException(I18n.L("截图列表读取失败。"));
            if (closed) return;
            total = page.Total;
            offset = page.Offset;
            tiles.Children.Clear();
            foreach (var item in page.Items)
            {
                var picture = new LocalPicture(double.NaN, 110, I18n.L("截图加载中"));
                _ = picture.SetPath(item.Path, Path.GetDirectoryName(item.Path)!, item.Name);
                var open = Ui.Btn("", BtnKind.Ghost, (_, _) => OpenImage(item)); open.Content = picture; open.Padding = new Thickness(0);
                open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                System.Windows.Automation.AutomationProperties.SetName(open, I18n.L("查看截图 {0}", item.Name));
                var use = Ui.Btn(I18n.L("设为封面"), BtnKind.Ghost, async (_, _) => await Run(async () => { await AppServices.Client.CallAsync("set_instance_cover", new { instance, path = item.Path }); await changed(); }));
                tiles.Children.Add(Ui.V(5, open, Ui.Small(item.Name).Trim(), use.Left()).M(5));
            }
            if (total == 0) tiles.Children.Add(Ui.Muted(I18n.L("还没有游戏截图，可用 F2 拍摄，或选择本地封面。")));
            pageText.Text = I18n.L("{0} / {1} 页 · {2} 张", total == 0 ? 0 : offset / 24 + 1, (total + 23) / 24, total);
            scroll.ScrollToTop();
        }
        upload.Click += async (_, _) =>
        {
            string? path = Dlg.PickFile(I18n.L("图片 (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"), I18n.L("选择实例封面"));
            if (path != null) await Run(async () => { await AppServices.Client.CallAsync("set_instance_cover", new { instance, path }); await changed(); });
        };
        reset.Click += async (_, _) => await Run(async () => { await AppServices.Client.CallAsync("reset_instance_cover", new { instance }); await changed(); });
        refresh.Click += async (_, _) => await Run(Load);
        previous.Click += async (_, _) => { if (busy) return; offset = Math.Max(0, offset - 24); await Run(Load); };
        next.Click += async (_, _) => { if (busy) return; offset += 24; await Run(Load); };
        await Run(Load);
    }

    private static void OpenImage(MediaRow item)
    {
        var picture = new LocalPicture(double.NaN, Math.Min(450, (AppServices.Window?.ActualHeight ?? 740) - 220), I18n.L("截图加载中"));
        _ = picture.SetPath(item.Path, Path.GetDirectoryName(item.Path)!, item.Name);
        Dlg.Panel(item.Name, picture, 850);
    }
}
