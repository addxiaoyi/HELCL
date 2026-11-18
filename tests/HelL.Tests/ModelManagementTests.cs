using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core;
using HelL.Core.Launcher.Ai;
using HelL.Core.Launcher.Ai.Local;
using Xunit;

namespace HelL.Tests;

public sealed class ModelManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hell-management-" + Guid.NewGuid().ToString("N"));
    public ModelManagementTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task QueuePersistsBeforeStartingAndRestoresInterruptedDownload()
    {
        string path = Path.Combine(_root, "queue.json");
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var queue = new ModelQueue(path, async (job, progress, ct) =>
        {
            Assert.True(File.Exists(path));
            progress(12, 40, "下载中"); started.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, ct);
        }))
        {
            queue.Enqueue(new ModelJob { Durable = true, Kind = "pull", Endpoint = ModelRules.DefaultEndpoint, Model = "qwen3:4b" });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        string resumedModel = "";
        using var restored = new ModelQueue(path, (job, progress, ct) => { resumedModel = job.Model; return Task.CompletedTask; });
        Assert.Equal("queued", Assert.Single(restored.Snapshot()).State);
        restored.Resume();
        await Until(() => restored.Current.State == "completed");
        Assert.Equal("qwen3:4b", resumedModel);
    }

    [Fact]
    public async Task CancelledQueueEntryDoesNotRestartAndRetryRunsOnce()
    {
        string path = Path.Combine(_root, "queue.json");
        using (var queue = new ModelQueue(path, async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct)))
        {
            var first = queue.Enqueue(new ModelJob { Durable = true, Kind = "pull", Model = "first" });
            await Until(() => queue.Current.State == "running");
            var second = queue.Enqueue(new ModelJob { Durable = true, Kind = "pull", Model = "second" });
            queue.Cancel(second.Id); queue.Cancel(first.Id);
            await Until(() => queue.Snapshot().All(job => job.State == "cancelled"));
        }
        int calls = 0;
        using var restored = new ModelQueue(path, (_, _, _) => { Interlocked.Increment(ref calls); return Task.CompletedTask; });
        restored.Resume(); await Task.Delay(50);
        Assert.Equal(0, calls);
        restored.Retry(restored.Snapshot()[1].Id);
        await Until(() => restored.Snapshot()[1].State == "completed");
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task StartServiceCanRunWhileQueueWaitsForService()
    {
        bool online = false;
        using var queue = new ModelQueue(Path.Combine(_root, "queue.json"), async (_, progress, ct) =>
        {
            while (!online) { progress(0, 0, "等待本地服务"); await Task.Delay(10, ct); }
        });
        queue.Enqueue(new ModelJob { Durable = true, Kind = "pull", Model = "first" });
        await Until(() => queue.Current.State == "waiting");
        queue.Enqueue(new ModelJob { Title = "启动服务" }, (_, _) => { online = true; return Task.CompletedTask; });
        await Until(() => queue.Snapshot().All(job => job.State == "completed"));
        Assert.True(online);
    }

    [Fact]
    public void CorruptQueueIsPreservedAndDoesNotRun()
    {
        string path = Path.Combine(_root, "queue.json"); File.WriteAllText(path, "broken");
        using var queue = new ModelQueue(path, (_, _, _) => throw new Exception("must not run"));
        Assert.NotEmpty(queue.LoadError);
        Assert.Throws<IOException>(() => queue.Enqueue(new ModelJob { Durable = true }));
        Assert.Equal("broken", File.ReadAllText(path));
    }

    [Fact]
    public void CacheCleaningValidatesSnapshotAndOnlyDeletesSelectedCache()
    {
        string folder = Path.Combine(_root, "downloads"); Directory.CreateDirectory(folder);
        string first = Path.Combine(folder, new string('a', 64) + ".gguf");
        string second = Path.Combine(folder, new string('b', 64) + ".gguf.partial");
        File.WriteAllText(first, "model"); File.WriteAllText(second, "partial");
        File.WriteAllText(Path.Combine(folder, "user-file.txt"), "preserve");
        var storage = new ModelStorage(_root);
        var files = storage.Inspect(Path.Combine(_root, "models")).Cache;
        Assert.Equal(2, files.Count);
        var selected = files.Single(file => !file.Partial);
        Assert.Equal(5, storage.DeleteCache(new[] { selected }));
        Assert.True(File.Exists(second)); Assert.True(File.Exists(Path.Combine(folder, "user-file.txt")));
        var partial = files.Single(file => file.Partial);
        File.AppendAllText(second, "changed");
        Assert.Throws<IOException>(() => storage.DeleteCache(new[] { partial }));
        Assert.Throws<ArgumentException>(() => storage.DeleteCache(new[] { new ModelCacheFile { Id = "../user-file.txt" } }));
    }

    [Fact]
    public void CompleteShardsAreOrderedAndMissingOrMixedShardsAreRejected()
    {
        var names = new[] { "model-Q4-00002-of-00002.gguf", "model-Q4-00001-of-00002.gguf" };
        Assert.Equal(names[1], GgufParts.Order(names)[0]);
        Assert.Throws<IOException>(() => GgufParts.Order(names.Take(1)));
        Assert.Throws<IOException>(() => GgufParts.Order(new[] { names[0], names[0] }));
        Assert.Throws<IOException>(() => GgufParts.Order(new[] { names[0], "other-00001-of-00002.gguf" }));
        var files = names.Select(name => new ModelFile { Name = name, Size = 100, Sha256 = new string('a', 64), Supported = true }).ToList();
        var group = Assert.Single(GgufParts.Group(files, new ModelHardware()));
        Assert.True(group.Supported); Assert.Equal(200, group.Size); Assert.Equal(2, group.Parts.Count);
        Assert.False(Assert.Single(GgufParts.Group(files.Take(1), new ModelHardware())).Supported);
    }

    [Fact]
    public void IncompleteToolStreamNeverEmitsExecutableCalls()
    {
        var frames = new[] { "{\"message\":{\"tool_calls\":[{\"function\":{\"name\":\"create_instance\",\"arguments\":{\"name\":\"test\"}}}]},\"done\":false}" };
        Assert.Throws<AIClientError>(() => LocalTools.Frames(frames.Select(Encoding.UTF8.GetBytes)).ToList());
        var complete = LocalTools.Frames(frames.Concat(new[] { "{\"done\":true}" }).Select(Encoding.UTF8.GetBytes)).ToList();
        Assert.Single(complete, frame => Py.S(frame.Get("type")) == "tool_calls");
        var settings = LocalTools.Settings(PyDict.Of("ai_permission_mode", "yolo", "ai_permission_dont_ask", true, "ai_fallback_model", "cloud"));
        Assert.Equal("default", settings["ai_permission_mode"]); Assert.Equal(false, settings["ai_permission_dont_ask"]); Assert.Equal("", settings["ai_fallback_model"]);
        Assert.Equal("plan", LocalTools.Settings(PyDict.Of("ai_permission_mode", "plan"))["ai_permission_mode"]);
    }

    private static async Task Until(Func<bool> ready)
    { for (int n = 0; n < 200; n++) { if (ready()) return; await Task.Delay(10); } Assert.True(ready()); }
}
