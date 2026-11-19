using System;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object recycle_catalog_file(string instance, string filename, string kind, string version = "")
    {
        if (!Instances.ListLegacyInstances().Select(Py.S).Contains(instance)) throw new ArgumentException("目标实例不存在。");
        RecycledResource entry = ResourceRecycle.Move(new Instance(instance), filename, kind, version);
        return PyDict.Of("receipt", entry.Receipt, "original", entry.Original, "recycled", entry.Recycled,
            "instance", entry.Instance, "kind", entry.Kind, "version", entry.Version);
    }

    public object restore_catalog_file(string instance, string receipt)
    {
        if (!Instances.ListLegacyInstances().Select(Py.S).Contains(instance)) throw new ArgumentException("目标实例不存在。");
        return PyDict.Of("restored", ResourceRecycle.Restore(new Instance(instance), receipt));
    }
}
