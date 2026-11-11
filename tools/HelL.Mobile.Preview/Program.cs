using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HelL.Mobile;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || Directory.Exists(args[0])) return 2;
        MobileRuntime.Root = args[0];
        MobileRuntime.Smoke = true;
        return AppBuilder.Configure<MobileApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
