using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace HelL.Core;

public sealed class RpcDispatcher
{
	private sealed class Entry
	{
		public MethodInfo Method;

		public ParameterInfo[] Params;

		public int VarKwIndex = -1;

		public int VarArgsIndex = -1;

		public bool IsStatic;
	}

	public const string PyClassName = "BackendAPI";

	private readonly object _target;

	private readonly Dictionary<string, Entry> _methods = new Dictionary<string, Entry>(StringComparer.Ordinal);

	public static Action<string> Warn = delegate(string s)
	{
		try
		{
			Console.Error.WriteLine(s);
		}
		catch
		{
		}
	};

	public IEnumerable<string> MethodNames => _methods.Keys;

	public RpcDispatcher(object target)
	{
		_target = target;
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
		MethodInfo[] methods = target.GetType().GetMethods(bindingAttr);
		foreach (MethodInfo methodInfo in methods)
		{
			if (!IsExposedName(methodInfo.Name) || methodInfo.IsSpecialName || methodInfo.DeclaringType == typeof(object) || methodInfo.GetCustomAttribute<RpcHiddenAttribute>() != null || methodInfo.IsGenericMethodDefinition)
			{
				continue;
			}
			if (_methods.ContainsKey(methodInfo.Name))
			{
				throw new InvalidOperationException("RPC 方法重载不受支持：" + methodInfo.Name);
			}
			Entry entry = new Entry
			{
				Method = methodInfo,
				Params = methodInfo.GetParameters(),
				IsStatic = methodInfo.IsStatic
			};
			for (int j = 0; j < entry.Params.Length; j++)
			{
				if (entry.Params[j].GetCustomAttribute<VarKwAttribute>() != null)
				{
					entry.VarKwIndex = j;
				}
				if (entry.Params[j].GetCustomAttribute<VarArgsAttribute>() != null)
				{
					entry.VarArgsIndex = j;
				}
			}
			_methods[methodInfo.Name] = entry;
		}
	}

	public static bool IsExposedName(string name)
	{
		if (string.IsNullOrEmpty(name) || name[0] == '_')
		{
			return false;
		}
		for (int i = 0; i < name.Length; i++)
		{
			if (char.IsUpper(name[i]))
			{
				return false;
			}
		}
		return true;
	}

	public bool Has(string method)
	{
		return _methods.ContainsKey(method);
	}

	public object Call(string method, object @params)
	{
		if (method.StartsWith("_"))
		{
			throw new RpcError(-32601L, "hidden method");
		}
		if (!_methods.TryGetValue(method, out Entry value))
		{
			throw new RpcError(-32601L, "unknown method: " + method);
		}
		object[] parameters;
		if (@params is List<object> list)
		{
			parameters = BindPositional(method, value, list);
		}
		else
		{
			if (!(@params is PyDict p))
			{
				throw new TypeError("params must be object or array");
			}
			parameters = BindKeywords(method, value, p);
		}
		try
		{
			return value.Method.Invoke(value.IsStatic ? null : _target, parameters);
		}
		catch (TargetInvocationException ex) when (ex.InnerException != null)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw;
		}
	}

	private static string Qual(string method)
	{
		return "BackendAPI." + method + "()";
	}

	private static IEnumerable<int> FormalIndexes(Entry e)
	{
		for (int i = 0; i < e.Params.Length; i++)
		{
			if (i != e.VarKwIndex && i != e.VarArgsIndex)
			{
				yield return i;
			}
		}
	}

	private object[] BindPositional(string method, Entry e, List<object> list)
	{
		List<int> list2 = FormalIndexes(e).ToList();
		object[] array = new object[e.Params.Length];
		bool[] array2 = new bool[e.Params.Length];
		if (list.Count > list2.Count && e.VarArgsIndex < 0)
		{
			int num = ((!e.IsStatic) ? 1 : 0);
			int num2 = list2.Count((int i) => !e.Params[i].HasDefaultValue);
			int num3 = list2.Count + num;
			int num4 = num2 + num;
			int num5 = list.Count + num;
			string text = ((num4 != num3) ? $"from {num4} to {num3} positional arguments" : string.Format("{0} positional argument{1}", num3, (num3 == 1) ? "" : "s"));
			throw new TypeError(string.Format("{0} takes {1} but {2} {3} given", Qual(method), text, num5, (num5 == 1) ? "was" : "were"));
		}
		for (int num6 = 0; num6 < Math.Min(list.Count, list2.Count); num6++)
		{
			int num7 = list2[num6];
			array[num7] = Convert(method, e.Params[num7], list[num6]);
			array2[num7] = true;
		}
		if (e.VarArgsIndex >= 0)
		{
			List<object> list3 = new List<object>();
			for (int num8 = list2.Count; num8 < list.Count; num8++)
			{
				list3.Add(list[num8]);
			}
			array[e.VarArgsIndex] = list3;
			array2[e.VarArgsIndex] = true;
		}
		if (e.VarKwIndex >= 0)
		{
			array[e.VarKwIndex] = new PyDict();
			array2[e.VarKwIndex] = true;
		}
		FillDefaults(method, e, array, array2);
		return array;
	}

	private object[] BindKeywords(string method, Entry e, PyDict p)
	{
		object[] array = new object[e.Params.Length];
		bool[] array2 = new bool[e.Params.Length];
		HashSet<string> accepted = new HashSet<string>(StringComparer.Ordinal);
		bool flag = false;
		PyDict pyDict = null;
		for (int i = 0; i < e.Params.Length; i++)
		{
			string name = e.Params[i].Name;
			if (p.ContainsKey(name))
			{
				accepted.Add(name);
				if (i == e.VarArgsIndex)
				{
					if (e.VarKwIndex < 0)
					{
						throw new TypeError(Qual(method) + " got an unexpected keyword argument '" + name + "'");
					}
					if (pyDict == null)
					{
						pyDict = new PyDict();
					}
					pyDict[name] = p[name];
				}
				else if (i == e.VarKwIndex)
				{
					if (pyDict == null)
					{
						pyDict = new PyDict();
					}
					pyDict[name] = p[name];
				}
				else
				{
					array[i] = Convert(method, e.Params[i], p[name]);
					array2[i] = true;
				}
			}
			else
			{
				if (i != e.VarKwIndex)
				{
					continue;
				}
				flag = true;
				if (pyDict == null)
				{
					pyDict = new PyDict();
				}
				foreach (KeyValuePair<string, object> item in p)
				{
					if (!accepted.Contains(item.Key) && !pyDict.ContainsKey(item.Key) && !IsLaterFormal(e, i, item.Key))
					{
						pyDict[item.Key] = item.Value;
					}
				}
			}
		}
		if (e.VarKwIndex >= 0)
		{
			array[e.VarKwIndex] = pyDict ?? new PyDict();
			array2[e.VarKwIndex] = true;
		}
		if (e.VarArgsIndex >= 0)
		{
			array[e.VarArgsIndex] = new List<object>();
			array2[e.VarArgsIndex] = true;
		}
		if (!flag)
		{
			List<string> items = p.Keys.Where((string k) => !accepted.Contains(k)).ToList();
			items = Py.Sorted(items);
			if (items.Count > 0)
			{
				List<object> o = ((IEnumerable<ParameterInfo>)e.Params).Select((Func<ParameterInfo, object>)((ParameterInfo x) => x.Name)).ToList();
				Warn("rpc " + method + ": 忽略了未知参数 " + Py.Repr(items.Cast<object>().ToList()) + "（形参只有 " + Py.Repr(o) + "）");
			}
		}
		FillDefaults(method, e, array, array2);
		return array;
	}

	private static bool IsLaterFormal(Entry e, int after, string key)
	{
		for (int i = after + 1; i < e.Params.Length; i++)
		{
			if (e.Params[i].Name == key)
			{
				return true;
			}
		}
		return false;
	}

	private static void FillDefaults(string method, Entry e, object[] args, bool[] set)
	{
		List<string> list = new List<string>();
		for (int i = 0; i < e.Params.Length; i++)
		{
			if (!set[i])
			{
				ParameterInfo parameterInfo = e.Params[i];
				DefAttribute customAttribute = parameterInfo.GetCustomAttribute<DefAttribute>();
				if (customAttribute != null)
				{
					args[i] = customAttribute.Value;
				}
				else if (parameterInfo.HasDefaultValue)
				{
					args[i] = ((parameterInfo.DefaultValue is DBNull) ? null : parameterInfo.DefaultValue);
				}
				else
				{
					list.Add(parameterInfo.Name);
				}
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		List<string> list2 = list.Select((string n) => "'" + n + "'").ToList();
		string text = ((list2.Count == 1) ? list2[0] : ((list2.Count != 2) ? (string.Join(", ", list2.Take(list2.Count - 1)) + ", and " + list2[list2.Count - 1]) : (list2[0] + " and " + list2[1])));
		throw new TypeError(string.Format("{0} missing {1} required positional argument{2}: {3}", Qual(method), list.Count, (list.Count == 1) ? "" : "s", text));
	}

	private static object Convert(string method, ParameterInfo pi, object v)
	{
		Type parameterType = pi.ParameterType;
		if (parameterType == typeof(object))
		{
			return v;
		}
		if (v == null)
		{
			if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null)
			{
				return null;
			}
			throw Mismatch(method, pi, v);
		}
		if (parameterType.IsInstanceOfType(v))
		{
			return v;
		}
		Type type = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
		if (type == typeof(long))
		{
			if (v is bool flag)
			{
				return (long)(flag ? 1 : 0);
			}
			if (v is double num && num == Math.Floor(num) && !double.IsInfinity(num))
			{
				return (long)num;
			}
			if (v is BigInteger bigInteger && bigInteger >= long.MinValue && bigInteger <= long.MaxValue)
			{
				return (long)bigInteger;
			}
		}
		if (type == typeof(int))
		{
			if (v is long num2 && num2 >= int.MinValue && num2 <= int.MaxValue)
			{
				return (int)num2;
			}
			if (v is bool flag2)
			{
				return flag2 ? 1 : 0;
			}
		}
		if (type == typeof(double))
		{
			if (v is long num3)
			{
				return (double)num3;
			}
			if (v is bool)
			{
				return ((bool)v) ? 1.0 : 0.0;
			}
		}
		if (type == typeof(bool))
		{
			return Py.Truthy(v);
		}
		throw Mismatch(method, pi, v);
	}

	private static TypeError Mismatch(string method, ParameterInfo pi, object v)
	{
		return new TypeError(Qual(method) + " 参数 " + pi.Name + " 类型不符：收到 " + Py.TypeName(v));
	}
}
