namespace HelL.Core.Launcher.Ai;

public static class Memory
{
	private static readonly object _LOCK = new object();

	private static NoopMemory _memory = new FileMemory();

	public static NoopMemory GetMemory()
	{
		lock (_LOCK)
		{
			return _memory;
		}
	}

	public static void SetMemory(NoopMemory impl)
	{
		lock (_LOCK)
		{
			_memory = impl ?? new NoopMemory();
		}
	}
}
