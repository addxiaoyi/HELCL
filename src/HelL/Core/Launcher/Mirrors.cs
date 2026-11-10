using System;
using System.Collections;
using System.Collections.Generic;

namespace HelL.Core.Launcher;

public static class Mirrors
{
	public static readonly string[] GITHUB_PROXY_PREFIXES = new string[5] { "https://gitproxy.mrhjx.cn/", "https://ghproxy.vip/", "https://gh-proxy.com/", "https://v6.gh-proxy.org/", "https://cdn.gh-proxy.com/" };

	internal static List<string> Prefixes()
	{
		try
		{
			object obj = Config.CONFIG.Get("github_proxy_prefixes");
			if (Py.Truthy(obj))
			{
				List<string> list = new List<string>();
				if (obj is string text)
				{
					string text2 = text;
					for (int i = 0; i < text2.Length; i++)
					{
						list.Add(text2[i].ToString());
					}
				}
				else
				{
					if (!(obj is IEnumerable enumerable))
					{
						return new List<string>(GITHUB_PROXY_PREFIXES);
					}
					foreach (object item in enumerable)
					{
						list.Add(Py.Str(item));
					}
				}
				return list;
			}
		}
		catch (Exception)
		{
		}
		return new List<string>(GITHUB_PROXY_PREFIXES);
	}

	public static List<string> GithubCandidates(string url)
	{
		List<string> list = new List<string>();
		HashSet<string> hashSet = new HashSet<string>();
		foreach (string item2 in Prefixes())
		{
			string item = item2 + url;
			if (hashSet.Add(item))
			{
				list.Add(item);
			}
		}
		if (!hashSet.Contains(url))
		{
			list.Add(url);
		}
		return list;
	}

	public static List<string> ExpandDownloadUrls(object url)
	{
		return Source.ExpandDownloadUrls(url);
	}
}
