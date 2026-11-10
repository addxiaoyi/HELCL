using System;
using System.Collections;
using System.Collections.Generic;

namespace HelL.Core;

public sealed class PyDict : IEnumerable<KeyValuePair<string, object>>, IEnumerable
{
	private readonly List<string> _keys = new List<string>();

	private readonly Dictionary<string, object> _map = new Dictionary<string, object>(StringComparer.Ordinal);

	public int Count => _keys.Count;

	public IReadOnlyList<string> Keys => _keys;

	public IEnumerable<object> Values
	{
		get
		{
			foreach (string key in _keys)
			{
				yield return _map[key];
			}
		}
	}

	public object this[string key]
	{
		get
		{
			if (key == null || !_map.TryGetValue(key, out object value))
			{
				return null;
			}
			return value;
		}
		set
		{
			if (key == null)
			{
				throw new ArgumentNullException("key");
			}
			if (!_map.ContainsKey(key))
			{
				_keys.Add(key);
			}
			_map[key] = value;
		}
	}

	public PyDict()
	{
	}

	public PyDict(PyDict other)
	{
		if (other == null)
		{
			return;
		}
		foreach (KeyValuePair<string, object> item in other)
		{
			this[item.Key] = item.Value;
		}
	}

	public PyDict(IEnumerable<KeyValuePair<string, object>> pairs)
	{
		if (pairs == null)
		{
			return;
		}
		foreach (KeyValuePair<string, object> pair in pairs)
		{
			this[pair.Key] = pair.Value;
		}
	}

	public object Get(string key, object dflt = null)
	{
		if (key == null || !_map.TryGetValue(key, out object value))
		{
			return dflt;
		}
		return value;
	}

	public object Req(string key)
	{
		if (key != null && _map.TryGetValue(key, out object value))
		{
			return value;
		}
		throw new KeyError(key);
	}

	public bool ContainsKey(string key)
	{
		if (key != null)
		{
			return _map.ContainsKey(key);
		}
		return false;
	}

	public bool Has(string key)
	{
		return ContainsKey(key);
	}

	public bool TryGetValue(string key, out object value)
	{
		if (key != null)
		{
			return _map.TryGetValue(key, out value);
		}
		value = null;
		return false;
	}

	public void Add(string key, object value)
	{
		this[key] = value;
	}

	public bool Remove(string key)
	{
		if (key == null || !_map.Remove(key))
		{
			return false;
		}
		_keys.Remove(key);
		return true;
	}

	public object Pop(string key, object dflt = null)
	{
		if (key != null && _map.TryGetValue(key, out object value))
		{
			Remove(key);
			return value;
		}
		return dflt;
	}

	public object SetDefault(string key, object value)
	{
		if (_map.TryGetValue(key, out object value2))
		{
			return value2;
		}
		this[key] = value;
		return value;
	}

	public void Update(PyDict other)
	{
		if (other == null)
		{
			return;
		}
		foreach (KeyValuePair<string, object> item in other)
		{
			this[item.Key] = item.Value;
		}
	}

	public void Clear()
	{
		_keys.Clear();
		_map.Clear();
	}

	public PyDict Copy()
	{
		return new PyDict(this);
	}

	public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
	{
		string[] array = _keys.ToArray();
		string[] array2 = array;
		foreach (string key in array2)
		{
			yield return new KeyValuePair<string, object>(key, _map[key]);
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public override string ToString()
	{
		return Py.Repr(this);
	}

	public static PyDict Of(params object[] kv)
	{
		PyDict pyDict = new PyDict();
		for (int i = 0; i + 1 < kv.Length; i += 2)
		{
			pyDict[(string)kv[i]] = Py.Box(kv[i + 1]);
		}
		return pyDict;
	}
}
