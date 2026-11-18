using HelL.Contracts;
using HelL.Core.Platform;
using HelL.Mobile.Remote;
using HelL.Remote;

internal static class RemotePreviewFixture
{
    internal static MobileRemoteController Create(string root, ISecretStore secrets)
    {
        var store = new RemotePairingStore(Path.Combine(root, "remote"), "HelL 手机", new MobileRemoteSecrets(secrets));
        bool cancelled = false;
        int snapshots = 0;
        return new MobileRemoteController(store, async (_, request, token) =>
        {
            if (request.Command == HostCommand.CancelTask) cancelled = true;
            else
            {
                int attempt = Interlocked.Increment(ref snapshots);
                await Task.Delay(800, token);
                if (attempt == 4) throw new IOException("isolated-preview-private-token");
            }
            return new HostReply { RequestId = request.RequestId, Device = "预览电脑", Ok = true,
                Tasks = request.Command == HostCommand.Snapshot
                    ? [new HostTask { Id = "preview-install", Title = "安装 Fabric 资源 · 长标题窄屏任务状态验证", State = cancelled ? "failed" : "running" },
                       new HostTask { Id = "preview-finished", Title = "同步资源列表", State = "completed" }]
                    : [] };
        });
    }
}
