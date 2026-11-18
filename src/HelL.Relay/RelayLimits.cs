namespace HelL.Relay;

public sealed class RelayLimits
{
    public int MaxRooms { get; set; } = 128;
    public int MaxTrackedRooms { get; set; } = 4096;
    public int ConnectionsPerWindow { get; set; } = 60;
    public TimeSpan ConnectionWindow { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan PairingTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(120);
    public TimeSpan TrafficWindow { get; set; } = TimeSpan.FromMinutes(1);
    public long BytesPerWindow { get; set; } = 16 * 1024 * 1024;
    public int FragmentsPerWindow { get; set; } = 4096;
    public int AnonymousRequestsPerMinute { get; set; } = 10;
    public int OperatorRequestsPerMinute { get; set; } = 60;
    public int MaxInvitationChannels { get; set; } = 128;
    public int InvitationReadsPerMinute { get; set; } = 60;
    public string? StatePath { get; set; }

    internal void Validate()
    {
        if (MaxRooms is < 1 or > 4096 || MaxTrackedRooms < MaxRooms || MaxTrackedRooms > 100_000 ||
            ConnectionsPerWindow is < 1 or > 10_000 || BytesPerWindow is < 1 or > 1L << 40 ||
            FragmentsPerWindow is < 1 or > 1_000_000 || AnonymousRequestsPerMinute is < 1 or > 100_000 ||
            OperatorRequestsPerMinute is < 1 or > 100_000 || MaxInvitationChannels is < 1 or > 1024 ||
            InvitationReadsPerMinute is < 1 or > 10_000)
            throw new InvalidOperationException("Relay capacity and rate limits are outside their supported ranges.");
        foreach (TimeSpan deadline in new[] { ConnectionWindow, PairingTimeout, IdleTimeout, TrafficWindow })
            if (deadline <= TimeSpan.Zero || deadline > TimeSpan.FromHours(24))
                throw new InvalidOperationException("Relay deadlines must be positive and at most 24 hours.");
    }
}

public sealed class RelayAdmissionException(int statusCode, string message) : IOException(message)
{
    public int StatusCode { get; } = statusCode;
}
