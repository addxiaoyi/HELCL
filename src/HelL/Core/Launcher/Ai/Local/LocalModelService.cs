using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class LocalModelService
{
    private readonly object _gate = new object();
    private readonly HttpClient _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _root;
    private readonly ModelQueue _queue;
    private ModelHardware? _hardware;
    private RuntimePackage? _package;
    public LocalRuntime Runtime { get; }
    public ModelJob Job => _queue.Current;
    public List<ModelJob> Queue => _queue.Snapshot();
    public string QueueError => _queue.LoadError;
    public bool Pending => _queue.Pending;

    public LocalModelService(string root)
    {
        _root = root;
        Runtime = new LocalRuntime(root, _http);
        _queue = new ModelQueue(Path.Combine(root, "queue.json"), RunQueued);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            _queue.Dispose();
            try { Runtime.Stop(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("LocalModelService shutdown: " + ex); }
        };
    }

    public ModelHardware Hardware(bool refresh = false)
    {
        lock (_gate)
        {
            if (_hardware != null && !refresh) return _hardware;
            Directory.CreateDirectory(_root);
            var memory = Sysinfo.MemoryInfo();
            var hardware = new ModelHardware { TotalMemory = Py.Int(memory.Get("total_bytes")), AvailableMemory = Py.Int(memory.Get("avail_bytes")),
                DiskPath = Runtime.ModelsPath, FreeDisk = Math.Min(new DriveInfo(Path.GetPathRoot(_root)!).AvailableFreeSpace,
                    new DriveInfo(Path.GetPathRoot(Runtime.ModelsPath)!).AvailableFreeSpace) };
            try
            {
                using var query = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                query.Options.Timeout = TimeSpan.FromSeconds(3);
                using var adapters = query.Get();
                var names = new List<string>();
                foreach (ManagementObject adapter in adapters)
                {
                    using (adapter)
                    {
                        string name = Convert.ToString(adapter["Name"]) ?? "GPU";
                        if (name.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Oray", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        names.Add(name);
                        hardware.VideoMemory = Math.Max(hardware.VideoMemory, Convert.ToInt64(adapter["AdapterRAM"] ?? 0));
                    }
                }
                if (names.Count > 0) hardware.Gpu = string.Join(" / ", names);
                hardware.Note = "显存来自系统报告，部分显卡可能少报；推荐按可用系统内存保守估算。运行 Minecraft 时建议选择更小模型。";
            }
            catch (Exception ex) when (ex is ManagementException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException)
            { hardware.Note = "显卡信息不可用，推荐仅按系统内存估算：" + ex.Message; }
            return _hardware = hardware;
        }
    }

    public async Task<LocalModelStatus> StatusAsync(string endpoint, bool detect, CancellationToken ct)
    {
        var status = await new LocalModelDiscovery().FindAsync(endpoint, detect, ct).ConfigureAwait(false);
        status.RuntimePath = Runtime.FindExecutable();
        status.Managed = Runtime.Managed;
        status.Hardware = Hardware();
        status.Job = Job;
        return status;
    }

    public void Resume() => _queue.Resume();
    public ModelJob Start(string title, Func<Action<long, long, string>, CancellationToken, Task> work) =>
        _queue.Enqueue(new ModelJob { Title = title, Kind = "operation" }, work);
    public ModelJob Cancel(string id = "") => _queue.Cancel(id);
    public ModelJob Retry(string id) => _queue.Retry(id);

    public async Task<RuntimePackage> PackageAsync() => _package = await Runtime.PackageAsync(CancellationToken.None).ConfigureAwait(false);
    public ModelJob Install() => Start("安装 Ollama", (progress, ct) => Runtime.InstallAsync(_package ?? throw new InvalidOperationException("请先查询安装包信息。"), progress, ct));
    public ModelJob StartRuntime(string endpoint) => Start("启动本地服务", (_, ct) => Runtime.StartAsync(endpoint, ct));

    public ModelJob Pull(string endpoint, string name)
    {
        ModelRules.ModelName(name);
        var model = ModelRules.Catalog(Hardware(true)).FirstOrDefault(item => item.Name == name);
        if (model != null && !model.Fit.CanDownload) throw new IOException(model.Fit.Reason);
        return _queue.Enqueue(new ModelJob { Kind = "pull", Title = "下载 " + name, Model = name, Endpoint = ModelRules.Endpoint(endpoint), Durable = true });
    }

    public ModelJob Load(string endpoint, string name, bool unload) => Start((unload ? "释放 " : "加载 ") + ModelRules.ModelName(name), async (progress, ct) =>
    {
        using var client = new OllamaClient(endpoint);
        await client.StreamAsync("/api/generate", new { model = name, prompt = "", keep_alive = unload ? "0" : "5m", stream = true, options = new { num_ctx = 4096 } }, progress, ct).ConfigureAwait(false);
    });

    public Task<List<string>> Search(string source, string query) => new ModelRepository(_http, source).SearchAsync(query, CancellationToken.None);
    public Task<List<ModelFile>> Files(string source, string repository) => new ModelRepository(_http, source).FilesAsync(repository, Hardware(), CancellationToken.None);

    public ModelJob Download(string endpoint, string source, string repository, string file, string name)
    {
        ModelRules.ModelName(name);
        var hub = new ModelRepository(_http, source);
        var files = hub.FilesAsync(repository, Hardware(true), CancellationToken.None).GetAwaiter().GetResult();
        var selected = files.FirstOrDefault(entry => entry.Name == file) ?? throw new IOException("仓库中已找不到所选文件，请刷新列表。");
        if (!selected.Supported) throw new IOException(selected.Note);
        if (!selected.Fit.CanDownload) throw new IOException(selected.Fit.Reason);
        return _queue.Enqueue(new ModelJob { Kind = "gguf", Title = "下载并导入 " + name, Model = name,
            Endpoint = ModelRules.Endpoint(endpoint), Source = hub.Source, Durable = true,
            Files = selected.Parts.Count > 0 ? selected.Parts : new List<ModelFile> { selected } });
    }

    private async Task RunQueued(ModelJob job, Action<long, long, string> progress, CancellationToken ct)
    {
        string endpoint = ModelRules.Endpoint(job.Endpoint);
        ModelRules.ModelName(job.Model);
        using var client = new OllamaClient(endpoint);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { await client.ModelsAsync(ct).ConfigureAwait(false); break; }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                progress(0, 0, "等待本地服务 " + endpoint + "；启动 Ollama 后自动继续");
                await Task.Delay(5000, ct).ConfigureAwait(false);
            }
        }
        if (job.Kind == "pull")
        {
            await client.StreamAsync("/api/pull", new { model = job.Model, stream = true }, progress, ct).ConfigureAwait(false);
            return;
        }
        if (job.Kind != "gguf") throw new IOException("无法恢复未知类型的任务。");
        var hub = new ModelRepository(_http, job.Source);
        var names = GgufParts.Order(job.Files.Select(file => file.Name));
        long total = job.Files.Sum(file => file.Size);
        if (total <= 0) throw new IOException("下载记录缺少文件大小。");
        if (!ModelRules.Assess(total, Hardware(true), true).CanDownload)
            throw new IOException("剩余磁盘空间不足以下载并导入这组模型。请释放空间后重试。");
        long completed = 0;
        var imports = new Dictionary<string, string>();
        foreach (string name in names)
        {
            var part = job.Files.Single(file => file.Name == name);
            if (!System.Text.RegularExpressions.Regex.IsMatch(part.Sha256, @"\A[a-fA-F0-9]{64}\z")) throw new IOException("下载记录的校验值无效。");
            string path = Path.Combine(_root, "downloads", part.Sha256 + ".gguf");
            long previous = completed;
            await new ModelDownload(_http).DownloadAsync(hub.FileUri(part), path, part.Size, part.Sha256,
                (current, _, message) => progress(previous + current, total, message + " · " + name), ct).ConfigureAwait(false);
            completed += part.Size;
            imports.Add(Path.GetFileName(part.Name), path);
        }
        await client.ImportFilesAsync(imports, job.Model, progress, ct).ConfigureAwait(false);
    }

    public ModelStorageInfo Storage() => new ModelStorage(_root).Inspect(Runtime.ModelsPath);
    public long CleanCache(List<ModelCacheFile> files)
    {
        return _queue.WhenIdle(() => new ModelStorage(_root).DeleteCache(files));
    }

    public void DeleteModel(string endpoint, string name) => _queue.WhenIdle(() =>
    {
        using var client = new OllamaClient(endpoint);
        client.DeleteAsync(name, CancellationToken.None).GetAwaiter().GetResult();
        return true;
    });

    public ModelJob Import(string endpoint, string path, string name) => Start("导入 " + ModelRules.ModelName(name), async (progress, ct) =>
    {
        if (!File.Exists(path)) throw new FileNotFoundException("模型文件已不存在。", path);
        long bytes = GgufParts.LocalFiles(path).Sum(file => new FileInfo(file).Length);
        if (!ModelRules.Assess(bytes, Hardware(true)).CanDownload) throw new IOException("本地模型存储空间不足，无法复制导入。");
        using var client = new OllamaClient(endpoint);
        await client.ImportAsync(path, name, progress, ct).ConfigureAwait(false);
    });

    public static List<LocalModelInfo> ScanFolder(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("模型文件夹不存在。");
        var files = new List<LocalModelInfo>();
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((Path.GetFullPath(folder), 0));
        int visited = 0;
        while (pending.Count > 0)
        {
            if (++visited > 2000) throw new IOException("文件夹过大，请选择更具体的模型目录。");
            var current = pending.Dequeue();
            foreach (string path in Directory.EnumerateFiles(current.Path, "*.gguf"))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                files.Add(new LocalModelInfo { Name = info.Name, Path = info.FullName, Size = info.Length });
                if (files.Count >= 300) return files;
            }
            if (current.Depth >= 3) continue;
            foreach (string child in Directory.EnumerateDirectories(current.Path))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Enqueue((child, current.Depth + 1));
        }
        return files;
    }
}
