using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public static class Usage
{
	private static readonly object _LOCK = new object();

	private static readonly Dictionary<string, PyDict> _data = new Dictionary<string, PyDict>();

	private static PyDict _blank()
	{
		return PyDict.Of("input", 0L, "output", 0L, "input_estimated", 0L, "requests", 0L, "real_requests", 0L);
	}

	private static void Inc(PyDict row, string k, long v)
	{
		row[k] = Py.Int(row[k]) + v;
	}

	public static void Add(string chatId, PyDict usage, long estimatedInput = 0L, long estimatedOutput = 0L)
	{
		chatId = (string.IsNullOrEmpty(chatId) ? "active" : chatId);
		lock (_LOCK)
		{
			if (!_data.TryGetValue(chatId, out PyDict value))
			{
				PyDict pyDict = (_data[chatId] = _blank());
				value = pyDict;
			}
			Inc(value, "requests", 1L);
			usage = usage ?? new PyDict();
			long num = Tokens.UsageInt(usage, "prompt_tokens", "input_tokens");
			long num2 = Tokens.UsageInt(usage, "completion_tokens", "output_tokens");
			if (num > 0 || num2 > 0)
			{
				Inc(value, "real_requests", 1L);
				Inc(value, "input", num);
				Inc(value, "output", num2);
			}
			else
			{
				Inc(value, "input_estimated", estimatedInput);
				Inc(value, "output", estimatedOutput);
			}
		}
	}

	public static PyDict Get(string chatId)
	{
		lock (_LOCK)
		{
			if (!_data.TryGetValue(string.IsNullOrEmpty(chatId) ? "active" : chatId, out PyDict value) || value.Count == 0)
			{
				return PyDict.Of("input", 0L, "output", 0L, "input_estimated", 0L, "requests", 0L, "real_requests", 0L, "source", "estimate");
			}
			PyDict pyDict = value.Copy();
			pyDict["source"] = (Py.Truthy(value["real_requests"]) ? "provider_usage" : "estimate");
			return pyDict;
		}
	}

	public static void Reset(string chatId)
	{
		lock (_LOCK)
		{
			_data.Remove(string.IsNullOrEmpty(chatId) ? "active" : chatId);
		}
	}
}
