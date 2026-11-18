using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HelL.Core;

public sealed class EventBus
{
	public const int QueueMax = 800;

	public const string ResetEvent = "events_reset";

	private readonly object _lock = new object();

	private readonly List<BlockingCollection<PyDict>> _subs = new List<BlockingCollection<PyDict>>();

	public int SubscriberCount
	{
		get
		{
			lock (_lock)
			{
				return _subs.Count;
			}
		}
	}

	public void Emit(string @event, object data)
	{
		PyDict item = PyDict.Of("event", @event, "data", data);
		List<BlockingCollection<PyDict>> list;
		lock (_lock)
		{
			list = new List<BlockingCollection<PyDict>>(_subs);
		}
		foreach (BlockingCollection<PyDict> queue in list)
		{
			try
			{
				lock (queue)
				{
					if (!queue.IsAddingCompleted)
					{
						if (queue.TryAdd(item)) continue;
						// A gap invalidates the stream; reconnecting consumers must load a snapshot.
						while (queue.TryTake(out _)) { }
						queue.Add(PyDict.Of("event", ResetEvent, "data", PyDict.Of("reason", "queue_overflow")));
						queue.CompleteAdding();
					}
				}
				Unsubscribe(queue);
			}
			catch (ObjectDisposedException)
			{
				Unsubscribe(queue);
			}
			catch (InvalidOperationException) when (queue.IsAddingCompleted)
			{
				Unsubscribe(queue);
			}
		}
	}

	public BlockingCollection<PyDict> Subscribe()
	{
		BlockingCollection<PyDict> blockingCollection = new BlockingCollection<PyDict>(new ConcurrentQueue<PyDict>(), QueueMax);
		lock (_lock)
		{
			_subs.Add(blockingCollection);
			return blockingCollection;
		}
	}

	public void Unsubscribe(BlockingCollection<PyDict> q)
	{
		lock (_lock)
		{
			_subs.Remove(q);
		}
	}
}
