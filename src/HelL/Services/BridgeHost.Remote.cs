using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HelL.Contracts;
using HelL.Core;

namespace HelL.Services;

public sealed partial class BridgeHost
{
    private RemoteCompanionSession? remoteCompanion;

    public async Task<RemoteCompanionSession> StartRemoteCompanionAsync(string executable, string deviceId,
        Uri relay, string operatorToken, IEnumerable<HostPermission> permissions, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || !File.Exists(executable))
            throw new ArgumentException("An absolute companion executable path is required.", nameof(executable));
        if (!Guid.TryParseExact(deviceId, "N", out _)) throw new ArgumentException("A paired device identifier is required.", nameof(deviceId));
        if (relay == null || !relay.IsAbsoluteUri || relay.Scheme != "https" || relay.UserInfo.Length != 0
            || relay.Query.Length != 0 || relay.Fragment.Length != 0 || !relay.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("An HTTPS relay base URL ending in '/' is required.", nameof(relay));
        if (string.IsNullOrWhiteSpace(operatorToken)) throw new ArgumentException("A relay operator token is required.", nameof(operatorToken));
        cancellation.ThrowIfCancellationRequested();
        RemoteCompanionSession session;
        lock (CoreState.Gate)
        {
            if (remoteCompanion != null) throw new InvalidOperationException("The remote companion is already active.");
            HostPipeServer pipe = StartHostPipe(permissions);
            var process = new Process { StartInfo = new ProcessStartInfo(Path.GetFullPath(executable))
            {
                Arguments = string.Join(" ", new[] { "serve", Root, Environment.MachineName, deviceId, relay.AbsoluteUri }.Select(QuoteWindowsArg)),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
            } };
            try
            {
                if (!process.Start()) throw new IOException("The remote companion did not start.");
                session = new RemoteCompanionSession(process);
                remoteCompanion = session;
                process.StandardInput.WriteLine(JsonSerializer.Serialize(new
                    { Pipe = pipe.Name, SessionToken = pipe.SessionToken, OperatorToken = operatorToken }));
                process.StandardInput.Flush();
            }
            catch
            {
                remoteCompanion?.Dispose(); remoteCompanion = null;
                process.Dispose(); StopHostPipe(); throw;
            }
        }
        try { await session.WaitForReadyAsync(cancellation).ConfigureAwait(false); return session; }
        catch { StopRemoteCompanion(session); throw; }
    }

    internal void StopRemoteCompanion(RemoteCompanionSession expected)
    {
        lock (CoreState.Gate)
        {
            if (ReferenceEquals(remoteCompanion, expected))
            {
                remoteCompanion = null;
                expected.Dispose();
                StopHostPipe();
                return;
            }
            expected.Dispose();
        }
    }

    public void StopRemoteCompanion()
    {
        lock (CoreState.Gate)
        {
            remoteCompanion?.Dispose();
            remoteCompanion = null;
            StopHostPipe();
        }
    }

    private static string QuoteWindowsArg(string value)
    {
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(character);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
}

public sealed class RemoteCompanionSession : IDisposable
{
    private readonly Process process;
    private readonly Task<bool> diagnostics;
    private readonly Task invitations;
    private readonly object invitationGate = new object();
    private TaskCompletionSource<bool> invitationChanged = NewSignal();
    private string? pendingInvitation;
    private bool outputEnded;
    private int disposed;
    public string InvitationJson { get; private set; } = "";
    internal RemoteCompanionSession(Process process)
    {
        this.process = process;
        diagnostics = DrainDiagnosticsAsync(process.StandardError);
        invitations = ReadInvitationsAsync();
    }

    internal async Task WaitForReadyAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        InvitationJson = await NextInvitationAsync(timeout.Token).ConfigureAwait(false);
    }

    public async Task<string> ReadRenewedInvitationAsync(CancellationToken cancellation)
    {
        if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(RemoteCompanionSession));
        string line = await NextInvitationAsync(cancellation).ConfigureAwait(false);
        InvitationJson = line;
        return line;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(2000)) { process.Kill(); process.WaitForExit(1000); }
            if (diagnostics.IsCompleted && diagnostics.Status == TaskStatus.RanToCompletion && diagnostics.Result)
                Trace.TraceWarning("Remote companion reported a lifecycle failure; inspect its configuration and pairing state.");
        }
        catch (InvalidOperationException) { }
        catch (IOException) { Trace.TraceWarning("The remote companion control stream ended during shutdown."); }
        finally { InvitationJson = ""; process.Dispose(); }
    }

    private async Task<string> NextInvitationAsync(CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            Task changed;
            lock (invitationGate)
            {
                if (pendingInvitation != null)
                {
                    string line = pendingInvitation;
                    pendingInvitation = null;
                    return line;
                }
                if (outputEnded) throw new IOException("The companion ended before publishing a valid invitation.");
                changed = invitationChanged.Task;
            }
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            Task cancelled = Task.Delay(Timeout.Infinite, waiting.Token);
            if (await Task.WhenAny(changed, cancelled).ConfigureAwait(false) != changed) cancellation.ThrowIfCancellationRequested();
            waiting.Cancel();
            await changed.ConfigureAwait(false);
        }
    }

    private async Task ReadInvitationsAsync()
    {
        try
        {
            while (true)
            {
                string line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line == null) return;
                if (string.IsNullOrWhiteSpace(line) || line.Length > 8192) throw new InvalidDataException("The companion invitation is invalid.");
                using JsonDocument parsed = JsonDocument.Parse(line);
                if (!parsed.RootElement.TryGetProperty("ClientTicket", out JsonElement ticket)
                    || ticket.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(ticket.GetString()))
                    throw new InvalidDataException("The companion invitation is incompatible.");
                TaskCompletionSource<bool> changed;
                lock (invitationGate)
                {
                    // Only the newest invitation remains valid after room renewal.
                    pendingInvitation = line;
                    changed = invitationChanged;
                    invitationChanged = NewSignal();
                }
                changed.TrySetResult(true);
            }
        }
        catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is InvalidOperationException || error is JsonException)
        { Trace.TraceWarning("The companion invitation stream ended: " + error.GetType().Name); }
        finally
        {
            TaskCompletionSource<bool> changed;
            lock (invitationGate) { outputEnded = true; changed = invitationChanged; }
            changed.TrySetResult(true);
        }
    }

    private static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<bool> DrainDiagnosticsAsync(StreamReader reader)
    {
        bool received = false;
        char[] buffer = new char[512];
        while (await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) != 0) received = true;
        return received;
    }
}
