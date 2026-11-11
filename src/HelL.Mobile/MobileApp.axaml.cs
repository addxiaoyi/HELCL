using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HelL.Core.Platform;

namespace HelL.Mobile;

public sealed partial class MobileApp : Application
{
    private Control? activityView;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        PlatformServices.ImageCodec = new HelL.Desktop.Services.DesktopImages();
        if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => activityView ??= CreateHome();
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = CreateHome();
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && MobileRuntime.Smoke)
        {
            desktop.MainWindow = new Window { Title = "HelL · Android 页面预览", Width = 390, Height = 844,
                Content = new MobileHome(MobileRuntime.Root) };
            desktop.MainWindow.Opened += async (_, _) =>
            {
                DateTime timeout = DateTime.UtcNow.AddSeconds(60);
                while (!File.Exists(Path.Combine(MobileRuntime.Root, "mobile-smoke.json")) && !File.Exists(Path.Combine(MobileRuntime.Root, "mobile-smoke-error.txt")) && DateTime.UtcNow < timeout)
                    await Task.Delay(200);
                desktop.Shutdown(File.Exists(Path.Combine(MobileRuntime.Root, "mobile-smoke.json")) ? 0 : 1);
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static Control CreateHome()
    {
        try { return new MobileHome(MobileRuntime.Root); }
        catch (Exception error)
        {
            return new TextBlock { Text = "无法打开数据目录：" + error.Message, Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        }
    }
}
