using Avalonia;
using HelL.Mobile;
using HelL.Portable;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        bool interactive = args.Contains("--interactive", StringComparer.Ordinal);
        bool remote = args.Contains("--remote", StringComparer.Ordinal);
        string[] positional = args.Where(arg => arg != "--interactive" && arg != "--remote").ToArray();
        if (positional.Length is not (1 or 3) || !Path.IsPathFullyQualified(positional[0]) || Directory.Exists(positional[0])) return 2;
        if (positional.Length == 3)
        {
            if (!int.TryParse(positional[1], out int width) || !int.TryParse(positional[2], out int height)
                || width is < 240 or > 2400 || height is < 240 or > 2400) return 2;
            MobileRuntime.PreviewWidth = width;
            MobileRuntime.PreviewHeight = height;
        }
        MobileRuntime.Root = positional[0];
        if (interactive)
        {
            Directory.CreateDirectory(MobileRuntime.Root);
            var seed = new PortableLauncher(MobileRuntime.Root);
            if (seed.InstancesList().Count == 0)
            {
                seed.CreateInstance("1.21.1 Fabric · 光影生存");
                seed.GroupInstance("1.21.1 Fabric · 光影生存", "移动精选");
                seed.PinInstance("1.21.1 Fabric · 光影生存", true);
                seed.SetReferenceVersion("1.21.1 Fabric · 光影生存", "1.21.1", "fabric");
                seed.CreateInstance("1.20.1 Forge · 机械动力");
                seed.SetReferenceVersion("1.20.1 Forge · 机械动力", "1.20.1", "forge");
                seed.CreateInstance("1.21.1 原版纯粹生存");
                seed.SetReferenceVersion("1.21.1 原版纯粹生存", "1.21.1", "vanilla");
                seed.SelectInstance("1.21.1 Fabric · 光影生存");
            }
        }
        MobileRuntime.RemotePreview = remote;
        if (remote) MobileRuntime.BeginRemotePreview = () => HelL.Core.Platform.PlatformServices.SecretStore = new PreviewKeys();
        if (remote) MobileRuntime.CreateRemotePreview = RemotePreviewFixture.Create;
        MobileRuntime.Smoke = !interactive && !remote;
        MobileRuntime.LiveCatalogPreview = !interactive && Environment.GetEnvironmentVariable("HELL_MOBILE_LIVE_CATALOG_PREVIEW") == "1";
        MobileRuntime.KeepPreviewOpen = interactive || Environment.GetEnvironmentVariable("HELL_MOBILE_KEEP_OPEN") == "1";
        if (!interactive) MobileRuntime.BeginAiSmoke = () => new AiReplayFixture();
        return AppBuilder.Configure<MobileApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }

    private sealed class PreviewKeys : HelL.Core.Platform.ISecretStore
    {
        private readonly Dictionary<(string, string), string> keys = [];
        public string GetPassword(string service, string username) => keys.GetValueOrDefault((service, username), "");
        public void SetPassword(string service, string username, string password) => keys[(service, username)] = password;
        public void DeletePassword(string service, string username) => keys.Remove((service, username));
    }
}
