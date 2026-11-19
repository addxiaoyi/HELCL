using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Services;

namespace HelL.Pages;

public static class WallpaperDialog
{
    public static async Task Show()
    {
        var api = AppServices.Client;
        async Task<string> Read(string key, string fallback = "") => await api.CallAsync<string>("get_setting", new { key, @default = fallback }) ?? fallback;
        string mode = await Read("ui_wallpaper_mode", "local"), path = await Read("ui_background"), folder = await Read("ui_background_folder");
        string engineConfig = await Read("ui_wallpaper_engine_config"), monitor = await Read("ui_wallpaper_monitor");
        var modes = Ui.Combo(); modes.Items.Add("本地图片 / 视频"); modes.Items.Add("同步 Windows 壁纸"); modes.Items.Add("同步 Wallpaper Engine"); modes.Items.Add("系统玻璃 · 不叠加壁纸"); modes.Items.Add(I18n.L("直接透出桌面 / 其他窗口"));
        modes.SelectedIndex = mode == "transparent" ? 4 : mode == "material" ? 3 : mode == "engine" ? 2 : mode == "windows" ? 1 : 0;
        var file = Ui.Input("选择图片、视频或轮播文件夹", folder.Length > 0 ? folder : path); file.IsReadOnly = true;
        var choose = Ui.Btn("选择文件", BtnKind.Ghost, (_, _) => { string? chosen = Dlg.PickFile("壁纸|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.webm;*.wmv;*.m4v", "选择壁纸"); if (chosen != null) { path = chosen; folder = ""; file.Text = chosen; } });
        var chooseFolder = Ui.Btn("轮播文件夹", BtnKind.Ghost, (_, _) => { string? chosen = Dlg.PickFolder("选择壁纸文件夹"); if (chosen != null) { folder = chosen; file.Text = chosen; } });
        var clear = Ui.Btn("清除", BtnKind.Ghost, (_, _) => { path = folder = file.Text = ""; });
        var local = Ui.V(8, file, Ui.H(8, choose, chooseFolder, clear));
        var displays = Ui.Combo(); var engineStatus = Ui.Muted("正在检测 Wallpaper Engine…").Wrap(); var preview = new LocalPicture(double.NaN, 145, "选择显示器查看项目预览");
        var refresh = Ui.Btn("重新检测", BtnKind.Ghost); var browse = Ui.Btn("指定配置文件", BtnKind.Ghost);
        var live = Ui.Check("尝试网页 / 场景动态画面", await api.CallAsync<bool>("get_setting", new { key = "ui_wallpaper_engine_live", @default = false }));
        var engine = Ui.V(8, displays, preview, engineStatus, live, Ui.Small("需要运行 Wallpaper Engine；会创建专用渲染窗口。系统不支持时回退为项目预览图。").Wrap(), Ui.H(8, refresh, browse)); List<EngineWallpaper> papers = new();
        var status = Ui.Muted(Wallpaper.SyncStatus).Wrap();
        bool closed = false, detecting = false;
        async Task Detect()
        {
            if (detecting) return; detecting = true; refresh.IsEnabled = browse.IsEnabled = false;
            try
            {
                var scanned = await Task.Run(() =>
                {
                    string config = File.Exists(engineConfig) ? engineConfig : WallpaperEngine.DiscoverConfigs().FirstOrDefault() ?? "";
                    return (Config: config, Papers: config.Length > 0 ? WallpaperEngine.Read(config) : new List<EngineWallpaper>());
                });
                if (closed) return; engineConfig = scanned.Config; papers = scanned.Papers; displays.Items.Clear();
                foreach (var paper in papers) displays.Items.Add(paper.Monitor + " · " + paper.Title);
                displays.SelectedIndex = Math.Max(0, papers.FindIndex(paper => paper.Monitor == monitor));
                if (papers.Count == 0) engineStatus.Text = "未找到当前壁纸。请启动 Wallpaper Engine，或指定其安装目录中的 config.json。";
            }
            catch (Exception ex) { engineStatus.Text = "检测失败：" + ex.Message + "，可重新检测。"; Smoke.Note("wallpaper-detect", ex.ToString()); }
            finally { detecting = false; refresh.IsEnabled = browse.IsEnabled = true; }
        }
        displays.SelectionChanged += (_, _) =>
        {
            if (displays.SelectedIndex < 0 || displays.SelectedIndex >= papers.Count) return;
            var paper = papers[displays.SelectedIndex]; monitor = paper.Monitor; engineStatus.Text = paper.Description;
            string image = paper.Preview.Length > 0 ? paper.Preview : paper.Kind == "image" ? paper.File : "";
            _ = preview.SetPath(image, image.Length > 0 ? Path.GetDirectoryName(image)! : "", paper.Title);
        };
        refresh.Click += (_, _) => PageBase.Run(Detect);
        browse.Click += (_, _) => { string? selected = Dlg.PickFile("Wallpaper Engine 配置|config.json", "选择 Wallpaper Engine 配置"); if (selected != null) { engineConfig = selected; PageBase.Run(Detect); } };
        var dim = Ui.Sld(0, 80, await api.CallAsync<int>("get_setting", new { key = "ui_background_dim", @default = 25 }), 1);
        var blur = Ui.Sld(0, 40, await api.CallAsync<int>("get_setting", new { key = "ui_background_blur", @default = 0 }), 1);
        var effects = Ui.Small(""); void Labels() => effects.Text = $"遮罩 {dim.Value:0}% · 模糊 {blur.Value:0}";
        dim.ValueChanged += (_, _) => Labels(); blur.ValueChanged += (_, _) => Labels(); Labels();
        var effectsPanel = Ui.V(10, effects, dim, blur);
        var optimize = Ui.Check("自动优化高分辨率视频", await api.CallAsync<bool>("get_setting", new { key = "ui_wallpaper_video_optimize", @default = true }));
        optimize.ToolTip = "首次生成较轻的播放缓存，保留原视频。容量和保留时间可在缓存策略中调整。";
        var videoStatus = Ui.Small(Wallpaper.VideoStatus).Wrap();
        effectsPanel.Children.Add(optimize); effectsPanel.Children.Add(videoStatus);
        var materialHelp = Ui.Muted("").Wrap();
        var material = Ui.V(8, Ui.H3(I18n.L("窗口背景")), materialHelp);
        var restart = Ui.Btn(I18n.L("重启应用外观"), BtnKind.Normal, (_, _) => AppServices.Window?.RestartForAppearance());
        restart.Visibility = AppServices.Window?.AllowsTransparency != (mode == "transparent") ? Visibility.Visible : Visibility.Collapsed;
        var apply = Ui.Btn("应用壁纸", BtnKind.Primary); apply.Click += (_, _) => PageBase.RunOnce(apply, async () =>
        {
            if (modes.SelectedIndex == 2 && papers.Count == 0) { status.Text = "请先检测到 Wallpaper Engine 当前壁纸。"; return; }
            if (modes.SelectedIndex == 0 && ((folder.Length > 0 && !Directory.Exists(folder)) || (folder.Length == 0 && path.Length > 0 && !File.Exists(path)))) { status.Text = "所选壁纸已移动，请重新选择。"; return; }
            await api.CallAsync("save_settings", new { data = new Dictionary<string, object> {
                ["ui_wallpaper_mode"] = modes.SelectedIndex == 4 ? "transparent" : modes.SelectedIndex == 3 ? "material" : modes.SelectedIndex == 2 ? "engine" : modes.SelectedIndex == 1 ? "windows" : "local",
                ["ui_wallpaper_engine_config"] = engineConfig, ["ui_wallpaper_monitor"] = monitor,
                ["ui_wallpaper_engine_live"] = live.IsChecked == true,
                ["ui_wallpaper_video_optimize"] = optimize.IsChecked == true,
                ["ui_background"] = path, ["ui_background_folder"] = folder, ["ui_background_dim"] = (int)dim.Value, ["ui_background_blur"] = (int)blur.Value } });
            await Wallpaper.ReloadAsync(); status.Text = Wallpaper.Error.Length > 0 ? Wallpaper.Error : Wallpaper.SyncStatus.Length > 0 ? Wallpaper.SyncStatus : "壁纸已应用。";
            restart.Visibility = AppServices.Window?.AllowsTransparency != (modes.SelectedIndex == 4) ? Visibility.Visible : Visibility.Collapsed;
        }, "壁纸未能应用");
        var authorize = Ui.Btn("授权已应用壁纸", BtnKind.Ghost);
        void SelectMode()
        {
            local.Visibility = modes.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            engine.Visibility = authorize.Visibility = modes.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            material.Visibility = modes.SelectedIndex >= 3 ? Visibility.Visible : Visibility.Collapsed;
            effectsPanel.Visibility = modes.SelectedIndex >= 3 ? Visibility.Collapsed : Visibility.Visible;
            string action = modes.SelectedIndex >= 3 ? "应用材质" : "应用壁纸"; apply.Content = action;
            materialHelp.Text = modes.SelectedIndex == 4
                ? I18n.L("直接显示窗口后方的桌面与其他窗口；首次切换需重启。透明度可在「实时调整玻璃」中设为 100%。")
                : I18n.L("Mica 随桌面色调变化，Acrylic 的背景模糊更明显。原来的壁纸选择会保留，随时可以切回。");
            System.Windows.Automation.AutomationProperties.SetName(apply, action);
            if (modes.SelectedIndex == 2) PageBase.Run(Detect);
        }
        modes.SelectionChanged += (_, _) => SelectMode();
        authorize.Click += (_, _) => PageBase.RunOnce(authorize, Wallpaper.AuthorizeEngineAsync, "壁纸授权未完成");
        authorize.ToolTip = "先应用网页或场景壁纸，再在 Windows 窗口选择器中选择 HelL-Wallpaper 专用窗口。";
        Action syncChanged = () => { if (closed) return; if (Wallpaper.SyncStatus.Length > 0) status.Text = Wallpaper.SyncStatus; videoStatus.Text = Wallpaper.VideoStatus; };
        Wallpaper.StatusChanged += syncChanged;
        Dlg.Panel("壁纸与材质", Ui.V(12, modes, local, engine, material, effectsPanel, new WallpaperCachePanel(),
            Ui.H(8, Ui.Btn("实时调整玻璃…", BtnKind.Ghost, (_, _) => PageBase.Run(MaterialDialog.Show)),
                Ui.Btn("外观方案…", BtnKind.Ghost, (_, _) => PageBase.Run(AppearanceDialog.Show))), status),
            650, onClose: () => { closed = true; Wallpaper.StatusChanged -= syncChanged; }, footer: Ui.H(8, authorize, restart, apply).Right());
        SelectMode();
    }
}
