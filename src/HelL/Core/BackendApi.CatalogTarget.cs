using System;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object catalog_targets(string instance)
    {
        if (!Instances.ListInstances().Select(Py.S).Contains(instance)) throw new ArgumentException("此实例已不存在，请重新选择。");
        return CatalogTarget.Read(new Instance(instance));
    }
}
