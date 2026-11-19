using HelL.Contracts;
using HelL.Remote;

namespace HelL.Mobile.Remote;

public sealed partial class MobileRemoteController
{
    private bool autoReconnect;
    public HostTaskDetails? Detail { get; private set; }
    public bool TasksTruncated { get; private set; }
    public HostGameStatus? Game { get; private set; }
    public bool CanControlGame => Invitation is { } invite && invite.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        && pairings.Find(invite.Host.DeviceId)?.Permissions.Contains(HostPermission.ControlGame) == true;

    public async Task RestoreConnectionAsync(CancellationToken cancellation = default)
    {
        ThrowIfDisposed();
        autoReconnect = true;
        if (Invitation is not { } invite || Busy) return;
        if (invite.Feed?.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds() && !Watching) StartUpdates();
        if (invite.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) await RefreshAsync(cancellation).ConfigureAwait(false);
    }

    public Task ReadTaskDetailsAsync(string taskId, CancellationToken cancellation = default)
    {
        if (!Tasks.Any(task => task.Id == taskId)) throw new InvalidOperationException("请先刷新任务，再查看任务详情。");
        return RunCommandAsync(new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.TaskDetails, TaskId = taskId }, cancellation);
    }

    public Task ReadGameAsync(CancellationToken cancellation = default) => RunCommandAsync(
        new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.GameStatus }, cancellation);

    public Task StopGameAsync(string sessionId, bool confirmed, CancellationToken cancellation = default)
    {
        if (!confirmed) throw new InvalidOperationException("请先确认当前游戏会话，再请求正常退出。");
        if (!CanControlGame) throw new UnauthorizedAccessException("这台手机未获准请求游戏退出。");
        if (Game is not { Running: true } current || current.SessionId != sessionId)
            throw new InvalidOperationException("游戏会话已变化。请刷新状态并重新确认。");
        return RunCommandAsync(new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.StopGame, SessionId = sessionId }, cancellation);
    }

    private async Task RunCommandAsync(HostRequest request, CancellationToken cancellation)
    {
        ThrowIfDisposed();
        request.Validate();
        RemoteHostInvite invite;
        int version;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        pending.CancelAfter(TimeSpan.FromSeconds(35));
        lock (stateGate)
        {
            if (operation is not null) throw new InvalidOperationException("请等待当前请求完成，或先取消请求。");
            invite = Invitation ?? throw new InvalidOperationException("请先导入所选电脑的邀请。");
            ValidateInvitation(invite);
            if (request.Command == HostCommand.StopGame && (!CanControlGame || Game is not { Running: true } current || current.SessionId != request.SessionId))
                throw new UnauthorizedAccessException("所选电脑、游戏会话或控制权限已变化。请刷新并重新确认。");
            version = revision;
            operation = pending;
            Error = "";
        }
        Changed?.Invoke();
        try
        {
            HostReply reply = await send(invite, request, pending.Token).WaitAsync(pending.Token).ConfigureAwait(false);
            pending.Token.ThrowIfCancellationRequested();
            if (reply.Protocol != HostProtocol.Version || reply.RequestId != request.RequestId)
                throw new IOException("The host did not confirm this request.");
            lock (stateGate)
            {
                if (version != revision || disposed) return;
                if (!reply.Ok)
                {
                    Error = reply.ErrorCode switch
                    {
                        "permission_denied" => "电脑拒绝了此操作。请在电脑上检查这台手机的授权。",
                        "game_not_running" => "电脑上的游戏已经退出。请刷新游戏状态。",
                        "game_session_changed" => "电脑上的游戏会话已变化。请刷新状态并重新确认。",
                        "graceful_stop_unavailable" => "当前游戏没有可接受正常退出请求的窗口。请在电脑上退出游戏；不会强制结束进程。",
                        "task_not_found" => "该任务已不在电脑的近期记录中。请刷新任务列表。",
                        "invalid_request" => "电脑无法识别此请求。请确认手机和电脑版本兼容。",
                        "request_conflict" => "此请求标识已用于另一项操作。请刷新后重新操作。",
                        _ => "电脑未能完成此操作。请检查电脑状态后刷新。"
                    };
                    return;
                }
                if (request.Command == HostCommand.TaskDetails)
                {
                    if (reply.Detail is null || reply.Detail.TaskId != request.TaskId) throw new InvalidDataException("The task details do not match this request.");
                    if (reply.Detail.RecentLogs is null || reply.Detail.RecentLogs.Length > 20
                        || reply.Detail.RecentLogs.Any(line => line is null || line.Length > 512)
                        || reply.Detail.Message is null || reply.Detail.Message.Length > 1024)
                        throw new InvalidDataException("The task details exceed the agreed response limits.");
                    Detail = reply.Detail;
                }
                else
                {
                    if (reply.Game is null) throw new InvalidDataException("The host did not return game status.");
                    if (reply.Game.Message is null || reply.Game.Message.Length > 1024 || reply.Game.SessionId is null
                        || reply.Game.State is not ("stopped" or "running" or "stopping")
                        || (reply.Game.Running && !Guid.TryParseExact(reply.Game.SessionId, "N", out _)))
                        throw new InvalidDataException("The host game status is invalid.");
                    Game = reply.Game;
                }
                Notice = request.Command == HostCommand.StopGame ? "已请求游戏正常退出。游戏可能要求电脑上的玩家保存或确认。" : "已更新电脑详情。";
            }
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            lock (stateGate) if (version == revision && !disposed) Notice = "远程请求已取消；电脑已接受的操作仍可能完成。";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (stateGate) if (version == revision && !disposed)
                Error = "无法读取电脑详情或确认此操作。请检查电脑连接和权限，再刷新状态。";
        }
        finally
        {
            lock (stateGate) if (ReferenceEquals(operation, pending)) operation = null;
            Changed?.Invoke();
        }
    }
}
