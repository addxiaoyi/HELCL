using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HelL.Core;

public sealed class EventBus
{
	public const int QueueMax = 800;

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
		foreach (BlockingCollection<PyDict> item2 in list)
		{
			try
			{
				item2.TryAdd(item);
			}
			catch (Exception)
			{
			}
		}
	}

	public BlockingCollection<PyDict> Subscribe()
	{
		BlockingCollection<PyDict> blockingCollection = new BlockingCollection<PyDict>(new ConcurrentQueue<PyDict>(), 800);
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
