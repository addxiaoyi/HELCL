using System.Text.Json;
using HelL.Companion;
using HelL.Contracts;
using HelL.Core.Platform;
using HelL.Portable;
using HelL.Remote;

internal static class Program
{
    private sealed record Bootstrap(string Pipe, string SessionToken, string OperatorToken);

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["--help"])
            {
                Console.WriteLine("Commands: identity|list <root> <name>; trust <root> <name> <offer-file> <confirmed-fingerprint> [--allow-cancel]; revoke <root> <name> <device-id>; serve <root> <name> <device-id> <relay-url>.");
                Console.WriteLine("Serve receives launcher pipe credentials and the relay operator token only through its private standard-input stream.");
                return 0;
            }
            if (args.Length < 3) throw new ArgumentException("Use identity|list|trust|revoke|serve <data-root> <device-name> [arguments].");
            string command = args[0];
            bool validCommand = (command is "identity" or "list" && args.Length == 3)
                || (command == "trust" && args.Length is 5 or 6)
                || (command == "revoke" && args.Length == 4) || (command == "serve" && args.Length == 5);
            if (!validCommand) throw new ArgumentException("The companion command or its arguments are invalid.");
            var store = new RemotePairingStore(args[1], args[2], new NativeSecrets());
            switch (command)
            {
                case "identity" when args.Length == 3:
                    Console.WriteLine(JsonSerializer.Serialize(store.Offer));
                    break;
                case "list" when args.Length == 3:
                    Console.WriteLine(JsonSerializer.Serialize(store.Devices()));
                    break;
                case "trust" when args.Length is 5 or 6:
                    if (args.Length == 6 && args[5] != "--allow-cancel") throw new ArgumentException("Unknown pairing permission.");
                    if (new FileInfo(args[3]).Length > 4096) throw new InvalidDataException("The pairing offer is too large.");
                    RemotePairingOffer offer = JsonSerializer.Deserialize<RemotePairingOffer>(await File.ReadAllTextAsync(args[3]))
                        ?? throw new InvalidDataException("The pairing offer is empty.");
                    if (!string.Equals(offer.Fingerprint, args[4], StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("The confirmed device fingerprint does not match this offer.");
                    store.Trust(offer, args.Length == 6 ? [HostPermission.Read, HostPermission.CancelTasks] : [HostPermission.Read]);
                    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, device = offer.DeviceId }));
                    break;
                case "revoke" when args.Length == 4:
                    store.Revoke(args[3]);
                    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, device = args[3] }));
                    break;
                case "serve" when args.Length == 5:
                    await ServeAsync(store, args[3], new Uri(args[4]));
                    break;
                default:
                    throw new ArgumentException("The companion command or its arguments are invalid.");
            }
            if (store.CleanupWarning.Length != 0) Console.Error.WriteLine(store.CleanupWarning);
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Bootstrap input and invitations contain credentials; diagnostics never echo input or exception payloads.
            Console.Error.WriteLine("The companion could not complete the operation (" + ex.GetType().Name + "). Check pairing, system key-store access and relay configuration.");
            return 1;
        }
    }

    private static async Task ServeAsync(RemotePairingStore store, string deviceId, Uri relay)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Serving a WPF launcher requires Windows.");
        using var stopped = new CancellationTokenSource();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        ConsoleCancelEventHandler interrupt = (_, args) => { args.Cancel = true; stopped.Cancel(); };
        Console.CancelKeyPress += interrupt;
        InvitationFeedPublisher? publisher = null;
        try
        {
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
            startup.CancelAfter(TimeSpan.FromSeconds(15));
            string bootstrapLine = await Console.In.ReadLineAsync(startup.Token)
                ?? throw new InvalidDataException("The launcher did not supply a companion session.");
            if (bootstrapLine.Length > 8192) throw new InvalidDataException("The companion session is too large.");
            Bootstrap bootstrap = JsonSerializer.Deserialize<Bootstrap>(bootstrapLine)
                ?? throw new InvalidDataException("The companion session is invalid.");
            HostReply probe = await HostPipeClient.ExecuteAsync(bootstrap.Pipe, bootstrap.SessionToken,
                new HostRequest { RequestId = Guid.NewGuid().ToString("N"), Command = HostCommand.Snapshot }, startup.Token);
            if (!probe.Ok) throw new InvalidOperationException("The launcher did not grant access to its task state.");
            publisher = new InvitationFeedPublisher(http, relay, bootstrap.OperatorToken, new NativeSecrets(), store.Offer.DeviceId, deviceId);
            await using PairedRemoteHost host = CompanionRelayHost.Connect(store, http, relay,
                bootstrap.OperatorToken, bootstrap.Pipe, bootstrap.SessionToken);
            await host.StartAsync(deviceId, async (invite, cancellation) =>
            {
                RemoteHostInvite published = await publisher.PublishAsync(invite, cancellation);
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(published).AsMemory(), cancellation);
                await Console.Out.FlushAsync(cancellation);
            }, stopped.Token);
            Task input = WaitForStopAsync(host, deviceId, stopped);
            Task session = host.Completion(deviceId);
            await Task.WhenAny(input, session);
            stopped.Cancel();
            try { await Task.WhenAll(input, session); }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        }
        finally
        {
            Console.CancelKeyPress -= interrupt;
            if (publisher is not null && store.Find(deviceId) is null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await publisher.RevokeAsync(cleanup.Token);
            }
        }
    }

    private static async Task WaitForStopAsync(PairedRemoteHost host, string deviceId, CancellationTokenSource stopped)
    {
        while (!stopped.IsCancellationRequested)
        {
            string? command = await Console.In.ReadLineAsync(stopped.Token);
            if (command is null or "stop") return;
            if (command == "renew") { await host.RenewAsync(deviceId, stopped.Token); continue; }
            if (command == "revoke") { await host.RevokeAsync(deviceId, stopped.Token); return; }
            throw new InvalidDataException("The companion control command is invalid.");
        }
    }

    private sealed class NativeSecrets : IRemoteSecretStore
    {
        private const string Service = "HelL remote device encryption";
        private readonly ISecretStore native = PlatformServices.SecretStore ?? SystemSecretStore.Create();
        public string Read(string key) => native.GetPassword(Service, key);
        public void Write(string key, string value) => native.SetPassword(Service, key, value);
        public void Delete(string key) => native.DeletePassword(Service, key);
    }
}
