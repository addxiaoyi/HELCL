using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HelL.Desktop.Services;
using HelL.Core;
using HelL.Core.Launcher;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async Task CheckInstanceContextAsync(List<string> failures)
    {
        string[] names = ["生存服 · Fabric", "建筑服 · 原版"];
        foreach (string name in names)
        {
            launcher.CreateInstance(name);
            var instance = new Instance(name);
            foreach (string version in new[] { "1.21.1", "1.20.1" })
            {
                string game = Path.Combine(instance.VersionsDir(), version);
                Directory.CreateDirectory(game);
                Utils.WriteJson(Path.Combine(game, version + ".json"), PyDict.Of("id", version));
                File.WriteAllText(Path.Combine(game, version + ".jar"), "fixture client");
                VersionSettings.Save(instance, version, PyDict.Of("isolation", "all"));
                Directory.CreateDirectory(Path.Combine(game, "mods"));
                File.WriteAllText(Path.Combine(game, "mods", name + "-" + version + ".jar"), "fixture mod");
                Directory.CreateDirectory(Path.Combine(game, "saves", name + "-" + version));
                var content = new Instance(name) { path = game };
                Servers.AddServer(content, name + "-" + version, "fixture.invalid");
            }
        }
        foreach (string name in names)
        {
            OpenInstance(name);
            InstalledVersions.SelectedItem = "1.21.1";
            ShowPage("mods");
            await JourneyCapture("context-" + Array.IndexOf(names, name) + "-mods", 1180, 760);
            if (!ModsView.FindControl<ListBox>("ContentList")!.Items.OfType<Views.ModsManagerView.ContentRow>()
                .Any(row => row.DisplayName.Contains(name + "-1.21.1"))) failures.Add("模组列表没有读取所选实例的隔离版本：" + name);
            ShowPage("saves");
            if (!SavesView.FindControl<ListBox>("WorldsList")!.Items.OfType<Views.SavesManagerView.WorldRow>()
                .Any(row => row.Name == name + "-1.21.1")) failures.Add("存档列表没有读取所选实例的隔离版本：" + name);
            ShowPage("servers");
            if (!ServersView.FindControl<ListBox>("ServerList")!.Items.OfType<Views.ServersView.ServerRow>()
                .Any(row => row.Name == name + "-1.21.1")) failures.Add("服务器列表没有读取所选实例的隔离版本：" + name);
            ShowPage("launch");
            InstalledVersions.SelectedItem = "1.20.1";
            ShowPage("mods");
            if (!ModsView.FindControl<ListBox>("ContentList")!.Items.OfType<Views.ModsManagerView.ContentRow>()
                .Any(row => row.DisplayName.Contains(name + "-1.20.1"))) failures.Add("切换游戏版本后仍显示旧内容：" + name);
        }
        OpenInstance(names[0]);
        if (InstalledVersions.SelectedItem as string != "1.20.1") failures.Add("切回实例后丢失上次选择的版本。");
        string configBackup = Config.CONFIG_FILE + ".selection-fixture";
        File.Move(Config.CONFIG_FILE, configBackup);
        Directory.CreateDirectory(Config.CONFIG_FILE);
        try
        {
            InstanceList.SelectedItem = InstanceList.Items.OfType<HelL.Portable.InstanceEntry>().Single(item => item.Name == names[1]);
            if (SelectedName != names[0] || launcher.SelectedInstance != names[0]) failures.Add("实例选择保存失败后侧栏与当前内容不一致。");
        }
        finally { Directory.Delete(Config.CONFIG_FILE); File.Move(configBackup, Config.CONFIG_FILE); }
        OpenInstance(names[0]);
        ShowPage("mods");
        await JourneyCapture("context-toggle-mod", 1180, 760);
        var toggle = ModsView.GetVisualDescendants().OfType<Button>().First(button => button.Content?.ToString() == "禁用");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await JourneyIdle();
        string folder = Path.Combine(new Instance(names[0]).VersionsDir(), "1.20.1", "mods");
        if (!File.Exists(Path.Combine(folder, names[0] + "-1.20.1.jar.disabled"))) failures.Add("禁用操作没有写入显示的版本目录。");
        if (!File.Exists(Path.Combine(new Instance(names[1]).VersionsDir(), "1.20.1", "mods", names[1] + "-1.20.1.jar"))) failures.Add("禁用操作改动了其他实例。");
        ShowPage("servers");
        await JourneyCapture("context-servers", 1180, 760);
        DesktopDialogs.Confirmation = (_, _) => Task.FromResult(true);
        try
        {
            ServersView.GetVisualDescendants().OfType<Button>().First(button => button.Tag is long).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await JourneyIdle();
            OpenInstance(names[1]);
            ShowPage("servers");
            ServersView.FindControl<Button>("UndoRemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await JourneyIdle();
            if (ServersView.FindControl<ListBox>("ServerList")!.Items.OfType<Views.ServersView.ServerRow>().Any(row => !row.Name.StartsWith(names[1])))
                failures.Add("撤销移除使服务器列表跳回另一个实例。");
        }
        finally { DesktopDialogs.Confirmation = null; }
        OpenInstance(names[0]);
        InstalledVersions.SelectedItem = "1.20.1";
        foreach (var mode in new[] { (Isolation: "saves", Kind: "mods", Extension: ".jar"), (Isolation: "mods", Kind: "resourcepacks", Extension: ".zip") })
        {
            var instance = new Instance(names[0]);
            string version = mode.Isolation == "saves" ? "1.21.1" : "1.20.1";
            InstalledVersions.SelectedItem = version;
            string versionFolder = Path.Combine(instance.VersionsDir(), version, mode.Kind);
            if (mode.Isolation == "saves")
                foreach (string file in Directory.GetFiles(versionFolder)) File.Delete(file);
            VersionSettings.SetIsolation(instance, version, mode.Isolation);
            string filename = "共享目录导入-" + mode.Kind + mode.Extension;
            string source = Path.Combine(Program.DataRoot, filename);
            File.WriteAllText(source, "shared import fixture");
            await ModsView.ImportPathsAsync(names[0], mode.Kind, new[] { source });
            if (!File.Exists(Path.Combine(instance.path, mode.Kind, filename))) failures.Add("部分隔离模式没有写入共享目录：" + mode.Isolation);
        }
    }

    private async Task CheckAppearanceCommitAsync(List<string> failures)
    {
        ShowPage("settings");
        string theme = CurrentTheme, glass = CurrentGlass;
        long revision = Config.CONFIG.revision;
        byte[] saved = File.ReadAllBytes(Config.CONFIG_FILE);
        string backup = Config.CONFIG_FILE + ".fixture";
        File.Move(Config.CONFIG_FILE, backup);
        Directory.CreateDirectory(Config.CONFIG_FILE);
        try
        {
            SelectTheme(theme == "light" ? "dark" : "light");
            SelectGlassOpacity(glass == "ultra" ? "solid" : "ultra");
            if (CurrentTheme != theme || CurrentGlass != glass || Config.CONFIG.revision != revision)
                failures.Add("外观保存失败后界面或内存偏好仍被更改。");
        }
        finally { Directory.Delete(Config.CONFIG_FILE); File.Move(backup, Config.CONFIG_FILE); }
        await JourneyCapture("settings-save-failed", 800, 600);
        if (!saved.SequenceEqual(File.ReadAllBytes(Config.CONFIG_FILE))) failures.Add("失败外观保存改变了原配置文件。");
        SelectTheme("dark");
        SelectGlassOpacity("ultra");
        var persisted = new LauncherConfig();
        if (Py.S(persisted.Get("portable_theme")) != "dark" || Py.S(persisted.Get("portable_glass")) != "ultra")
            failures.Add("重新载入配置没有恢复已保存的外观。");
        var threads = SettingsView.FindControl<TextBox>("ThreadsBox")!;
        string original = threads.Text ?? "8";
        threads.Text = "17";
        bool prompted = false;
        DesktopDialogs.Confirmation = (_, _) => { prompted = true; return Task.FromResult(false); };
        try
        {
            Close();
            await Task.Delay(30);
            if (!IsVisible || !prompted || threads.Text != "17") failures.Add("退出未提醒未保存设置或未保留草稿。");
            prompted = false;
            var waiting = RunTask(token => Task.Delay(Timeout.Infinite, token), "DraftExitFixture");
            Close();
            await waiting;
            await Task.Delay(30);
            if (!IsVisible || !prompted || IsClosing) failures.Add("取消后台任务并拒绝退出后窗口没有恢复正常状态。");
            await RunTask(_ => Task.CompletedTask, "AfterDraftExitFixture");
            if (!IsVisible || IsClosing) failures.Add("取消退出后再次执行任务仍触发关闭。");
        }
        finally { DesktopDialogs.Confirmation = null; threads.Text = original; }
    }
}
