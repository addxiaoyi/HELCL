namespace HelL.Models;

public sealed class InstanceVisual
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "none";
    public long Modified { get; set; }
}

public sealed class RecentPlayInfo
{
    public string Instance { get; set; } = "";
    public string Version { get; set; } = "";
    public long Ended { get; set; }
    public long Duration { get; set; }
    public bool Available { get; set; }
    public InstanceVisual Visual { get; set; } = new InstanceVisual();
}
