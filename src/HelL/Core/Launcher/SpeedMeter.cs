using System.Collections.Generic;

namespace HelL.Core.Launcher;

public sealed class SpeedMeter
{
	public readonly double window;

	private readonly object _lock = new object();

	private readonly LinkedList<KeyValuePair<double, long>> _samples = new LinkedList<KeyValuePair<double, long>>();

	public SpeedMeter(double window = 1.5)
	{
		this.window = window;
	}

	public void Add(long n)
	{
		if (n <= 0)
		{
			return;
		}
		double num = Py.Monotonic();
		lock (_lock)
		{
			_samples.AddLast(new KeyValuePair<double, long>(num, n));
			Trim(num);
		}
	}

	public void Reset()
	{
		lock (_lock)
		{
			_samples.Clear();
		}
	}

	public double Speed()
	{
		double num = Py.Monotonic();
		lock (_lock)
		{
			Trim(num);
			if (_samples.Count == 0)
			{
				return 0.0;
			}
			double num2 = num - _samples.First.Value.Key;
			long num3 = 0L;
			foreach (KeyValuePair<double, long> sample in _samples)
			{
				num3 += sample.Value;
			}
			if (num2 <= 0.05)
			{
				return (double)num3 / 0.05;
			}
			return (double)num3 / num2;
		}
	}

	private void Trim(double now)
	{
		double num = now - window;
		while (_samples.Count > 0 && _samples.First.Value.Key < num)
		{
			_samples.RemoveFirst();
		}
	}
}
