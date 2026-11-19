using System.Text.Json;
using HelL.Contracts;
using HelL.Remote;

namespace HelL.Mobile.Remote;

public sealed partial class MobileRemoteController : IDisposable
{
    private readonly RemotePairingStore pairings;
    private readonly Func<RemoteHostInvite, HostRequest, CancellationToken, Task<HostReply>> send;
    private readonly Func<RemoteHostInvite, CancellationToken, IAsyncEnumerable<InvitationFeedUpdate>> watch;
    private readonly HttpClient updatesHttp = new(new HttpClientHandler { AllowAutoRedirect = false });
    private CancellationTokenSource? updateCancellation;
    private Task updateTask = Task.CompletedTask;
    private int updateRevision;
    private readonly Dictionary<string, (string channel, long revision)> received = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RemoteHostInvite> invitations = new(StringComparer.Ordinal);
    private CancellationTokenSource? operation;
    private readonly object stateGate = new();
    private bool disposed;
    private int revision;
    public MobileRemoteController(RemotePairingStore pairings,
        Func<RemoteHostInvite, HostRequest, CancellationToken, Task<HostReply>>? send = null,
        Func<RemoteHostInvite, CancellationToken, IAsyncEnumerable<InvitationFeedUpdate>>? watch = null)
    {
        this.pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));
        this.send = send ?? new PairedRemoteClient(pairings).ExecuteAsync;
        this.watch = watch ?? ((invite, token) => new InvitationFeedClient(updatesHttp).WatchAsync(invite, token,
            afterRevision: pairings.InvitationRevision(invite.Host.DeviceId)));
        foreach (RemoteHostInvite saved in pairings.Invitations()) invitations[saved.Host.DeviceId] = saved;
        SelectedHostId = pairings.SelectedHostId.Length == 0 ? invitations.Keys.FirstOrDefault() : pairings.SelectedHostId;
        if (SelectedHostId is not null) Invitation = invitations.GetValueOrDefault(SelectedHostId);
        foreach (RemoteHostInvite saved in invitations.Values)
            if (saved.Feed is { } feed) received[saved.Host.DeviceId] = (feed.Channel, pairings.InvitationRevision(saved.Host.DeviceId));
        if (Invitation?.Feed?.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) StartUpdates();
    }
    public string OfferJson => JsonSerializer.Serialize(pairings.Offer);
    public RemotePairingOffer OwnOffer => pairings.Offer;
    public RemotePairingOffer? PendingHost { get; private set; }
    public RemoteHostInvite? Invitation { get; private set; }
    public string? SelectedHostId { get; private set; }
    public HostTask[] Tasks { get; private set; } = [];
    public PairedDevice[] Hosts => pairings.Devices();
    public long? LastConnected(string deviceId) => pairings.LastConnected(deviceId);
    public string HostState(string deviceId)
    {
        lock (stateGate)
        {
            if (!invitations.TryGetValue(deviceId, out RemoteHostInvite? invite)) return "未导入邀请";
            if (invite.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return invite.Feed?.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ? "邀请过期，等待安全续期" : "邀请与续期能力均已过期";
            return SelectedHostId == deviceId && updateCancellation is not null ? "续期监听中" : "邀请已保存";
        }
    }
    public bool Busy { get { lock (stateGate) return operation is not null; } }
    public string Error { get; private set; } = "";
    public string Notice { get; private set; } = "尚未连接电脑。先核对双方指纹并建立信任。";
    public bool CanCancel => Invitation is { } invite && invite.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        && pairings.Find(invite.Host.DeviceId)?.Permissions.Contains(HostPermission.CancelTasks) == true;
    public event Action? Changed;
    public bool Watching { get { lock (stateGate) return updateCancellation is not null; } }
    public bool IsWatching(string hostId) { lock (stateGate) return SelectedHostId == hostId && updateCancellation is not null; }
    public Task UpdatesCompletion => updateTask;
    public string UpdateNotice { get; private set; } = "邀请更新尚未启用。";
    public void StartUpdates()
    {
        ThrowIfDisposed();
        RemoteHostInvite current;
        CancellationTokenSource pending;
        int version;
        lock (stateGate)
        {
            ThrowIfDisposed();
            if (updateCancellation is not null) return;
            current = Invitation ?? throw new InvalidOperationException("请先导入电脑的连接邀请。");
            if (current.Feed is null) throw new InvalidOperationException("该邀请不包含安全更新通道，请从电脑获取新邀请。");
            InvitationFeedCodec.Validate(current.Feed);
            PairedDevice? currentHost = pairings.Find(current.Host.DeviceId);
            if (currentHost is null || !string.Equals(currentHost.Fingerprint, current.Host.Fingerprint, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The selected host's trust changed. Pair it again before subscribing.");
            updateCancellation = pending = new CancellationTokenSource();
            version = ++updateRevision;
        }
        UpdateNotice = "安全邀请更新已开启；连接凭据保存在系统密钥库保护的加密配对存储中。";
        Changed?.Invoke();
        updateTask = WatchUpdatesAsync(current, pending, version);
    }
    public void StopUpdates()
    {
        lock (stateGate)
        {
            ++updateRevision;
            updateCancellation?.Cancel();
            updateCancellation = null;
        }
        UpdateNotice = "邀请自动更新已暂停；可恢复更新或手动导入最新邀请。";
        Changed?.Invoke();
    }
    public void SelectHost(string hostId)
    {
        ThrowIfDisposed();
        lock (stateGate)
        {
            if (pairings.Find(hostId) is null) throw new UnauthorizedAccessException("这台电脑尚未配对。");
            pairings.SelectHost(hostId);
            StopUpdates();
            SelectedHostId = hostId;
            Invitation = invitations.TryGetValue(hostId, out RemoteHostInvite? invite) ? invite : null;
            ++revision;
            Cancel();
            operation = null;
            Tasks = [];
            TasksTruncated = false;
            Detail = null;
            Game = null;
            Error = "";
            Notice = Invitation is null ? "请为这台电脑导入私有邀请。" : "已选择这台电脑；读取任务以更新当前状态。";
        }
        if (Invitation?.Feed?.Expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) StartUpdates();
        Changed?.Invoke();
    }
    private async Task WatchUpdatesAsync(RemoteHostInvite initial, CancellationTokenSource pending, int version)
    {
        using (pending)
        {
            long latest;
            lock (stateGate) latest = received.TryGetValue(initial.Host.DeviceId, out var saved)
                && saved.channel == initial.Feed!.Channel ? saved.revision : 0;
            try
            {
                await foreach (InvitationFeedUpdate update in watch(initial, pending.Token).ConfigureAwait(false))
                {
                    pending.Token.ThrowIfCancellationRequested();
                    RemoteHostInvite next = update.Invitation;
                    if (next.Host.DeviceId != initial.Host.DeviceId || !string.Equals(next.Host.Fingerprint, initial.Host.Fingerprint, StringComparison.OrdinalIgnoreCase)
                        || next.ClientDeviceId != initial.ClientDeviceId || next.Relay != initial.Relay || next.Feed != initial.Feed)
                        throw new InvalidDataException("The invitation update changed its paired host binding.");
                    lock (stateGate)
                    {
                        if (disposed || version != updateRevision || SelectedHostId != initial.Host.DeviceId
                            || !invitations.TryGetValue(initial.Host.DeviceId, out RemoteHostInvite? current) || current.Feed != initial.Feed) return;
                        if (update.Revision <= latest) continue;
                        pairings.SaveInvitation(next, update.Revision);
                        invitations[initial.Host.DeviceId] = next;
                        ApplyInvitation(next, persist: false);
                        latest = update.Revision;
                        received[initial.Host.DeviceId] = (initial.Feed!.Channel, latest);
                        UpdateNotice = "已安全接收电脑续期邀请；读取任务会使用最新连接。";
                    }
                    Changed?.Invoke();
                    if (autoReconnect && !Busy) await RefreshAsync(pending.Token).ConfigureAwait(false);
                }
                if (version == updateRevision && !disposed) UpdateNotice = "邀请更新通道已结束，请恢复更新或导入新邀请后重试。";
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (version == updateRevision && !disposed) UpdateNotice = "邀请自动更新失败。确认电脑在线后恢复更新；若授权已撤销，请重新配对后重试。";
            }
            finally
            {
                lock (stateGate) { if (ReferenceEquals(updateCancellation, pending)) updateCancellation = null; }
                Changed?.Invoke();
            }
        }
    }
    public void StageOffer(string json)
    {
        ThrowIfDisposed();
        using JsonDocument document = Parse(json, 4096, ["DeviceId", "Name", "Fingerprint"]);
        RemotePairingOffer offer;
        try { offer = document.RootElement.Deserialize<RemotePairingOffer>() ?? throw InvalidInput(); }
        catch (JsonException) { throw InvalidInput(); }
        ValidateOffer(offer);
        if (offer.DeviceId == OwnOffer.DeviceId) throw new InvalidDataException("不能将手机与自己配对。");
        PendingHost = offer;
        Error = "";
        Notice = "请在电脑上核对手机指纹，同时核对下方电脑指纹；确认前不会保存信任。";
        Changed?.Invoke();
    }
    public void ConfirmPairing(bool verified, bool allowCancel, bool allowControlGame = false)
    {
        ThrowIfDisposed();
        if (!verified || PendingHost is null) throw new InvalidOperationException("请先在两台设备上逐字核对双方 SHA-256 指纹，再确认配对。");
        var permissions = new List<HostPermission> { HostPermission.Read };
        if (allowCancel) permissions.Add(HostPermission.CancelTasks);
        if (allowControlGame) permissions.Add(HostPermission.ControlGame);
        pairings.Trust(PendingHost, permissions);
        PendingHost = null;
        Error = "";
        Notice = "已保存电脑信任。请在电脑端信任这台手机，并单独授权允许的操作。";
        Changed?.Invoke();
    }
    public void Revoke(string id)
    {
        ThrowIfDisposed();
        lock (stateGate)
        {
            pairings.Revoke(id);
            received.Remove(id);
            invitations.Remove(id);
            if (SelectedHostId == id)
            {
                StopUpdates();
                ++revision;
                Cancel();
                operation = null;
                SelectedHostId = null;
                Invitation = null;
                Tasks = [];
                TasksTruncated = false;
                Detail = null;
                Game = null;
            }
        }
        if (PendingHost?.DeviceId == id) PendingHost = null;
        Error = "";
        Notice = "已在此手机撤销信任并清除连接邀请。要禁止所有连接，请同时在电脑端撤销这台手机。";
        Changed?.Invoke();
    }
    public void ImportInvitation(string json)
    {
        ThrowIfDisposed();
        using JsonDocument document = Parse(json, 8192, ["Host", "ClientDeviceId", "Relay", "Room", "ClientTicket", "Expires", "Feed"]);
        if (!document.RootElement.TryGetProperty("Host", out JsonElement host)) throw InvalidInput();
        using JsonDocument checkedHost = Parse(host.GetRawText(), 4096, ["DeviceId", "Name", "Fingerprint"]);
        if (document.RootElement.TryGetProperty("Feed", out JsonElement feed) && feed.ValueKind != JsonValueKind.Null)
        {
            using JsonDocument checkedFeed = Parse(feed.GetRawText(), 4096, ["Channel", "ReaderToken", "EncryptionKey", "Expires"]);
        }
        RemoteHostInvite invite;
        try { invite = document.RootElement.Deserialize<RemoteHostInvite>() ?? throw InvalidInput(); }
        catch (JsonException) { throw InvalidInput(); }
        lock (stateGate)
        {
            ValidateInvitation(invite);
            pairings.SaveInvitation(invite);
            invitations[invite.Host.DeviceId] = invite;
            StopUpdates();
            SelectedHostId = invite.Host.DeviceId;
            ApplyInvitation(invite, persist: false);
        }
        if (invite.Feed is not null) StartUpdates();
        else { UpdateNotice = "此邀请不包含自动更新。电脑续期后，请手动导入最新邀请。"; Changed?.Invoke(); }
    }
    public void UpdateInvitation(RemoteHostInvite invite)
    {
        ThrowIfDisposed();
        bool restart;
        lock (stateGate)
        {
            ValidateInvitation(invite);
            pairings.SaveInvitation(invite);
            restart = Invitation?.Host.DeviceId != invite.Host.DeviceId || Invitation?.Feed != invite.Feed;
            if (restart) StopUpdates();
            ApplyInvitation(invite, persist: false);
        }
        if (restart && invite.Feed is not null) StartUpdates();
    }
    private void ApplyInvitation(RemoteHostInvite invite, bool persist = true)
    {
        ThrowIfDisposed();
        ValidateInvitation(invite);
        if (persist) pairings.SaveInvitation(invite);
        invitations[invite.Host.DeviceId] = invite;
        SelectedHostId = invite.Host.DeviceId;
        ++revision;
        Cancel();
        operation = null;
        Invitation = invite;
        Tasks = [];
        TasksTruncated = false;
        Detail = null;
        Game = null;
        Error = "";
        Notice = "邀请已更新。读取快照以检查电脑当前任务。";
        Changed?.Invoke();
    }
    private void ValidateInvitation(RemoteHostInvite invite)
    {
        if (invite?.Host is null) throw InvalidInput();
        if (invite.Feed is not null) InvitationFeedCodec.Validate(invite.Feed);
        ValidateOffer(invite.Host);
        PairedDevice? host = pairings.Find(invite.Host.DeviceId);
        if (host is null || !string.Equals(host.Fingerprint, invite.Host.Fingerprint, StringComparison.OrdinalIgnoreCase)
            || invite.ClientDeviceId != OwnOffer.DeviceId)
            throw new UnauthorizedAccessException("邀请必须来自已核对指纹的电脑，且只能发给这台手机。");
        if (!Uri.TryCreate(invite.Relay, UriKind.Absolute, out Uri? relay) || relay.Scheme != "https"
            || relay.UserInfo.Length != 0 || relay.Query.Length != 0 || relay.Fragment.Length != 0 || !relay.AbsolutePath.EndsWith('/'))
            throw new InvalidDataException("邀请必须使用不含凭据或查询参数的 HTTPS 中继地址。");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (invite.Expires <= now || invite.Expires > now + 86400 || invite.Room is null || invite.Room.Length != 48
            || invite.Room.Any(ch => !Uri.IsHexDigit(ch)) || string.IsNullOrWhiteSpace(invite.ClientTicket) || invite.ClientTicket.Length > 1024)
            throw new InvalidDataException("邀请无效或已过期，请从电脑获取最新邀请。");
    }
    public Task RefreshAsync(CancellationToken cancellation = default) => RunAsync(null, cancellation);
    public async Task CancelTaskAsync(string id, CancellationToken cancellation = default)
    {
        if (!CanCancel) throw new UnauthorizedAccessException("此手机仅允许读取任务。取消任务还需要电脑端授权。");
        if (!Tasks.Any(task => task.Id == id && task.State == "running")) throw new InvalidOperationException("该任务已不在运行，请刷新快照。");
        await RunAsync(id, cancellation).ConfigureAwait(false);
    }
    private async Task RunAsync(string? taskId, CancellationToken cancellation)
    {
        ThrowIfDisposed();
        RemoteHostInvite invite;
        int version;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        pending.CancelAfter(TimeSpan.FromSeconds(35));
        lock (stateGate)
        {
            ThrowIfDisposed();
            if (operation is not null) throw new InvalidOperationException("正在读取电脑状态，请先等待或取消当前连接。");
            invite = Invitation ?? throw new InvalidOperationException("请先导入已配对电脑发给本机的邀请。");
            version = revision;
            ValidateInvitation(invite);
            operation = pending;
        }
        Error = "";
        Notice = taskId is null ? "正在安全连接并读取任务…" : "正在发送取消请求，等待电脑确认…";
        Changed?.Invoke();
        try
        {
            if (taskId is not null) await Execute(new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.CancelTask, TaskId = taskId });
            HostReply reply = await Execute(new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot });
            lock (stateGate)
            {
                pending.Token.ThrowIfCancellationRequested();
                if (version != revision || disposed) return;
                Tasks = reply.Tasks ?? [];
                TasksTruncated = reply.TasksTruncated;
                Notice = Tasks.Length == 0 ? "已连接。电脑当前没有可显示的任务。" : $"已更新 {Tasks.Length} 项任务 · {DateTime.Now:HH:mm:ss}";
                RecordSuccessfulConnection(invite.Host.DeviceId);
            }
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        { lock (stateGate) { if (version == revision && !disposed) Notice = "本次连接已取消；电脑上的任务继续运行。"; } }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (stateGate)
            {
                if (version != revision || disposed) return;
                Error = "未能读取电脑状态。请确认电脑在线、双方信任仍有效，并导入最新邀请后重试。";
                Notice = "连接未完成；保留上次快照。";
            }
        }
        finally { lock (stateGate) { if (ReferenceEquals(operation, pending)) operation = null; } Changed?.Invoke(); }
        async Task<HostReply> Execute(HostRequest request)
        {
            HostReply reply = await send(invite, request, pending.Token).WaitAsync(pending.Token).ConfigureAwait(false);
            pending.Token.ThrowIfCancellationRequested();
            reply.Validate(request);
            if (!reply.Ok || reply.Protocol != HostProtocol.Version || reply.RequestId != request.RequestId) throw new IOException("电脑未确认该请求。");
            return reply;
        }
    }
    private void RecordSuccessfulConnection(string hostId)
    {
        try { pairings.RecordConnection(hostId); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceWarning("MobileRemoteController.RecordConnection: " + error.GetType().Name);
            Notice = "电脑任务已更新，但最近连接时间未能保存。系统密钥库解锁后会再次尝试。";
        }
    }
    public void Cancel() { lock (stateGate) operation?.Cancel(); }
    public void Dispose()
    {
        lock (stateGate) { if (disposed) return; disposed = true; ++revision; Cancel(); StopUpdates(); }
        updatesHttp.Dispose();
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    private static InvalidDataException InvalidInput() => new("内容格式无效。请仅导入公开配对信息或仅发给本机的连接邀请。");
    private static JsonDocument Parse(string json, int limit, string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > limit) throw InvalidInput();
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(json); } catch (JsonException) { throw InvalidInput(); }
        if (parsed.RootElement.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (parsed.RootElement.EnumerateObject().All(field => allowed.Contains(field.Name, StringComparer.Ordinal) && names.Add(field.Name))) return parsed;
        }
        parsed.Dispose();
        throw InvalidInput();
    }
    private static void ValidateOffer(RemotePairingOffer offer)
    {
        if (!Guid.TryParseExact(offer.DeviceId, "N", out _) || string.IsNullOrWhiteSpace(offer.Name) || offer.Name.Length > 80
            || offer.Name.Any(char.IsControl) || offer.Fingerprint is null || offer.Fingerprint.Length != 64 || offer.Fingerprint.Any(ch => !Uri.IsHexDigit(ch))) throw InvalidInput();
    }
}
