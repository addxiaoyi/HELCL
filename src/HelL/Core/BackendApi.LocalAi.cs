using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using HelL.Core.Launcher;
using HelL.Core.Launcher.Ai.Local;

namespace HelL.Core;

public partial class BackendApi
{
    private static readonly string[] LocalSettingKeys = { "ai_local_url", "ai_local_model", "ai_local_tools", "ai_local_vision", "ai_local_context", "ai_cloud_mode", "ai_model_source" };
    private readonly Lazy<LocalModelService> _localModels = new Lazy<LocalModelService>(() => new LocalModelService(Path.Combine(Utils.ROOT, "ai-local")));
    private LocalModelService LocalModels => _localModels.Value;
    private string LocalEndpoint => ModelRules.Endpoint(Py.S(CONFIG.Get("ai_local_url", ModelRules.DefaultEndpoint)));
    private static object LocalJson(object value) => PyJson.Loads(JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

    public object ai_local_status(bool detect = false)
    {
        var status = LocalModels.StatusAsync(LocalEndpoint, detect, CancellationToken.None).GetAwaiter().GetResult();
        return LocalJson(status);
    }

    public object ai_local_model_choices()
    {
        var status = LocalModels.StatusAsync(LocalEndpoint, true, CancellationToken.None).GetAwaiter().GetResult();
        if (!status.Online && status.DiscoveredEndpoint.Length > 0)
            status = LocalModels.StatusAsync(status.DiscoveredEndpoint, false, CancellationToken.None).GetAwaiter().GetResult();
        return LocalJson(status);
    }

    private string CloudMode()
    {
        string mode = Py.S(CONFIG.Get("ai_mode", "custom"));
        if (mode == "local") mode = Py.S(CONFIG.Get("ai_cloud_mode", "custom"));
        return mode == "public" ? "public" : "custom";
    }

    public object ai_cloud_model_choices()
    {
        var settings = _settings_full();
        settings["ai_mode"] = CloudMode();
        var listed = ai_list_models(settings) as PyDict ?? throw new InvalidOperationException("云端模型响应无效。");
        if (listed.Get("models") is System.Collections.Generic.List<object> models)
        {
            string selected = Py.S(settings.Get("ai_model"));
            if (selected.Length > 0 && !models.Any(model => Py.S(model) == selected)) models.Insert(0, selected);
        }
        return listed;
    }

    public object ai_cloud_select(string model)
    {
        model = (model ?? "").Trim();
        if (model.Length == 0 || model.Length > 240 || model.Any(char.IsControl)) throw new ArgumentException("请输入有效的云端模型名称。");
        lock (_ai_lock)
        {
            if (_ai_busy) throw new InvalidOperationException("请等当前回复结束后再切换模型。");
            string mode = CloudMode();
            CONFIG.Update(PyDict.Of("ai_mode", mode, "ai_cloud_mode", mode, "ai_model", model, "minecraft_model_enabled", false));
        }
        return PyDict.Of("ok", true);
    }

    public object ai_local_catalog() => LocalJson(ModelRules.Catalog(LocalModels.Hardware(true)));
    public object ai_local_job() => LocalJson(LocalModels.Job);
    public object ai_local_cancel(string id = "") => LocalJson(LocalModels.Cancel(id));
    public object ai_local_queue() => PyDict.Of("jobs", LocalJson(LocalModels.Queue), "error", LocalModels.QueueError);
    public object ai_local_retry(string id) => LocalJson(LocalModels.Retry(id));
    public object ai_local_storage()
    {
        var storage = LocalModels.Storage();
        var status = LocalModels.StatusAsync(LocalEndpoint, false, CancellationToken.None).GetAwaiter().GetResult();
        storage.InstalledModelBytes = status.Models.Sum(model => model.Size);
        return LocalJson(storage);
    }
    public object ai_local_clean_cache(object files)
    {
        RequireLocalIdle();
        var selected = JsonSerializer.Deserialize<System.Collections.Generic.List<ModelCacheFile>>(PyJson.Dumps(files),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
            ?? throw new ArgumentException("请选择缓存文件。");
        return PyDict.Of("freed", LocalModels.CleanCache(selected));
    }
    public object ai_local_delete(string model)
    {
        RequireLocalIdle();
        LocalModels.DeleteModel(LocalEndpoint, ModelRules.ModelName(model));
        string selected = Py.S(CONFIG.Get("ai_local_model"));
        if (selected == model || selected + ":latest" == model || selected == model + ":latest") CONFIG.Update(PyDict.Of("ai_local_model", "", "ai_local_tools", false));
        return PyDict.Of("ok", true);
    }
    public object ai_local_package() => LocalJson(LocalModels.PackageAsync().GetAwaiter().GetResult());
    public object ai_local_install() => LocalJson(LocalModels.Install());
    public object ai_local_start() => LocalJson(LocalModels.StartRuntime(LocalEndpoint));
    public object ai_local_stop()
    {
        if (_ai_busy) throw new InvalidOperationException("正在生成回复，请先停止回复。");
        if (LocalModels.Job.State != "waiting") RequireLocalIdle();
        LocalModels.Runtime.Stop();
        return PyDict.Of("ok", true);
    }
    public object ai_local_pull(string model) => LocalJson(LocalModels.Pull(LocalEndpoint, model));
    public object ai_local_load(string model, bool unload = false)
    {
        RequireLocalIdle();
        return LocalJson(LocalModels.Load(LocalEndpoint, model, unload));
    }
    public object ai_local_search(string source, string query) => LocalJson(LocalModels.Search(source, query).GetAwaiter().GetResult());
    public object ai_local_files(string source, string repository) => LocalJson(LocalModels.Files(source, repository).GetAwaiter().GetResult());
    public object ai_local_download(string source, string repository, string file, string model) => LocalJson(LocalModels.Download(LocalEndpoint, source, repository, file, model));
    public object ai_local_scan(string folder) => LocalJson(LocalModelService.ScanFolder(folder));
    public object ai_local_import(string path, string model) => LocalJson(LocalModels.Import(LocalEndpoint, path, model));

    public object ai_local_select(string model, string endpoint = "")
    {
        if (_ai_busy) throw new InvalidOperationException("请等当前回复结束后再切换模型。");
        ModelRules.ModelName(model);
        endpoint = endpoint.Length == 0 ? LocalEndpoint : ModelRules.Endpoint(endpoint);
        using var client = new OllamaClient(endpoint);
        var installed = client.ModelsAsync(CancellationToken.None).GetAwaiter().GetResult();
        var selected = installed.FirstOrDefault(entry => entry.Name == model || entry.Name == model + ":latest");
        if (selected == null)
            throw new InvalidOperationException("该模型尚未安装，请先在模型库下载或导入。");
        model = selected.Name;
        var details = client.JsonAsync("/api/show", new { model }, CancellationToken.None).GetAwaiter().GetResult();
        if (OllamaClient.Text(details, "remote_model").Length > 0 || OllamaClient.Text(details, "remote_host").Length > 0)
            throw new InvalidOperationException("该模型会转发到云端，不能用于本地聊天。请选择已下载的本地权重。");
        bool tools = details.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array && caps.EnumerateArray().Any(cap => cap.GetString() == "tools");
        lock (_ai_lock)
        {
            if (_ai_busy) throw new InvalidOperationException("当前回复已开始，请结束后再切换模型。");
            string current = Py.S(CONFIG.Get("ai_mode"));
            var changes = PyDict.Of("ai_mode", "local", "ai_local_url", endpoint, "ai_local_model", model, "ai_local_tools", false,
                "ai_local_vision", false, "ai_local_context", 4096L, "minecraft_model_enabled", false);
            if (current != "local") changes["ai_cloud_mode"] = current;
            CONFIG.Update(changes);
        }
        return PyDict.Of("ok", true, "tools_available", tools, "model", model);
    }

    public object ai_local_cloud()
    {
        if (_ai_busy) throw new InvalidOperationException("请等当前回复结束后再切换连接。");
        string mode = Py.S(CONFIG.Get("ai_cloud_mode", "custom"));
        if (mode != "custom" && mode != "public" && mode != "builtin") mode = "custom";
        CONFIG.Update(PyDict.Of("ai_mode", mode));
        return PyDict.Of("ok", true);
    }

    public object ai_local_tool_mode(bool enabled)
    {
        if (_ai_busy) throw new InvalidOperationException("请先结束当前回复，再修改工具模式。");
        if (enabled)
        {
            var settings = _settings_full();
            if (Py.S(settings.Get("ai_mode")) != "local" || Py.S(settings.Get("ai_local_model")).Length == 0)
                throw new InvalidOperationException("请先在已安装列表中使用一个本地模型。");
            Launcher.Ai.Local.LocalTools.Verify(settings);
        }
        CONFIG.Update(PyDict.Of("ai_local_tools", enabled, "ai_local_context", enabled ? 16384L : 4096L));
        return PyDict.Of("ok", true, "enabled", enabled);
    }

    private void RequireLocalIdle()
    {
        if (_ai_busy) throw new InvalidOperationException("正在生成回复，请先停止回复。");
        if (LocalModels.Pending) throw new InvalidOperationException("本地模型任务尚未结束，请等待或先取消。");
    }
}
