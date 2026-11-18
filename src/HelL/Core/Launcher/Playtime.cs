using System;
using System.Collections.Generic;
using System.Linq;

namespace HelL.Core.Launcher;

public static class Playtime
{
	public const string PLAYTIME_FILE = "playtime.json";

	private static readonly object _lock = new object();

	private static string _path()
	{
		return PPath.Join(Utils.ROOT, "playtime.json");
	}

	private static PyDict _load()
	{
		object obj = Utils.ReadJson(_path(), PyDict.Of("instances", new PyDict()));
		if (!Py.Truthy(obj))
		{
			obj = PyDict.Of("instances", new PyDict());
		}
		return (PyDict)obj;
	}

	private static void _write_safe(PyDict data)
	{
		Utils.WriteJson(_path(), data);
	}

	private static object Get(object d, string k, object dflt)
	{
		if (d is PyDict pyDict)
		{
			if (!pyDict.ContainsKey(k))
			{
				return dflt;
			}
			return pyDict[k];
		}
		throw new AttributeError("'" + Py.TypeName(d) + "' object has no attribute 'get'");
	}

	private static PyDict _ensure_instance(PyDict data, string instance_name)
	{
		PyDict pyDict = (PyDict)data.Req("instances");
		if (!pyDict.ContainsKey(instance_name))
		{
			pyDict[instance_name] = PyDict.Of("total", 0L, "versions", new PyDict(), "sessions", new List<object>());
		}
		return (PyDict)pyDict[instance_name];
	}

	private static object Add(object a, object b)
	{
		if (a is double || b is double)
		{
			return Py.Float(a) + Py.Float(b);
		}
		return Py.Int(a) + Py.Int(b);
	}

	public static void RecordSession(string instance_name, string version_id, long duration)
	{
		if (duration <= 0)
		{
			return;
		}
		lock (_lock)
		{
			PyDict data = _load();
			PyDict pyDict = _ensure_instance(data, instance_name);
			pyDict["total"] = Add(Get(pyDict, "total", 0L), duration);
			PyDict pyDict2 = (PyDict)Get(pyDict, "versions", new PyDict());
			pyDict2[version_id] = Add(Get(pyDict2, version_id, 0L), duration);
			pyDict["versions"] = pyDict2;
			List<object> list = (List<object>)Get(pyDict, "sessions", new List<object>());
			list.Add(PyDict.Of("start", (long)Math.Floor(Py.Time()) - duration, "duration", duration, "version", version_id));
			if (list.Count > 500)
			{
				list = list.Skip(list.Count - 500).ToList();
			}
			pyDict["sessions"] = list;
			_write_safe(data);
		}
	}

	public static PyDict GetPlaytime(object instance_name)
	{
		object obj = Get(Get(_load(), "instances", new PyDict()), Py.Str(instance_name), null);
		if (!Py.Truthy(obj))
		{
			return PyDict.Of("total", 0L, "versions", new PyDict(), "sessions", new List<object>());
		}
		return PyDict.Of("total", Get(obj, "total", 0L), "versions", Py.DictCtor(Get(obj, "versions", new PyDict())), "sessions", Py.ListCtor(Get(obj, "sessions", new List<object>())));
	}

	public static object GetAllPlaytime()
	{
		return Get(_load(), "instances", new PyDict());
	}

	public static object GetTotalPlaytime()
	{
		PyDict d = _load();
		object obj = 0L;
		object obj2 = Get(d, "instances", new PyDict());
		foreach (KeyValuePair<string, object> item in (obj2 as PyDict) ?? throw new AttributeError("'" + Py.TypeName(obj2) + "' object has no attribute 'values'"))
		{
			obj = Add(obj, Get(item.Value, "total", 0L));
		}
		return obj;
	}

	public static string FormatDuration(object seconds)
	{
		if (seconds is double num)
		{
			if (num < 0.0)
			{
				num = 0.0;
			}
			double num2 = Math.Floor(num / 3600.0);
			double num3 = Math.Floor(PyMod(num, 3600.0) / 60.0);
			double d = PyMod(num, 60.0);
			if (num2 > 0.0)
			{
				return Py.FloatRepr(num2) + " 小时 " + Py.FloatRepr(num3) + " 分钟";
			}
			if (num3 > 0.0)
			{
				return Py.FloatRepr(num3) + " 分钟 " + Py.FloatRepr(d) + " 秒";
			}
			return Py.FloatRepr(d) + " 秒";
		}
		if (!(seconds is long) && !(seconds is bool))
		{
			throw new TypeError("'<' not supported between instances of '" + Py.TypeName(seconds) + "' and 'int'");
		}
		long num4 = ((seconds is bool flag) ? (flag ? 1 : 0) : ((long)seconds));
		if (num4 < 0)
		{
			num4 = 0L;
		}
		long num5 = num4 / 3600;
		long num6 = num4 % 3600 / 60;
		long num7 = num4 % 60;
		if (num5 > 0)
		{
			return num5 + " 小时 " + num6 + " 分钟";
		}
		if (num6 > 0)
		{
			return num6 + " 分钟 " + num7 + " 秒";
		}
		return num7 + " 秒";
	}

	private static double PyMod(double a, double b)
	{
		double num = a % b;
		if (num != 0.0 && num < 0.0 != b < 0.0)
		{
			num += b;
		}
		return num;
	}

	public static void ClearPlaytime(object instance_name, object version_id)
	{
		lock (_lock)
		{
			PyDict pyDict = _load();
			PyDict pyDict2 = Get(pyDict, "instances", new PyDict()) as PyDict;
			if (!Py.Truthy(instance_name))
			{
				pyDict["instances"] = new PyDict();
			}
			else if (pyDict2 != null && pyDict2.ContainsKey(Py.Str(instance_name)))
			{
				string key = Py.Str(instance_name);
				if (Py.Truthy(version_id))
				{
					PyDict pyDict3 = (PyDict)pyDict2[key];
					PyDict pyDict4 = Py.DictCtor(Get(pyDict3, "versions", new PyDict()));
					string key2 = Py.Str(version_id);
					object b = 0L;
					if (pyDict4.ContainsKey(key2))
					{
						b = pyDict4[key2];
						pyDict4.Remove(key2);
					}
					object obj = Sub(Get(pyDict3, "total", 0L), b);
					pyDict3["total"] = ((Py.Cmp(0L, obj) >= 0) ? ((object)0L) : obj);
					pyDict3["versions"] = pyDict4;
					pyDict3["sessions"] = ((List<object>)Get(pyDict3, "sessions", new List<object>())).Where((object s) => !Py.Eq(Get(s, "version", null), version_id)).ToList();
				}
				else
				{
					pyDict2.Remove(key);
				}
			}
			_write_safe(pyDict);
		}
	}

	private static object Sub(object a, object b)
	{
		if (a is double || b is double)
		{
			return Py.Float(a) - Py.Float(b);
		}
		return Py.Int(a) - Py.Int(b);
	}
}
