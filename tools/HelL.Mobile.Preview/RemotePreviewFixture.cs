using HelL.Contracts;
using HelL.Core.Platform;
using HelL.Mobile;
using HelL.Mobile.Remote;
using HelL.Remote;

internal static class RemotePreviewFixture
{
    internal static MobileRemoteController Create(string root, ISecretStore secrets)
    {
        var store = new RemotePairingStore(Path.Combine(root, "remote"), "HelL 手机", new MobileRemoteSecrets(secrets));
        bool cancelled = false;
        bool stopping = false;
        int failNextSnapshot = 0;
        int failNextGame = 0;
        MobileRuntime.FailNextRemoteSnapshot = () => Interlocked.Exchange(ref failNextSnapshot, 1);
        MobileRuntime.FailNextRemoteGameStatus = () => Interlocked.Exchange(ref failNextGame, 1);
        return new MobileRemoteController(store, async (_, request, token) =>
        {
            if (request.Command == HostCommand.CancelTask) cancelled = true;
            else if (request.Command == HostCommand.StopGame) stopping = true;
            else if (request.Command == HostCommand.GameStatus && Interlocked.Exchange(ref failNextGame, 0) != 0)
                throw new IOException("isolated-preview-game-status-private-token");
            else if (request.Command == HostCommand.Snapshot)
            {
                await Task.Delay(800, token);
                if (Interlocked.Exchange(ref failNextSnapshot, 0) != 0) throw new IOException("isolated-preview-private-token");
            }
            return new HostReply { RequestId = request.RequestId, Device = "预览电脑", Ok = true,
                Detail = request.Command == HostCommand.TaskDetails ? new HostTaskDetails
                    { TaskId = request.TaskId, Message = "受控预览任务详情", RecentLogs = ["下载校验通过", "正在安装（本机路径已脱敏）"] } : null,
                Game = request.Command is HostCommand.GameStatus or HostCommand.StopGame ? new HostGameStatus
                    { Running = !stopping || request.Command == HostCommand.StopGame,
                      SessionId = stopping && request.Command == HostCommand.GameStatus ? "" : new string('f', 32),
                      State = stopping ? request.Command == HostCommand.GameStatus ? "stopped" : "stopping" : "running",
                      Message = "受控预览游戏会话" } : null,
                Tasks = request.Command == HostCommand.Snapshot
                    ? [new HostTask { Id = "preview-install", Title = "安装 Fabric 资源 · 长标题窄屏任务状态验证", State = cancelled ? "failed" : "running", ProgressPercent = 42, Stage = "校验游戏资源", Speed = "2.4 MB/s" },
                       new HostTask { Id = "preview-finished", Title = "同步资源列表", State = "completed" }]
                    : [] };
        });
    }
}
