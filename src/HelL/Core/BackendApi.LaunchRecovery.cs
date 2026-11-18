using System;
using System.Collections.Generic;
using System.Linq;
using HelL.Core.Launcher;

namespace HelL.Core;

public partial class BackendApi
{
    public object repair_launch(string instance, string version, string repair) => StartTask("修复启动环境", ctx =>
    {
        if (repair != "java" && repair != "files") throw new ArgumentException("未知的启动修复类型。");
        var selected = _instance(instance);
        var manifest = selected.VersionJson(version) as PyDict ?? throw new InvalidOperationException("版本未安装，请先安装游戏。");
        manifest = Manifest.ResolveInherits(manifest, parent => selected.VersionJson(parent) as PyDict);
        ctx.Token.ThrowIfCancellationRequested();
        var downloads = _dm(ctx);
        if (repair == "files")
            Repair.RepairVersion(new Installer(selected, downloads, downloads.on_progress, downloads.cancel), version);
        else
        {
            Java.ResetCaches();
            string executable = Java.ResolveLaunchJava(manifest, dm: downloads, on_progress: downloads.on_progress, on_note: text => ctx.Log(text));
            if (!Java.JavaUsableFor(manifest, executable)) throw new LaunchError("没有找到可运行此版本的 Java，请在 Java 页面重新选择。");
            ctx.Token.ThrowIfCancellationRequested();
            VersionSettings.Save(selected, version, PyDict.Of("java", executable));
        }
        var check = Preflight.CheckLaunch(selected, version);
        var blockers = Py.ListOr(check.Get("items")).OfType<PyDict>().Where(item => Py.S(item.Get("level")) == "error").ToList();
        if (blockers.Count > 0) throw new LaunchError("修复后仍有启动问题：\n" + string.Join("\n", blockers.Select(item => Py.S(item.Get("title")))));
        return "修复完成，启动环境已重新检测。";
    });
}
