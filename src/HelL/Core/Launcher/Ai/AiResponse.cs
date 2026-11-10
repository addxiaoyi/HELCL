using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace HelL.Core.Launcher.Ai;

public sealed class AiResponse : IDisposable
{
	public int StatusCode;

	public HttpResponseMessage Raw;

	public Stream Body;

	private byte[] _content;

	public int ReadTimeoutMs = 90000;

	public byte[] Content
	{
		get
		{
			if (_content != null)
			{
				return _content;
			}
			MemoryStream memoryStream = new MemoryStream();
			if (Body != null)
			{
				CopyTimed(Body, memoryStream, ReadTimeoutMs);
			}
			_content = memoryStream.ToArray();
			return _content;
		}
	}

	public string Text => Encoding.UTF8.GetString(Content);

	public IEnumerable<byte[]> IterLines()
	{
		byte[] buf = new byte[8192];
		MemoryStream pending = new MemoryStream();
		bool lastCr = false;
		while (true)
		{
			int n;
			try
			{
				Task<int> task = Body.ReadAsync(buf, 0, buf.Length);
				if (!task.Wait(ReadTimeoutMs))
				{
					throw new AiStreamBroken("Read timed out.");
				}
				n = task.Result;
			}
			catch (AggregateException ex)
			{
				throw new AiStreamBroken(ex.InnerException?.Message ?? ex.Message, ex);
			}
			catch (IOException ex2)
			{
				throw new AiStreamBroken(ex2.Message, ex2);
			}
			catch (ObjectDisposedException ex3)
			{
				throw new AiStreamBroken(ex3.Message, ex3);
			}
			if (n <= 0)
			{
				break;
			}
			for (int i = 0; i < n; i++)
			{
				byte b = buf[i];
				if (b == 10 && lastCr)
				{
					lastCr = false;
					continue;
				}
				lastCr = false;
				if (b == 10 || b == 13)
				{
					lastCr = b == 13;
					yield return pending.ToArray();
					pending.SetLength(0L);
				}
				else
				{
					pending.WriteByte(b);
				}
			}
		}
		if (pending.Length > 0)
		{
			yield return pending.ToArray();
		}
	}

	public void Dispose()
	{
		try
		{
			Body?.Dispose();
		}
		catch (Exception)
		{
		}
		try
		{
			Raw?.Dispose();
		}
		catch (Exception)
		{
		}
	}

	private static void CopyTimed(Stream src, Stream dst, int ms)
	{
		byte[] buffer = new byte[8192];
		while (true)
		{
			int num;
			bool flag;
			try
			{
				num = 0;
				Task<int> task = src.ReadAsync(buffer, 0, 8192);
				flag = task.Wait(ms);
				if (flag)
				{
					num = task.Result;
				}
			}
			catch (Exception ex)
			{
				throw new AiRequestException(ex.GetBaseException().Message, ex);
			}
			if (!flag)
			{
				((IDisposable)src).Dispose();
				throw new AiRequestException("Read timed out. (上游长时间没有返回数据)");
			}
			if (num > 0)
			{
				dst.Write(buffer, 0, num);
				continue;
			}
			break;
		}
	}
}
