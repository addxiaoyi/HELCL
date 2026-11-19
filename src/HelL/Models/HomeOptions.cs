using System.Collections.Generic;

namespace HelL.Models;

public sealed class HomeOptions
{
    public List<string> PinnedInstances { get; set; } = new();
    public bool SidebarEnabled { get; set; } = true;
    public List<string> SidebarModules { get; set; } = new() { "clock", "worlds", "tasks" };
}
