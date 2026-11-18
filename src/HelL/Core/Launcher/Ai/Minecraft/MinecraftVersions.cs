using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher.Ai.Minecraft;

public static class MinecraftVersions
{
    private static readonly Lazy<List<PyDict>> Supported = new Lazy<List<PyDict>>(() =>
        Py.ListOr((PyJson.Loads(CoreResources.ReadText("minecraft/dist/versions.json")) as PyDict)?.Get("versions")).OfType<PyDict>().ToList());
    public static string ForProtocol(int protocol) => Py.S(Supported.Value.LastOrDefault(row => Py.Int(row.Get("protocol")) == protocol)?.Get("version"));
    public static int? ProtocolFor(string version)
    {
        var row = Supported.Value.FirstOrDefault(entry => Py.S(entry.Get("version")) == version);
        return row == null ? (int?)null : (int)Py.Int(row.Get("protocol"));
    }
    public static string Range => Py.S(Supported.Value.First().Get("version")) + " – " + Py.S(Supported.Value.Last().Get("version"));
}
