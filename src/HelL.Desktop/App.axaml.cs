using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace HelL.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        HelL.Core.Platform.PlatformServices.ImageCodec = new Services.DesktopImages();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                desktop.MainWindow = Program.StartupError is { } error ? ErrorWindow(error) : new MainWindow(Program.DataRoot);
            }
            catch (Exception ex) { desktop.MainWindow = ErrorWindow("初始化失败：" + ex.Message); }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static Window ErrorWindow(string error) => new()
    {
        Title = "HelL · 无法打开便携数据", Width = 560, Height = 240,
        Content = new TextBlock { Text = error, Margin = new Thickness(28), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
    };
}
