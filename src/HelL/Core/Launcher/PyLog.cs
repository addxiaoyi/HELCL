using System;
using System.Text;

namespace HelL.Core.Launcher;

public sealed class PyLog
{
	public int Level = 20;

	public static Action<string> Sink = delegate(string s)
	{
		try
		{
			Console.Error.WriteLine(s);
		}
		catch
		{
		}
	};

	public string Name { get; }

	public PyLog(string name)
	{
		Name = name;
	}

	public static string Percent(string fmt, object[] args)
	{
		if (args == null || args.Length == 0)
		{
			return fmt;
		}
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		for (int i = 0; i < fmt.Length; i++)
		{
			char c = fmt[i];
			if (c == '%' && i + 1 < fmt.Length)
			{
				char c2 = fmt[i + 1];
				switch (c2)
				{
				case '%':
					stringBuilder.Append('%');
					i++;
					continue;
				case 'd':
				case 'r':
				case 's':
					if (num < args.Length)
					{
						StringBuilder stringBuilder2 = stringBuilder;
						stringBuilder2.Append(c2 switch
						{
							'd' => Py.Int(args[num]).ToString(),
							'r' => Py.Repr(args[num]),
							_ => Py.Str(args[num]),
						});
						num++;
						i++;
						continue;
					}
					break;
				}
			}
			stringBuilder.Append(c);
		}
		return stringBuilder.ToString();
	}

	private void Emit(int level, string lname, string fmt, object[] args)
	{
		if (level >= Level)
		{
			Sink($"[{DateTime.Now:HH:mm:ss}] {lname} {Name}: {Percent(fmt, args)}");
		}
	}

	public void Debug(string fmt, params object[] args)
	{
		Emit(10, "DEBUG", fmt, args);
	}

	public void Info(string fmt, params object[] args)
	{
		Emit(20, "INFO", fmt, args);
	}

	public void Warning(string fmt, params object[] args)
	{
		Emit(30, "WARNING", fmt, args);
	}

	public void Error(string fmt, params object[] args)
	{
		Emit(40, "ERROR", fmt, args);
	}

	public void Exception(string fmt, params object[] args)
	{
		Emit(40, "ERROR", fmt, args);
	}
}
