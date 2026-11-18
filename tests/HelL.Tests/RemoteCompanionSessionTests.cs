using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HelL.Services;
using Xunit;

namespace HelL.Tests;

public sealed class RemoteCompanionSessionTests
{
    [Fact]
    public async Task CancellingAnInvitationWaitDoesNotConsumeTheNextInvitation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Process process = StartCompanion();
        using var session = new RemoteCompanionSession(process);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await session.WaitForReadyAsync(deadline.Token);
        Assert.Contains("initial-ticket", session.InvitationJson);
        using var cancelled = new CancellationTokenSource();
        Task waiting = session.ReadRenewedInvitationAsync(cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await process.StandardInput.WriteLineAsync("next");
        string renewed = await session.ReadRenewedInvitationAsync(deadline.Token);
        Assert.Contains("renewed-ticket", renewed);
    }

    [Fact]
    public async Task DisposingTheSessionStopsItsCompanionProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Process process = StartCompanion();
        using Process observer = Process.GetProcessById(process.Id);
        var session = new RemoteCompanionSession(process);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await session.WaitForReadyAsync(deadline.Token);
        session.Dispose();
        Assert.True(observer.WaitForExit(3000));
        Assert.Empty(session.InvitationJson);
    }

    [Fact]
    public async Task ACompanionThatEndsBeforeItsInvitationFailsTheStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Process process = StartCompanion("exit 0");
        using var session = new RemoteCompanionSession(process);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<IOException>(() => session.WaitForReadyAsync(deadline.Token));
    }

    [Fact]
    public async Task AStaleStartupCleanupCannotStopItsReplacementSession()
    {
        if (!OperatingSystem.IsWindows()) return;
        using Process previousProcess = StartCompanion();
        using Process nextProcess = StartCompanion();
        using var previous = new RemoteCompanionSession(previousProcess);
        using var next = new RemoteCompanionSession(nextProcess);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await previous.WaitForReadyAsync(deadline.Token);
        await next.WaitForReadyAsync(deadline.Token);
        var host = (BridgeHost)RuntimeHelpers.GetUninitializedObject(typeof(BridgeHost));
        FieldInfo current = typeof(BridgeHost).GetField("remoteCompanion", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(current);
        current.SetValue(host, next);
        host.StopRemoteCompanion(previous);
        Assert.Same(next, current.GetValue(host));
        await nextProcess.StandardInput.WriteLineAsync("next");
        Assert.Contains("renewed-ticket", await next.ReadRenewedInvitationAsync(deadline.Token));
        host.StopRemoteCompanion();
    }

    private static Process StartCompanion(string script = null)
    {
        script ??= "[Console]::WriteLine('{\"ClientTicket\":\"initial-ticket\"}'); while (($line = [Console]::ReadLine()) -ne $null) { if ($line -eq 'next') { [Console]::WriteLine('{\"ClientTicket\":\"renewed-ticket\"}') } }";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new IOException("The test companion could not start.");
    }
}
