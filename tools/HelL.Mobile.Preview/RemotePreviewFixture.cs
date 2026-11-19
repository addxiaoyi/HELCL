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
        bool stopping = false;
        int snapshots = 0;
        return new MobileRemoteController(store, async (_, request, token) =>
        {
            if (request.Command == HostCommand.CancelTask) cancelled = true;
            else if (request.Command == HostCommand.StopGame) stopping = true;
            else if (request.Command == HostCommand.Snapshot)
            {
                int attempt = Interlocked.Increment(ref snapshots);
                await Task.Delay(800, token);
                if (attempt == 4) throw new IOException("isolated-preview-private-token");
            }
            return new HostReply { RequestId = request.RequestId, Device = "预览电脑", Ok = true,
                Detail = request.Command == HostCommand.TaskDetails ? new HostTaskDetails
                    { TaskId = request.TaskId, Message = "受控预览任务详情", RecentLogs = ["下载校验通过", "正在安装（本机路径已脱敏）"] } : null,
                Game = request.Command is HostCommand.GameStatus or HostCommand.StopGame ? new HostGameStatus
                    { Running = true, SessionId = new string('f', 32), State = stopping ? "stopping" : "running", Message = "受控预览游戏会话" } : null,
                Tasks = request.Command == HostCommand.Snapshot
                    ? [new HostTask { Id = "preview-install", Title = "安装 Fabric 资源 · 长标题窄屏任务状态验证", State = cancelled ? "failed" : "running", ProgressPercent = 42, Stage = "校验游戏资源", Speed = "2.4 MB/s" },
                       new HostTask { Id = "preview-finished", Title = "同步资源列表", State = "completed" }]
                    : [] };
        });
    }
}
