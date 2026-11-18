using System;
using System.Linq;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

internal sealed class BackendFixture : IDisposable
{
    private readonly TestHome home = new TestHome();
    internal EventBus Bus { get; } = new EventBus();
    internal BackendApi Api { get; }
    internal RpcDispatcher Rpc { get; }

    internal BackendFixture()
    {
        try
        {
            Api = new BackendApi(Bus, startBackgroundServices: false);
            Rpc = new RpcDispatcher(Api);
        }
        catch
        {
            home.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            BackendWorker[] workers;
            lock (Api._lock) workers = Api._workers.Values.ToArray();
            foreach (var worker in workers) worker.cancel();
            foreach (var worker in workers)
                Assert.True(worker.Join(5), "Contract test worker did not stop: " + worker.task_id);
        }
        finally { home.Dispose(); }
    }
}
