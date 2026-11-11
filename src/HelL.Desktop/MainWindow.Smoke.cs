using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HelL.Desktop;

public sealed partial class MainWindow
{
    private async void RunUiSmoke(object? sender, EventArgs e)
    {
        int exitCode = 1;
        try
        {
            await Task.Delay(600);
            if (InstanceList.ItemCount != 0) throw new InvalidOperationException("界面自检要求空数据目录。");
            InstanceName.Text = "中文生存世界 · 跨平台便携测试";
            CreateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (InstanceList.ItemCount != 1 || !Workspace.IsEnabled || !InstanceTitle.Text!.StartsWith("中文生存世界"))
                throw new InvalidOperationException("实例创建后的界面状态不正确。");
            await Capture(1060, 720);
            await Capture(800, 600);
            await CheckLibraryAndResources(Capture);
            File.WriteAllText(Path.Combine(Program.DataRoot, "ui-smoke.json"), JsonSerializer.Serialize(new
            {
                ok = true, platform = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                widths = new[] { 1060, 800 }, instanceCreation = true, selection = true, library = true, resources = true
            }));
            exitCode = 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Program.DataRoot, "ui-smoke-error.txt"), ex.ToString()); }
        finally { (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(exitCode); }

        async Task Capture(int width, int height, string page = "")
        {
            Width = width;
            Height = height;
            await Task.Delay(400);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(Program.DataRoot, $"ui-{page}{width}.png"), new PngBitmapEncoderOptions());
        }
    }

    private async Task CheckLibraryAndResources(Func<int, int, string, Task> capture)
    {
        var name = (InstanceList.SelectedItem as HelL.Portable.InstanceEntry)!.Name;
        launcher.CreateInstance("创造世界 · 界面验收夹具");
        LibraryView.Reload(name);
        ShowPage("library");
        LibraryView.FindControl<TextBox>("RenameBox")!.Text = "中文世界 · 已改名";
        LibraryView.FindControl<TextBox>("GroupBox")!.Text = "周末联机";
        var save = LibraryView.FindControl<Button>("SaveButton")!;
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        LibraryView.FindControl<Button>("PinButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var entry = launcher.LibraryList().First(row => row.Name == "中文世界 · 已改名");
        if (entry.Group != "周末联机" || !entry.Pinned) throw new InvalidOperationException("实例编辑界面未保存分组和固定状态。");
        await capture(1060, 720, "library-");
        await capture(800, 600, "library-");
        LibraryView.FindControl<Button>("ViewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await capture(1060, 720, "covers-");
        await capture(800, 600, "covers-");
        LibraryView.FindControl<TextBox>("SearchBox")!.Text = "没有这个实例";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (LibraryView.FindControl<ListBox>("LibraryList")!.ItemCount != 0) throw new InvalidOperationException("实例筛选未生效。");
        await capture(800, 600, "empty-");
        LibraryView.FindControl<TextBox>("SearchBox")!.Text = "";
        ShowPage("resources");
        await capture(800, 600, "resources-empty-");
        var project = new HelL.Portable.CatalogProject("fixture", "fixture", "modrinth", "resourcepack", "界面验收资源包 · 长中文名称与版本选择", "UI fixture", "测试夹具，非上游资源", "用于检查长标题换行、选择文件和原文切换。不发起网络请求。", "Offline UI fixture; no project artwork is substituted.", "", 0);
        ResourcesView.FindControl<ListBox>("Results")!.ItemsSource = new[] { project };
        await capture(1060, 720, "resources-");
        await capture(800, 600, "resources-");
        ResourcesView.PresentProject(project);
        var releases = ResourcesView.FindControl<ComboBox>("ReleasePicker")!;
        releases.ItemsSource = new[] { new HelL.Portable.CatalogRelease("fixture-file", "验收版本 · 长文件名称", "fixture-resourcepack.zip", new[] { "1.21.1" }, Array.Empty<string>(), "2026-11-11", 0, new HelL.Core.PyDict()) };
        releases.SelectedIndex = 0;
        if (ResourcesView.FindControl<Button>("DownloadButton")!.IsEnabled) throw new InvalidOperationException("尚未安装游戏时应阻止资源安装。");
        ResourcesView.FindControl<Button>("OriginalButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!ResourcesView.FindControl<TextBlock>("ProjectSummary")!.Text!.Contains("Offline")) throw new InvalidOperationException("原文切换未生效。");
        await capture(1060, 720, "resource-detail-");
        await capture(800, 600, "resource-detail-");
    }
}
