using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private void InitializeDesktopNavigation(string root)
    {
        SettingsDataDirectory.Text = root;
        string modifier = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";
        ShortcutHelp.Text = $"{modifier} + 1  启动游戏\n{modifier} + 2  实例库\n{modifier} + 3  资源下载\n{modifier} + ,  设置与数据\n{modifier} + F  搜索当前页面";
        PlatformNotice.Text = OperatingSystem.IsMacOS()
            ? "macOS 预览包为 ad-hoc 签名，未获得 Apple 公证。首次打开请按系统提示，在隐私与安全性中确认。数据保存在应用旁的 data 文件夹。"
            : "当前为预览版。Windows 包尚未获得正式发布者签名；请从项目构建页获取，并核对随包的 SHA-256。";
        AddHandler(KeyDownEvent, DesktopShortcut, RoutingStrategies.Tunnel);
        if (!OperatingSystem.IsMacOS()) return;
        var pages = new NativeMenu();
        AddPage("启动游戏", "launch", Key.D1);
        AddPage("实例库", "library", Key.D2);
        AddPage("资源下载", "resources", Key.D3);
        AddPage("设置与数据", "settings", Key.OemComma);
        NativeMenu.SetMenu(this, new NativeMenu { Items = { new NativeMenuItem("工作区") { Menu = pages } } });

        void AddPage(string title, string page, Key key)
        {
            var menu = new NativeMenuItem(title) { Gesture = new KeyGesture(key, KeyModifiers.Meta) };
            menu.Click += (_, _) => { if (task == null) ShowPage(page); };
            pages.Items.Add(menu);
        }
    }

    private void DesktopShortcut(object? sender, KeyEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (task != null || e.KeyModifiers != modifier) return;
        string? page = e.Key switch { Key.D1 => "launch", Key.D2 => "library", Key.D3 => "resources", Key.OemComma => "settings", _ => null };
        if (page != null) { ShowPage(page); e.Handled = true; }
        if (e.Key != Key.F) return;
        TextBox? search = LibraryView.IsVisible ? LibraryView.FindControl<TextBox>("SearchBox")
            : ResourcesView.IsVisible ? ResourcesView.FindControl<TextBox>("QueryBox") : null;
        if (search is { IsVisible: true }) { search.Focus(); search.SelectAll(); e.Handled = true; }
    }

    private async void OpenDataDirectory(object? sender, RoutedEventArgs e) => await RunTask(async _ =>
    {
        if (!await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(DataDirectory.Text!)))
            throw new IOException("无法打开文件管理器，请手动复制数据目录路径。");
    });
}
