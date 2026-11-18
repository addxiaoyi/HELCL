namespace HelL.Remote;

public sealed class RemoteConnectionOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (ConnectTimeout <= TimeSpan.Zero || HandshakeTimeout <= TimeSpan.Zero || RequestTimeout <= TimeSpan.Zero ||
            ConnectTimeout > TimeSpan.FromMinutes(5) || HandshakeTimeout > TimeSpan.FromMinutes(5) || RequestTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(RemoteConnectionOptions), "Remote operation deadlines must be positive and at most five minutes.");
    }
}
