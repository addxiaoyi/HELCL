using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

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
            File.WriteAllText(Path.Combine(Program.DataRoot, "ui-smoke.json"), JsonSerializer.Serialize(new
            {
                ok = true, platform = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                widths = new[] { 1060, 800 }, instanceCreation = true, selection = true
            }));
            exitCode = 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(Program.DataRoot, "ui-smoke-error.txt"), ex.ToString()); }
        finally { (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(exitCode); }

        async Task Capture(int width, int height)
        {
            Width = width;
            Height = height;
            await Task.Delay(400);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(Program.DataRoot, $"ui-{width}.png"), new PngBitmapEncoderOptions());
        }
    }
}
