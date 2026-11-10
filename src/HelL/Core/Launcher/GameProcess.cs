using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace HelL.Core.Launcher;

public sealed class GameProcess
{
	public readonly Process proc;

	public readonly double started_at;

	private readonly Action<string> on_line;

	private readonly LinkedList<string> lines = new LinkedList<string>();

	private readonly object _lk = new object();

	private readonly Thread _t1;

	private readonly Thread _t2;

	private bool _started;

	public GameProcess(IList<string> cmd, string cwd, Action<string> on_line = null, PyDict env = null, string priority = "normal", string window_title = "", bool start_readers = true)
	{
		ProcessStartInfo startInfo = Utils.MakeStartInfo(cmd, cwd, env);
		proc = Process.Start(startInfo);
		try
		{
			proc.StandardInput.Close();
		}
		catch (Exception)
		{
		}
		started_at = Py.Time();
		this.on_line = on_line;
		try
		{
			McLauncher._set_priority(proc.Id, priority);
		}
		catch (Exception)
		{
		}
		McLauncher.WatchWindowTitle(proc, window_title);
		_t1 = new Thread((ThreadStart)delegate
		{
			Reader(proc.StandardOutput);
		})
		{
			IsBackground = true
		};
		_t2 = new Thread((ThreadStart)delegate
		{
			Reader(proc.StandardError);
		})
		{
			IsBackground = true
		};
		if (start_readers)
		{
			StartReaders();
		}
	}

	public void StartReaders()
	{
		lock (_lk)
		{
			if (_started)
			{
				return;
			}
			_started = true;
		}
		_t1.Start();
		_t2.Start();
	}

	public List<string> LastLines()
	{
		lock (_lk)
		{
			return lines.ToList();
		}
	}

	private void Reader(StreamReader r)
	{
		try
		{
			string text;
			while ((text = r.ReadLine()) != null)
			{
				lock (_lk)
				{
					lines.AddLast(text);
					while (lines.Count > 200)
					{
						lines.RemoveFirst();
					}
					if (on_line != null)
					{
						try
						{
							on_line(text);
						}
						catch (Exception)
						{
						}
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public long? Poll()
	{
		try
		{
			return proc.HasExited ? new long?(proc.ExitCode) : ((long?)null);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public long Wait()
	{
		StartReaders();
		proc.WaitForExit();
		_t1.Join(2000);
		_t2.Join(2000);
		return proc.ExitCode;
	}

	public void Kill()
	{
		try
		{
			proc.Kill();
		}
		catch (Exception)
		{
		}
	}
}
