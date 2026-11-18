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
            activity.MainViewFactory = () =>
            {
                ReleaseActivityView();
                return activityView = CreateHome();
            };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = CreateHome();
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && (MobileRuntime.Smoke || MobileRuntime.KeepPreviewOpen || MobileRuntime.RemotePreview))
        {
            desktop.MainWindow = new Window { Title = "HelL · 移动界面预览", Width = MobileRuntime.PreviewWidth, Height = MobileRuntime.PreviewHeight,
                Content = new MobileHome(MobileRuntime.Root) };
            if (MobileRuntime.RemotePreview)
                desktop.MainWindow.Opened += async (_, _) => desktop.Shutdown(await ((MobileHome)desktop.MainWindow.Content!).RunRemotePreviewAsync() ? 0 : 1);
            if (MobileRuntime.Smoke)
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    DateTime timeout = DateTime.UtcNow.AddSeconds(MobileRuntime.LiveCatalogPreview ? 180 : 60);
                    while (!File.Exists(Path.Combine(MobileRuntime.Root, "mobile-smoke.json")) && !File.Exists(Path.Combine(MobileRuntime.Root, "mobile-smoke-error.txt")) && DateTime.UtcNow < timeout)
                        await Task.Delay(200);
                    if (!MobileRuntime.KeepPreviewOpen)
                        desktop.Shutdown(File.Exists(Path.Combine(MobileRuntime.Root, "mobile-smoke.json")) ? 0 : 1);
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    public void ReleaseActivityView()
    {
        (activityView as IDisposable)?.Dispose();
        activityView = null;
    }

    private static Control CreateHome()
    {
        try { return new MobileHome(MobileRuntime.Root); }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("MobileApp.CreateHome: " + error);
            return new TextBlock { Text = "无法打开数据目录：" + error.Message, Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        }
    }
}
