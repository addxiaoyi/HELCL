using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HelL.Core.Launcher.Ai.Local;

internal static class ModelQueueProcessCheck
{
    internal static int Run(string folder, bool recover)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        string partial = Path.Combine(folder, "fixture.partial");
        string queueFile = Path.Combine(folder, "queue.json");
        if (!recover && File.Exists(queueFile)) throw new IOException("Use an empty fixture directory for the interrupted run.");
        using var started = new ManualResetEventSlim();
        using var complete = new ManualResetEventSlim();
        using var queue = new ModelQueue(queueFile, async (job, progress, ct) =>
        {
            if (!recover)
            {
                File.WriteAllText(partial, "first-half");
                progress(10, 21, "downloading");
                started.Set();
                await Task.Delay(Timeout.Infinite, ct);
                return;
            }
            if (File.ReadAllText(partial) != "first-half") throw new IOException("Previous download bytes were lost.");
            File.AppendAllText(partial, "-second-half");
            complete.Set();
        });
        if (!recover)
        {
            queue.Enqueue(new ModelJob { Kind = "pull", Model = "restart-fixture", Endpoint = ModelRules.DefaultEndpoint, Durable = true });
            if (!started.Wait(3000)) throw new TimeoutException("Queue did not start.");
            Console.WriteLine("[QUEUE] Persisted running task and partial bytes, simulating abrupt exit.");
            Environment.Exit(37);
        }
        queue.Resume();
        if (!complete.Wait(3000)) throw new TimeoutException("Persisted queue did not resume.");
        for (int n = 0; n < 100 && queue.Current.State != "completed"; n++) Thread.Sleep(10);
        if (queue.Current.State != "completed" || File.ReadAllText(partial) != "first-half-second-half")
            throw new IOException("Restarted queue did not complete with preserved bytes.");
        Console.WriteLine("[QUEUE] New process restored the interrupted task and preserved its partial bytes.");
        return 0;
    }
}
