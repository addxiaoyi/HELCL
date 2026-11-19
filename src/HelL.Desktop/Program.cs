using System.Text.Json;
using Avalonia;
using HelL.Portable;

namespace HelL.Desktop;

internal static class Program
{
    internal static string DataRoot { get; private set; } = "";
    internal static string? StartupError { get; private set; }
    internal static bool UiSmoke { get; private set; }
    internal static bool AuthUiSmoke { get; private set; }
    internal static bool CatalogSmoke { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        bool selfTest = args.Contains("--self-test", StringComparer.Ordinal);
        bool seedPreview = args.Contains("--seed-preview", StringComparer.Ordinal);
        AuthUiSmoke = args.Contains("--auth-ui-smoke", StringComparer.Ordinal);
        UiSmoke = AuthUiSmoke || args.Contains("--ui-smoke", StringComparer.Ordinal);
        CatalogSmoke = args.Contains("--catalog-ui-smoke", StringComparer.Ordinal);
        PortableHome? home = null;
        try
        {
            int homeIndex = Array.IndexOf(args, "--home");
            string? argHome = homeIndex >= 0 && homeIndex + 1 < args.Length ? args[homeIndex + 1] : null;
            string requested = !string.IsNullOrWhiteSpace(argHome) ? argHome : (Environment.GetEnvironmentVariable("TIPCL_HOME") ?? "");
            if ((selfTest || UiSmoke || CatalogSmoke) && string.IsNullOrWhiteSpace(requested))
                throw new ArgumentException("自检需要独立的 TIPCL_HOME，避免改动日常数据。");
            if (new[] { selfTest, UiSmoke, CatalogSmoke }.Count(enabled => enabled) > 1)
                throw new ArgumentException("请一次运行一种自检模式。");
            if (AuthUiSmoke && (!Uri.TryCreate(Environment.GetEnvironmentVariable("TIPCL_HTTP_REPLAY"), UriKind.Absolute, out var replay)
                || replay.Scheme != "http" || !replay.IsLoopback))
                throw new ArgumentException("账号界面自检只能使用本机回放服务器。");
            DataRoot = PortableHome.Resolve(AppContext.BaseDirectory, requested, OperatingSystem.IsMacOS());
            home = PortableHome.Open(DataRoot);
            if (seedPreview && !selfTest && !UiSmoke && !CatalogSmoke)
            {
                var seed = new PortableLauncher(DataRoot);
                if (seed.InstancesList().Count == 0)
                {
                    const string primary = "1.21.1 Fabric · 樱花光影生存";
                    const string createPack = "1.20.1 Forge · 机械动力工业";
                    const string modpackInst = "1.20.4 暮色整合包探险";
                    const string vanillaInst = "1.21.4 原版红石建筑";

                    seed.CreateInstance(primary);
                    seed.GroupInstance(primary, "生存整合");
                    seed.PinInstance(primary, true);
                    seed.SetReferenceVersion(primary, "1.21.1", "fabric");

                    seed.CreateInstance(createPack);
                    seed.GroupInstance(createPack, "科技模组");
                    seed.PinInstance(createPack, true);
                    seed.SetReferenceVersion(createPack, "1.20.1", "forge");

                    seed.CreateInstance(modpackInst);
                    seed.GroupInstance(modpackInst, "冒险整合");
                    seed.SetReferenceVersion(modpackInst, "1.20.4", "quilt");

                    seed.CreateInstance(vanillaInst);
                    seed.GroupInstance(vanillaInst, "原版建筑");
                    seed.SetReferenceVersion(vanillaInst, "1.21.4", "vanilla");

                    seed.SelectInstance(primary);

                    string pRoot = seed.InstanceFolder(primary);
                    Directory.CreateDirectory(Path.Combine(pRoot, "mods"));
                    File.WriteAllBytes(Path.Combine(pRoot, "mods", "sodium-fabric-0.6.5+mc1.21.1.jar"), [0x50, 0x4B, 0x03, 0x04]);
                    File.WriteAllBytes(Path.Combine(pRoot, "mods", "iris-fabric-1.8.1+mc1.21.1.jar"), [0x50, 0x4B, 0x03, 0x04]);
                    File.WriteAllBytes(Path.Combine(pRoot, "mods", "lithium-fabric-0.14.7+mc1.21.1.jar"), [0x50, 0x4B, 0x03, 0x04]);
                    File.WriteAllBytes(Path.Combine(pRoot, "mods", "xaeros-minimap-24.6.1.jar.disabled"), [0x50, 0x4B, 0x03, 0x04]);
                    Directory.CreateDirectory(Path.Combine(pRoot, "shaderpacks"));
                    File.WriteAllBytes(Path.Combine(pRoot, "shaderpacks", "ComplementaryUnbound_r5.3.zip"), [0x50, 0x4B, 0x03, 0x04]);
                    Directory.CreateDirectory(Path.Combine(pRoot, "saves", "樱花城堡生存存档"));
                    File.WriteAllText(Path.Combine(pRoot, "saves", "樱花城堡生存存档", "level.dat"), "HelsteraL preview world fixture");
                    Directory.CreateDirectory(Path.Combine(pRoot, "logs"));
                    File.WriteAllText(Path.Combine(pRoot, "logs", "latest.log"),
                        "[14:20:01] [main/INFO]: Loading Minecraft 1.21.1 with Fabric Loader 0.16.9\n" +
                        "[14:20:02] [main/INFO]: Loading 58 mods: sodium 0.6.5, iris 1.8.1, lithium 0.14.7\n" +
                        "[14:20:04] [Render thread/INFO]: [Iris] Loaded shaderpack: ComplementaryUnbound_r5.3.zip\n" +
                        "[14:20:06] [Render thread/INFO]: Created: 2048x1024x4 minecraft:textures/atlas/blocks.png-atlas\n");

                    var instObj = new HelL.Core.Launcher.Instance(primary);
                    HelL.Core.Launcher.Servers.AddServer(instObj, "HelsteraMC 官方公益生存服", "play.helstera.mc", 25565L, "1.21.1 多元生存 · 极速BGP");
                    HelL.Core.Launcher.Servers.AddServer(instObj, "机械动力联机创造服", "create.example.org", 25565L, "1.20.1 蒸汽工业社区");
                    HelL.Core.Launcher.Playtime.RecordSession(primary, "1.21.1", 19800);
                    HelL.Core.Launcher.Playtime.RecordSession(createPack, "1.20.1", 11400);
                    HelL.Core.Launcher.Playtime.RecordSession(vanillaInst, "1.21.4", 4500);
                }
            }
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
            if (selfTest || UiSmoke || CatalogSmoke) { Console.Error.WriteLine(ex); return 1; }
            StartupError = ex.Message;
        }

        using (home) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
