using System;
using System.Collections.Generic;

namespace HelL;

internal static class BclExtensions
{
	public static V GetValueOrDefault<K, V>(this IReadOnlyDictionary<K, V> dict, K key)
	{
		if (!dict.TryGetValue(key, out V value))
		{
			return default(V);
		}
		return value;
	}

	public static V GetValueOrDefault<K, V>(this IReadOnlyDictionary<K, V> dict, K key, V fallback)
	{
		if (!dict.TryGetValue(key, out V value))
		{
			return fallback;
		}
		return value;
	}

	public static void Deconstruct<K, V>(this KeyValuePair<K, V> kv, out K key, out V value)
	{
		key = kv.Key;
		value = kv.Value;
	}

	public static bool TryAdd<K, V>(this Dictionary<K, V> dict, K key, V value)
	{
		if (dict.ContainsKey(key))
		{
			return false;
		}
		dict.Add(key, value);
		return true;
	}

	public static bool Contains(this string s, string value, StringComparison comparison)
	{
		return s.IndexOf(value, comparison) >= 0;
	}

	public static bool Contains(this string s, char c)
	{
		return s.IndexOf(c) >= 0;
	}

	public static bool StartsWith(this string s, char c)
	{
		if (s.Length > 0)
		{
			return s[0] == c;
		}
		return false;
	}

	public static bool EndsWith(this string s, char c)
	{
		if (s.Length > 0)
		{
			return s[s.Length - 1] == c;
		}
		return false;
	}
}
