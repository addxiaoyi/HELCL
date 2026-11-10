using System;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai.Minecraft;

namespace HelL.Core;

public partial class BackendApi
{
    public object minecraft_status() => MinecraftSession.Status();
    public object minecraft_retry_action(string id) => PyJson.Loads(MinecraftSession.RetryAction(id));
    public object minecraft_discover() => MinecraftLanDiscovery.DiscoverAsync().GetAwaiter().GetResult();
    public object minecraft_probe(string host, int port, string version = "") => MinecraftServerProbe.CheckAsync(host.Trim(), port, version.Trim()).GetAwaiter().GetResult();
    public object minecraft_diagnose(string node = "") => PyDict.Of("runtime", MinecraftRuntime.Diagnose(node.Length > 0 ? node : Py.S(MinecraftSession.Options().Get("node")), true), "supported_range", MinecraftVersions.Range);
    public object minecraft_extensions() => MinecraftExtensions.Status();
    public object minecraft_extension_set(string extension, bool enabled)
    {
        MinecraftExtensions.Set(extension, enabled);
        Emit("ui_changed", new PyDict());
        return MinecraftExtensions.Status();
    }
    public object minecraft_configure(string host, int port, string name, string auth = "offline", string version = "", string node = "")
    {
        if (MinecraftSession.Running) throw new InvalidOperationException("请先断开 AI 玩家，再修改连接设置。");
        var options = MinecraftOptions.Validate(host, port, name, auth, version);
        options["node"] = MinecraftRuntime.FindNode(node);
        MinecraftSession.SaveOptions(options);
        return options;
    }
    public object minecraft_install(string node = "") => StartTask("安装 Minecraft MCP 组件", ctx =>
    {
        if (MinecraftSession.Running) throw new InvalidOperationException("请先断开 AI 玩家，再修复组件。");
        string executable = MinecraftRuntime.FindNode(node.Length > 0 ? node : Py.S(MinecraftSession.Options().Get("node")));
        MinecraftSession.Repair(executable, ctx);
        return "Minecraft MCP 组件已就绪";
    });
    public object minecraft_connect() => MinecraftSession.Start(MinecraftSession.Options());
    public object minecraft_enable_tools(bool enabled)
    {
        if (enabled && Py.S(CONFIG.Get("ai_mode")) == "local")
        {
            HelL.Core.Launcher.Ai.Local.LocalTools.Verify(_settings_full());
            CONFIG.Set("ai_local_tools", true);
            CONFIG.Save();
            Emit("ui_changed", new PyDict());
        }
        MinecraftSession.SetToolsEnabled(enabled);
        return MinecraftSession.Status(false);
    }
    public object minecraft_stop()
    {
        MinecraftSession.Stop();
        return MinecraftSession.Status(false);
    }
    public object minecraft_clear_login() { MinecraftRuntime.ClearLogin(); return PyDict.Of("ok", true); }
    public object minecraft_allow_feiyu()
    {
        if (_ai_busy) throw new InvalidOperationException("请先结束当前 AI 回复，再交给大肥鱼控制。");
        MinecraftSession.AllowFeiyu(); return MinecraftSession.Status(false);
    }
    public object minecraft_prepare_model() => StartTask("准备独立游戏模型", ctx =>
    {
        if (_ai_busy) throw new InvalidOperationException("请先结束 AI 回复，再切换本地模型。");
        var ready = MinecraftModels.Prepare(ctx);
        ai_local_select(MinecraftModels.Model, Py.S(ready.Get("endpoint")));
        ai_local_tool_mode(true);
        CONFIG.Update(PyDict.Of("minecraft_model_enabled", true));
        CONFIG.Save();
        Emit("ui_changed", new PyDict()); return "独立 Ollama 与游戏工具模型已配置";
    });
}
