using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace HelL.Core.Launcher;

public static class Lan
{
	public static List<object> LocalIps()
	{
		List<object> list = new List<object>();
		try
		{
			IPAddress[] hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
			foreach (IPAddress iPAddress in hostAddresses)
			{
				if (iPAddress.AddressFamily == AddressFamily.InterNetwork)
				{
					string text = iPAddress.ToString();
					if (text.Length > 0 && !text.StartsWith("127.", StringComparison.Ordinal) && !text.StartsWith("169.254.", StringComparison.Ordinal) && !list.Contains(text))
					{
						list.Add(text);
					}
				}
			}
		}
		catch (SocketException)
		{
		}
		try
		{
			using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Connect(new IPEndPoint(IPAddress.Parse("223.5.5.5"), 80));
			string text2 = ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
			if (text2.Length > 0 && !list.Contains(text2) && !text2.StartsWith("127.", StringComparison.Ordinal) && !text2.StartsWith("169.254.", StringComparison.Ordinal))
			{
				list.Insert(0, text2);
			}
		}
		catch (SocketException)
		{
		}
		if (list.Count <= 0)
		{
			return new List<object> { "127.0.0.1" };
		}
		return list.OrderByDescending(ip => IsPrivate(ip.ToString())).ToList();
	}

	private static bool IsPrivate(string ip)
	{
		if (ip.StartsWith("192.168.", StringComparison.Ordinal) || ip.StartsWith("10.", StringComparison.Ordinal))
		{
			return true;
		}
		if (ip.StartsWith("172.", StringComparison.Ordinal))
		{
			string[] parts = ip.Split('.');
			int second;
			if (parts.Length > 1 && int.TryParse(parts[1], out second))
			{
				return second >= 16 && second <= 31;
			}
		}
		return false;
	}

	public static string LanHint(object port)
	{
		IEnumerable<string> values = from ip in LocalIps()
			select ip?.ToString() + ":" + Py.Str(port);
		return "房主在游戏里「对局域网开放」后，把下面地址发给好友：\n" + string.Join("\n", values);
	}
}
