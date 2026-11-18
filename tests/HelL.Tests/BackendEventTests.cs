using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HelL.Core;
using Xunit;

namespace HelL.Tests;

public sealed class BackendEventTests
{
    [Fact]
    public void SuccessfulTaskPublishesOrderedEventsAndRetainsItsRpcReceipt()
    {
        using var fixture = new BackendFixture();
        using var events = fixture.Bus.Subscribe();
        string task = fixture.Api.StartTask("contract task", context =>
        {
            Assert.Same(context, TaskContext.Current);
            context.Progress(1L, 2L, "halfway");
            context.Log("working");
            return "complete";
        });
        var timeline = Take(events, 7);
        Assert.Equal(new[] { "task_added", "task_count_changed", "progress", "log", "finished", "task_count_changed", "ui_changed" }, timeline.Select(row => Py.S(row["event"])));
        AssertPayload(timeline[0], PyDict.Of("task_id", task, "title", "contract task"));
        AssertPayload(timeline[1], PyDict.Of("count", 1L));
        AssertPayload(timeline[2], PyDict.Of("task_id", task, "current", 1L, "total", 2L, "message", "halfway"));
        AssertPayload(timeline[3], PyDict.Of("task_id", task, "text", "working"));
        AssertPayload(timeline[4], PyDict.Of("task_id", task, "success", true, "message", "complete"));
        AssertPayload(timeline[5], PyDict.Of("count", 0L));
        AssertPayload(timeline[6], new PyDict());
        var receipt = Assert.IsType<PyDict>(fixture.Rpc.Call("wait_task", PyDict.Of("task_id", task)));
        Assert.Equal(new[] { "message", "ok", "task_id" }, receipt.Keys.OrderBy(key => key));
        Assert.Equal(true, receipt["ok"]);
        Assert.Equal("complete", receipt["message"]);
        Assert.Equal(task, receipt["task_id"]);
        Assert.Empty((List<object>)fixture.Api.list_tasks()["running"]);
        Assert.Single((List<object>)fixture.Api.list_tasks()["finished"]);
        Assert.Equal("contract task", fixture.Rpc.Call("task_title", new List<object> { task }));
        fixture.Bus.Unsubscribe(events);
    }

    [Fact]
    public void CancellationFinishesOnceAndDoesNotEmitUiSuccess()
    {
        using var fixture = new BackendFixture();
        using var events = fixture.Bus.Subscribe();
        string task = fixture.Api.StartTask("cancel task", context =>
        {
            if (!context.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Cancellation was not delivered to the task.");
            context.Token.ThrowIfCancellationRequested();
            return "unexpected completion";
        });
        fixture.Rpc.Call("cancel_task", PyDict.Of("task_id", task));
        var timeline = Take(events, 4);
        Assert.Equal(new[] { "task_added", "task_count_changed", "finished", "task_count_changed" }, timeline.Select(row => Py.S(row["event"])));
        AssertPayload(timeline[2], PyDict.Of("task_id", task, "success", false, "message", "已取消"));
        var receipt = fixture.Api.wait_task(task, 0L);
        Assert.False(Py.Truthy(receipt["ok"]));
        Assert.Equal("已取消", receipt["message"]);
        fixture.Rpc.Call("cancel_task", PyDict.Of("task_id", task));
        Assert.Empty(events);
        fixture.Bus.Unsubscribe(events);
    }

    [Fact]
    public void CrashPrecedesFailedCompletionAndPreservesTheTaskId()
    {
        using var fixture = new BackendFixture();
        using var events = fixture.Bus.Subscribe();
        string task = fixture.Api.StartTask("crash task", _ => throw new TestCrash());
        var timeline = Take(events, 6);
        Assert.Equal(new[] { "task_added", "task_count_changed", "log", "crash", "finished", "task_count_changed" }, timeline.Select(row => Py.S(row["event"])));
        AssertPayload(timeline[3], PyDict.Of("reason", "fixture", "task_id", task));
        var finished = Assert.IsType<PyDict>(timeline[4]["data"]);
        Assert.Equal(task, finished["task_id"]);
        Assert.False(Py.Truthy(finished["success"]));
        Assert.True(Py.Truthy(finished["crash"]));
        Assert.Equal("fixture crash", finished["message"]);
        Assert.False(Py.Truthy(fixture.Api.wait_task(task, 0L)["ok"]));
        fixture.Bus.Unsubscribe(events);
    }

    [Fact]
    public void EventSubscriptionsPreservePayloadAndStopAfterUnsubscribe()
    {
        var bus = new EventBus();
        using var first = bus.Subscribe();
        using var second = bus.Subscribe();
        var payload = PyDict.Of("instance", "世界");
        bus.Emit("instance_changed", payload);
        AssertPayload(Assert.Single(Take(first, 1)), payload);
        AssertPayload(Assert.Single(Take(second, 1)), payload);
        bus.Unsubscribe(first);
        bus.Emit("ui_changed", new PyDict());
        Assert.Empty(first);
        Assert.Equal("ui_changed", Take(second, 1)[0]["event"]);
        bus.Unsubscribe(second);
        Assert.Equal(0, bus.SubscriberCount);
    }

    private static List<PyDict> Take(BlockingCollection<PyDict> events, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var timeline = new List<PyDict>();
        for (int index = 0; index < count; index++)
        {
            Assert.True(events.TryTake(out var entry, Timeout.Infinite, timeout.Token), "Expected backend event was not delivered.");
            Assert.Equal(new[] { "data", "event" }, entry.Keys.OrderBy(key => key));
            timeline.Add(entry);
        }
        return timeline;
    }

    private static void AssertPayload(PyDict entry, PyDict expected)
    {
        var payload = Assert.IsType<PyDict>(entry["data"]);
        Assert.Equal(expected.Keys.OrderBy(key => key), payload.Keys.OrderBy(key => key));
        foreach (var field in expected) Assert.Equal(field.Value, payload[field.Key]);
    }

    private sealed class TestCrash : Exception, ICrashReportException
    {
        public TestCrash() : base("fixture crash") { }
        public PyDict Report => PyDict.Of("reason", "fixture");
    }
}
