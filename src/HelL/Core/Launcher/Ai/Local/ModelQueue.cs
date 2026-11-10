using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai.Local;

public sealed class ModelQueue : IDisposable
{
    private readonly object _gate = new object();
    private readonly string _path;
    private readonly Func<ModelJob, Action<long, long, string>, CancellationToken, Task> _execute;
    private readonly Dictionary<string, Func<Action<long, long, string>, CancellationToken, Task>> _transient = new Dictionary<string, Func<Action<long, long, string>, CancellationToken, Task>>();
    private readonly List<ModelJob> _jobs;
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private bool _stopping;
    private bool _preempt;
    private string _active = "";
    public string LoadError { get; private set; } = "";

    public ModelQueue(string path, Func<ModelJob, Action<long, long, string>, CancellationToken, Task> execute)
    {
        _path = Path.GetFullPath(path);
        _execute = execute;
        _jobs = Load();
    }

    public List<ModelJob> Snapshot() { lock (_gate) return _jobs.Select(Clone).ToList(); }
    public bool Pending { get { lock (_gate) return _jobs.Any(job => IsPending(job.State)); } }
    public ModelJob Current { get { lock (_gate) return Clone(_jobs.FirstOrDefault(job => job.Id == _active) ?? _jobs.FirstOrDefault(job => job.State == "queued") ?? _jobs.LastOrDefault() ?? new ModelJob()); } }

    public static bool IsPending(string state) => state is "queued" or "running" or "waiting" or "cancelling";
    private static ModelJob Clone(ModelJob job) => JsonSerializer.Deserialize<ModelJob>(JsonSerializer.Serialize(job))!;

    public ModelJob Enqueue(ModelJob job, Func<Action<long, long, string>, CancellationToken, Task>? transient = null)
    {
        lock (_gate)
        {
            if (_stopping) throw new InvalidOperationException("应用正在关闭，无法添加下载任务。");
            if (LoadError.Length > 0) throw new IOException(LoadError);
            if (_jobs.Any(existing => IsPending(existing.State) && existing.Endpoint == job.Endpoint && existing.Model == job.Model && existing.Kind == job.Kind && existing.Source == job.Source))
                throw new InvalidOperationException("相同模型已有任务在队列中。");
            if (_jobs.Count(existing => IsPending(existing.State)) >= 100) throw new InvalidOperationException("下载队列已满，请先完成部分任务。");
            job = Clone(job);
            job.Id = Guid.NewGuid().ToString("N");
            job.State = "queued";
            job.Message = "等待开始";
            job.CreatedAt = DateTime.UtcNow.ToString("O");
            var completed = _jobs.Where(entry => !IsPending(entry.State)).ToList();
            foreach (var old in completed.Take(Math.Max(0, completed.Count - 199))) { _jobs.Remove(old); _transient.Remove(old.Id); }
            _jobs.Add(job);
            if (transient != null) _transient[job.Id] = transient;
            try { Save(); }
            catch { _jobs.Remove(job); _transient.Remove(job.Id); throw; }
            if (transient != null && _jobs.Any(entry => entry.Id == _active && entry.State == "waiting"))
            { _preempt = true; _cancel?.Cancel(); }
            Resume();
            return Clone(job);
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_stopping || LoadError.Length > 0 || _worker != null && !_worker.IsCompleted) return;
            _worker = Task.Run(Drain);
        }
    }

    public ModelJob Cancel(string id = "")
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(entry => entry.Id == (id.Length > 0 ? id : _active));
            if (job == null) return Current;
            if (IsPending(job.State))
            {
                job.State = job.Id == _active ? "cancelling" : "cancelled";
                job.Message = "已取消，下载部分保留";
                Save();
                if (job.Id == _active) _cancel?.Cancel();
            }
            return Clone(job);
        }
    }

    public ModelJob Retry(string id)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(entry => entry.Id == id) ?? throw new ArgumentException("找不到该下载任务。");
            if (job.State is not ("failed" or "cancelled")) throw new InvalidOperationException("仅失败或取消的任务可以重试。");
            if (!job.Durable && !_transient.ContainsKey(id)) throw new InvalidOperationException("请重新执行这项操作。");
            job.State = "queued";
            job.Message = "等待重试";
            Save();
            Resume();
            return Clone(job);
        }
    }

    private async Task Drain()
    {
        while (true)
        {
            ModelJob job;
            CancellationToken token;
            lock (_gate)
            {
                if (_stopping) { _worker = null; return; }
                job = _jobs.Where(entry => entry.State == "queued").OrderBy(entry => entry.Durable).FirstOrDefault()!;
                if (job == null) { _worker = null; return; }
                _active = job.Id;
                _cancel?.Dispose();
                _cancel = new CancellationTokenSource();
                token = _cancel.Token;
                job.State = "running";
                job.Message = "准备中";
                try { Save(); }
                catch (Exception ex) { LoadError = "无法保存任务状态：" + ex.Message; _active = ""; _worker = null; return; }
            }
            try
            {
                Action<long, long, string> progress = (current, total, message) =>
                {
                    lock (_gate)
                    {
                        job.Completed = current; job.Total = total; job.Message = message;
                        if (job.State != "cancelling") job.State = message.StartsWith("等待本地服务", StringComparison.Ordinal) ? "waiting" : "running";
                    }
                };
                Func<Action<long, long, string>, CancellationToken, Task>? run;
                lock (_gate) _transient.TryGetValue(job.Id, out run);
                if (run != null) await run(progress, token).ConfigureAwait(false);
                else await _execute(Clone(job), progress, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (_gate) { job.State = "completed"; job.Message = "已完成"; }
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    bool resume = (_stopping || _preempt) && job.Durable && job.State != "cancelling";
                    job.State = resume ? "queued" : token.IsCancellationRequested ? "cancelled" : "failed";
                    job.Message = resume ? "等待恢复下载" : token.IsCancellationRequested ? "已取消，下载部分保留" : ex.Message;
                }
                if (!token.IsCancellationRequested) System.Diagnostics.Trace.TraceError("ModelQueue: " + ex);
            }
            lock (_gate)
            {
                _active = "";
                _preempt = false;
                try { Save(); }
                catch (Exception ex) { LoadError = "任务状态保存失败：" + ex.Message; _worker = null; return; }
            }
        }
    }

    private List<ModelJob> Load()
    {
        if (!File.Exists(_path)) return new List<ModelJob>();
        try
        {
            if (new FileInfo(_path).Length > 16 * 1024 * 1024) throw new IOException("队列记录过大。");
            var jobs = JsonSerializer.Deserialize<List<ModelJob>>(File.ReadAllText(_path)) ?? throw new IOException("队列记录为空。");
            if (jobs.Any(job => job == null || string.IsNullOrWhiteSpace(job.Id)) || jobs.Select(job => job.Id).Distinct().Count() != jobs.Count)
                throw new IOException("队列记录不完整。");
            foreach (var job in jobs.Where(job => IsPending(job.State)))
            {
                bool resume = job.Durable && job.State != "cancelling";
                job.State = resume ? "queued" : "cancelled";
                job.Message = resume ? "正在恢复上次下载" : "上次操作已结束，请重新操作";
            }
            return jobs;
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
        { LoadError = "无法读取下载队列，原文件已保留：" + ex.Message; return new List<ModelJob>(); }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_jobs));
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        if (File.Exists(_path)) File.Replace(temporary, _path, null);
        else File.Move(temporary, _path);
    }

    public T WhenIdle<T>(Func<T> action)
    {
        lock (_gate)
        {
            if (_jobs.Any(job => IsPending(job.State))) throw new InvalidOperationException("请先完成或取消下载队列，再执行此操作。");
            return action();
        }
    }

    public void Dispose()
    {
        Task? worker;
        lock (_gate) { _stopping = true; _cancel?.Cancel(); worker = _worker; }
        worker?.Wait(TimeSpan.FromSeconds(5));
    }
}
