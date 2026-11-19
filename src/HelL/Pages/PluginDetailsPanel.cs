using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Plugins;
using HelL.Services;

namespace HelL.Pages;

public sealed class PluginDetailsPanel : StackPanel
{
    private readonly PluginListing _listing;
    private readonly SPanel _gallery = Ui.V(8);
    private readonly TextBlock _state = Ui.Small("");
    private readonly Button _install;
    private bool _loaded;
    public PluginDetailsPanel(PluginListing listing, Func<Task> changed)
    {
        _listing = listing; var manifest = listing.Manifest;
        Children.Add(Ui.V(6, Ui.Muted(manifest.Author + " · " + manifest.Version), Ui.Txt(manifest.Description, 14).Wrap()).M(0, 0, 0, 16));
        _state.Text = "安装后可在启动页模块库中添加";
        var current = LauncherPlugins.Installed().FirstOrDefault(plugin => plugin.Manifest.Id == manifest.Id);
        bool update = current != null && current.Error.Length == 0 && new Version(manifest.Version) > new Version(current.Manifest.Version);
        _install = Ui.Btn(current == null ? "安装插件" : current.Error.Length > 0 ? "修复插件" : update ? "更新插件" : "已安装", BtnKind.Primary);
        _install.IsEnabled = current == null || update || current.Error.Length > 0;
        _install.Click += async (_, _) =>
        {
            _install.IsEnabled = false;
            try { _state.Text = "正在下载并校验…"; await PluginCatalog.Install(listing, LauncherPlugins.Store); LauncherPlugins.NotifyChanged(); await changed(); _state.Text = "安装完成。到启动页模块库添加即可使用。"; _install.Content = "已安装"; }
            catch (Exception ex) { Smoke.Note("plugin-details-install", ex.ToString()); _state.Text = "安装失败：" + ex.Message; _install.IsEnabled = true; }
        };
        Children.Add(Ui.Inset(new PageHeader(_state.Wrap().VCenter(), _install), 12).M(0, 0, 0, 16));
        Children.Add(_gallery);
        Children.Add(Ui.V(8, Ui.H3("包含的模块"), Ui.Muted(string.Join("\n", manifest.Modules.Select(module => module.Title + " · " + Kind(module.Kind)))).Wrap()).M(0, 16, 0, 0));
        Children.Add(Ui.V(6, Ui.H3("更新说明"), manifest.Releases.Count == 0 ? Ui.Muted("作者尚未提供更新说明。") : Releases(manifest)).M(0, 20, 0, 0));
        if (!string.IsNullOrEmpty(manifest.Homepage)) Children.Add(Ui.Btn("项目主页", BtnKind.Ghost, (_, _) => Process.Start(new ProcessStartInfo(manifest.Homepage) { UseShellExecute = true })).Left().M(0, 16, 0, 0));
        Loaded += (_, _) => { if (!_loaded) { _loaded = true; PageBase.Run(LoadImages); } };
    }
    private static UIElement Releases(PluginManifest manifest)
    {
        var releases = Ui.V(14);
        foreach (var release in manifest.Releases) releases.Children.Add(Ui.V(4, Ui.Txt(release.Version + (release.Date.Length > 0 ? " · " + release.Date : ""), 13, true), Ui.Muted(release.Notes).Wrap()));
        return releases;
    }
    private Task LoadImages() => LoadImages(false);
    private async Task LoadImages(bool refresh)
    {
        _gallery.Children.Clear();
        if (_listing.Manifest.Screenshots.Count == 0) { _gallery.Children.Add(Ui.Muted("作者尚未提供截图。")); return; }
        _gallery.Children.Add(Ui.Muted("正在校验并加载插件截图…"));
        try
        {
            var preview = await PluginCatalog.Preview(_listing, Path.Combine(Utils.ROOT, "cache", "plugin-previews"), refresh: refresh);
            _gallery.Children.Clear();
            foreach (string asset in preview.Manifest.Screenshots)
            {
                var picture = new LocalPicture(double.NaN, 220, "截图读取失败", contain: true, decodeWidth: 960);
                string path = Path.Combine(preview.Directory, asset.Replace('/', Path.DirectorySeparatorChar)); await picture.SetPath(path, preview.Directory, preview.Manifest.Name + "真实模块截图");
                if (!picture.Ready) throw new InvalidDataException("插件截图无法解码，请重试或联系插件作者。");
                var open = Ui.Btn("", BtnKind.Ghost, (_, _) =>
                {
                    var large = new LocalPicture(double.NaN, 420, "截图读取失败", contain: true, decodeWidth: 1600); _ = large.SetPath(path, preview.Directory, preview.Manifest.Name);
                    Dlg.Panel("插件截图", large, 820);
                });
                open.Content = picture; open.Padding = new Thickness(0); open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                System.Windows.Automation.AutomationProperties.SetName(open, "放大插件截图"); _gallery.Children.Add(open);
            }
        }
        catch (Exception ex) { Smoke.Note("plugin-preview", ex.ToString()); _gallery.Children.Clear(); _gallery.Children.Add(Ui.V(8, Ui.Muted("截图未能加载：" + ex.Message).Wrap(), Ui.Btn("重试截图", BtnKind.Ghost, (_, _) => PageBase.Run(() => LoadImages(true))))); }
    }
    private static string Kind(string kind) => kind switch { "notes" => "独立便签", "clock" => "时钟", "links" => "快捷链接", "image" => "图片", "system" => "系统状态", _ => kind };
}
