using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HelL.Contracts;
#if HELL_PORTABLE
using System.Runtime.Versioning;
#endif

namespace HelL.Core;

// This local endpoint is opt-in and always dispatches into the existing business owner.
#if HELL_PORTABLE
[SupportedOSPlatform("windows")]
#endif
public sealed class HostPipeServer : IDisposable
{
    private const int HandshakeBytes = 4096;
    private readonly HostCommandService commands;
    private readonly HostPermission[] permissions;
    private readonly byte[] tokenHash;
    private readonly string device;
    private readonly CancellationTokenSource stopped = new CancellationTokenSource();
    private readonly Task completion;
    private int disposed;
    public string Name { get; }
    public string SessionToken { get; }
    public Task Completion => completion;

    private HostPipeServer(HostCommandService commands, string device, IEnumerable<HostPermission> permissions)
    {
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        this.device = device;
        this.permissions = permissions?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(permissions));
        if (!this.permissions.Contains(HostPermission.Read) || this.permissions.Any(permission => !Enum.IsDefined(typeof(HostPermission), permission)))
            throw new ArgumentException("A local companion requires explicit valid host permissions.", nameof(permissions));
        Name = "hell-host-" + Guid.NewGuid().ToString("N");
        byte[] secret = new byte[32];
        using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(secret);
        SessionToken = Convert.ToBase64String(secret);
        Array.Clear(secret, 0, secret.Length);
        using (SHA256 hash = SHA256.Create()) tokenHash = hash.ComputeHash(Encoding.UTF8.GetBytes(SessionToken));
        completion = RunAsync(stopped.Token);
    }

    public static HostPipeServer Start(BackendApi owner, string device, IEnumerable<HostPermission> permissions)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            throw new PlatformNotSupportedException("The protected WPF companion pipe requires Windows.");
        return new HostPipeServer(new HostCommandService(owner, device), device, permissions);
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            using (NamedPipeServerStream pipe = CreatePipe())
            using (cancellation.Register(pipe.Dispose))
            {
                try
                {
                    await pipe.WaitForConnectionAsync(cancellation).ConfigureAwait(false);
                    if (!SameUser(pipe)) throw new UnauthorizedAccessException("The local companion user differs from the owner.");
                    using (var session = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        session.CancelAfter(TimeSpan.FromSeconds(30));
                        using (session.Token.Register(pipe.Dispose)) await ServeAsync(pipe, session).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is IOException || ex is ObjectDisposedException || ex is JsonException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    if (cancellation.IsCancellationRequested) return;
                    // Authentication payloads and host state never enter transport logs.
                    Trace.TraceWarning("HostPipeServer session ended: " + ex.GetType().Name);
                }
            }
        }
    }

    private NamedPipeServerStream CreatePipe()
    {
#if HELL_PORTABLE
        return new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
#else
        using WindowsIdentity current = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = current.User ?? throw new UnauthorizedAccessException("The host user SID is unavailable.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        return new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security);
#endif
    }

    private static bool SameUser(NamedPipeServerStream pipe)
    {
        string? peer = null;
        pipe.RunAsClient(() => { using WindowsIdentity identity = WindowsIdentity.GetCurrent(); peer = identity.User?.Value; });
        using WindowsIdentity owner = WindowsIdentity.GetCurrent();
        return peer != null && peer == owner.User?.Value;
    }

    private async Task ServeAsync(Stream pipe, CancellationTokenSource session)
    {
        byte[]? payload = await HostFrames.ReadAsync(pipe, session.Token, HandshakeBytes).ConfigureAwait(false);
        if (payload == null || payload.Length > HandshakeBytes) return;
        HostPipeHello? hello = JsonSerializer.Deserialize<HostPipeHello>(payload);
        if (hello == null || hello.Protocol != HostProtocol.Version || hello.Role != "companion" || !MatchesToken(hello.SessionToken))
            throw new UnauthorizedAccessException("The local companion handshake was rejected.");
        await HostFrames.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(new HostPipeWelcome { Ok = true, Device = device }), session.Token).ConfigureAwait(false);
        while (!session.IsCancellationRequested)
        {
            session.CancelAfter(TimeSpan.FromSeconds(30));
            payload = await HostFrames.ReadAsync(pipe, session.Token).ConfigureAwait(false);
            if (payload == null) return;
            HostRequest? request;
            try { request = JsonSerializer.Deserialize<HostRequest>(payload); }
            catch (JsonException) { request = null; }
            HostReply reply = request == null
                ? new HostReply { Device = device, ErrorCode = "invalid_request", Error = "The host request is not valid JSON." }
                : commands.Execute(request, permissions);
            await HostFrames.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(reply), session.Token).ConfigureAwait(false);
        }
    }

    private bool MatchesToken(string? supplied)
    {
        if (supplied == null || supplied.Length != SessionToken.Length) return false;
        byte[] candidate;
        using (SHA256 hash = SHA256.Create()) candidate = hash.ComputeHash(Encoding.UTF8.GetBytes(supplied));
        int difference = 0;
        for (int index = 0; index < tokenHash.Length; index++) difference |= tokenHash[index] ^ candidate[index];
        return difference == 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stopped.Cancel();
        _ = completion.ContinueWith(task => { if (task.IsFaulted) Trace.TraceError("HostPipeServer stopped: " + task.Exception?.GetBaseException().GetType().Name); stopped.Dispose(); }, TaskScheduler.Default);
    }
}
