using HelL.Core.Launcher;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core;

public partial class BackendApi
{
    public object set_instance_group(string instance, string group)
    {
        InstanceGroups.Assign(instance, group);
        invalidate_instances();
        Emit("ui_changed", new PyDict());
        return null;
    }

    private static string[] GroupSelection(object instances)
    {
        if (instances is not List<object> list || list.Any(item => item is not string)) throw new ArgumentException("实例选择格式无效。");
        return list.Cast<string>().ToArray();
    }

    public object set_instance_groups(object instances, string group)
    {
        InstanceGroups.AssignMany(GroupSelection(instances), group);
        invalidate_instances(); Emit("ui_changed", new PyDict()); return null;
    }

    public object pin_instances(object instances, bool pinned)
    {
        HomePreferences.PinMany(GroupSelection(instances), pinned);
        Emit("ui_changed", new PyDict()); return home_options();
    }

    public object rename_instance_group(string group, string name)
    {
        int count = InstanceGroups.Rename(group, name);
        invalidate_instances(); Emit("ui_changed", new PyDict()); return count;
    }
}
