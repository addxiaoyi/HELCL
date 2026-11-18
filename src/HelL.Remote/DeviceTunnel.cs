using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HelL.Remote;

public static class DeviceTunnel
{
    public static X509Certificate2 CreateIdentity(string device)
    {
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("Device name is required.", nameof(device));
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName("CN=HelL device"), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
        byte[] pkcs12 = generated.Export(X509ContentType.Pkcs12);
        try
        {
            return LoadIdentity(pkcs12);
        }
        finally { CryptographicOperations.ZeroMemory(pkcs12); }
    }

    internal static X509Certificate2 LoadIdentity(byte[] pkcs12)
    {
        ArgumentNullException.ThrowIfNull(pkcs12);
        // Schannel needs a user key; macOS uses a runtime-owned temporary keychain and rejects EphemeralKeySet.
        X509KeyStorageFlags storage = OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet
            : OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
        return X509CertificateLoader.LoadPkcs12(pkcs12, null, storage | X509KeyStorageFlags.Exportable);
    }

    public static string Fingerprint(X509Certificate2 identity) => identity.GetCertHashString(HashAlgorithmName.SHA256);

    public static async Task<SslStream> ConnectAsync(Stream transport, X509Certificate2 identity,
        string hostFingerprint, CancellationToken cancellation)
    {
        ValidatePin(hostFingerprint);
        var tls = new SslStream(transport, false, (_, cert, _, _) => Matches(cert, hostFingerprint));
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "hell-paired-device",
                EnabledSslProtocols = SslProtocols.Tls13,
                ClientCertificates = new X509CertificateCollection { identity },
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancellation).ConfigureAwait(false);
            return tls;
        }
        catch { await tls.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public static async Task<SslStream> AcceptAsync(Stream transport, X509Certificate2 identity,
        string clientFingerprint, CancellationToken cancellation)
    {
        ValidatePin(clientFingerprint);
        var tls = new SslStream(transport, false, (_, cert, _, _) => Matches(cert, clientFingerprint));
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = identity,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancellation).ConfigureAwait(false);
            return tls;
        }
        catch { await tls.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static bool Matches(X509Certificate? certificate, string pin)
    {
        if (certificate is null) return false;
        using X509Certificate2 presented = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        DateTime now = DateTime.UtcNow;
        if (now < presented.NotBefore.ToUniversalTime() || now > presented.NotAfter.ToUniversalTime()) return false;
        byte[] actual = certificate.GetCertHash(HashAlgorithmName.SHA256);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(pin));
    }

    private static void ValidatePin(string pin)
    {
        if (pin is null || pin.Length != 64 || pin.Any(ch => !Uri.IsHexDigit(ch))) throw new ArgumentException("A SHA-256 device fingerprint is required.", nameof(pin));
    }
}
