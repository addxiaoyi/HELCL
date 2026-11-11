using System.Text.Json;
using Avalonia;
using HelL.Portable;

namespace HelL.Desktop;

internal static class Program
{
    internal static string DataRoot { get; private set; } = "";
    internal static string? StartupError { get; private set; }
    internal static bool UiSmoke { get; private set; }
    internal static bool CatalogSmoke { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        bool selfTest = args.Contains("--self-test", StringComparer.Ordinal);
        UiSmoke = args.Contains("--ui-smoke", StringComparer.Ordinal);
        CatalogSmoke = args.Contains("--catalog-ui-smoke", StringComparer.Ordinal);
        PortableHome? home = null;
        try
        {
            string requested = Environment.GetEnvironmentVariable("TIPCL_HOME") ?? "";
            if ((selfTest || UiSmoke || CatalogSmoke) && string.IsNullOrWhiteSpace(requested))
                throw new ArgumentException("自检需要独立的 TIPCL_HOME，避免改动日常数据。");
            DataRoot = PortableHome.Resolve(AppContext.BaseDirectory, requested, OperatingSystem.IsMacOS());
            home = PortableHome.Open(DataRoot);
            if (selfTest)
            {
                var launcher = new PortableLauncher(DataRoot);
                string name = "便携自检-" + Guid.NewGuid().ToString("N")[..8];
                launcher.CreateInstance(name);
                launcher.SelectInstance(name);
                if (!launcher.InstancesList().Any(instance => instance.Name == name))
                    throw new IOException("实例读写自检失败。");
                string report = JsonSerializer.Serialize(new { ok = true, root = DataRoot, instance = name,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier });
                File.WriteAllText(Path.Combine(DataRoot, "self-test.json"), report);
                Console.WriteLine(report);
                return 0;
            }
        }
        catch (Exception ex)
        {
            home?.Dispose();
            home = null;
            if (selfTest || UiSmoke || CatalogSmoke) { Console.Error.WriteLine(ex.Message); return 1; }
            StartupError = ex.Message;
        }

        using (home) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
