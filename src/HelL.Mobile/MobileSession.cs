using HelL.Core.Platform;
using HelL.Portable;

namespace HelL.Mobile;

internal sealed class MobileSession
{
    private static readonly object Gate = new();
    private static MobileSession? current;
    // The data owner survives Activity recreation; releasing its lock early would allow concurrent writers.
    private readonly PortableHome lease;
    private PortableAccount? account;
    private Remote.MobileRemoteController? remote;
    internal string Root { get; }
    internal PortableLauncher Launcher { get; }
    internal SemaphoreSlim Operations { get; } = new(1, 1);
    internal bool AccountRestored { get; set; }
    internal Ai.MobileChat Chat { get; } = new();
    internal Workflow.MobileOperationHistory Tasks { get; } = new();

    internal ISecretStore Secrets => PlatformServices.SecretStore ?? (OperatingSystem.IsAndroid()
        ? throw new InvalidOperationException("Android 凭据服务未就绪，请重新打开 HelL。")
        : SystemSecretStore.Create());

    private MobileSession(string root)
    {
        Root = root;
        lease = PortableHome.Open(root);
        try { Launcher = new PortableLauncher(root); }
        catch { lease.Dispose(); throw; }
    }

    internal static MobileSession Open(string directory)
    {
        string root = Path.GetFullPath(directory);
        lock (Gate)
        {
            if (current != null)
            {
                if (!string.Equals(current.Root, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException("请重启 HelL 后再切换应用数据目录。");
                return current;
            }
            return current = new MobileSession(root);
        }
    }

    internal PortableAccount Account => account ??= new PortableAccount(new AccountVault(Root, Secrets));

    internal PortableAccount? ExistingAccount => account;
    internal Remote.MobileRemoteController Remote => remote ??= MobileRuntime.RemotePreview && MobileRuntime.CreateRemotePreview is { } preview
        ? preview(Root, Secrets)
        : new Remote.MobileRemoteController(new HelL.Remote.RemotePairingStore(Path.Combine(Root, "remote"), "HelL 手机", new Remote.MobileRemoteSecrets(Secrets)));
}
