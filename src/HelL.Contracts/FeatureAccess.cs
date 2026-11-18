using System;

namespace HelL.Contracts;

public enum ExecutionSite { Local, PairedHost, Unavailable }

public sealed class FeatureAccess
{
    public string Feature { get; }
    public ExecutionSite Site { get; }
    public string Device { get; }
    public string Reason { get; }

    public FeatureAccess(string feature, ExecutionSite site, string device, string reason = "")
    {
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("A feature ID is required.", nameof(feature));
        if (!Enum.IsDefined(typeof(ExecutionSite), site)) throw new ArgumentOutOfRangeException(nameof(site));
        if (site != ExecutionSite.Unavailable && string.IsNullOrWhiteSpace(device))
            throw new ArgumentException("Available features must identify the executing device.", nameof(device));
        if (site == ExecutionSite.Unavailable && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Unavailable features must explain why.", nameof(reason));
        Feature = feature;
        Site = site;
        Device = device;
        Reason = reason;
    }
}
