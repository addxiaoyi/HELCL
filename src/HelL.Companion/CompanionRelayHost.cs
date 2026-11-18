using System.Runtime.Versioning;
using HelL.Contracts;
using HelL.Remote;

namespace HelL.Companion;

public static class CompanionRelayHost
{
    [SupportedOSPlatform("windows")]
    public static PairedRemoteHost Connect(RemotePairingStore pairings, HttpClient http, Uri relay,
        string operatorToken, string pipeName, string sessionToken, TimeSpan? renewalInterval = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The launcher companion pipe requires Windows.");
        if (string.IsNullOrWhiteSpace(pipeName) || !pipeName.StartsWith("hell-host-", StringComparison.Ordinal))
            throw new ArgumentException("A running launcher host pipe is required.", nameof(pipeName));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        return new PairedRemoteHost(pairings, http, relay, operatorToken,
            (HostRequest request, CancellationToken cancellation) => HostPipeClient.ExecuteAsync(pipeName, sessionToken, request, cancellation), renewalInterval);
    }
}
