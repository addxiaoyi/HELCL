using System.Collections.Generic;

namespace HelL.Core.Launcher.Ai;

public static class Tokens
{
	private const int _PER_MSG_OVERHEAD = 4;

	public static long EstimateText(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return 0L;
		}
		double num = 0.0;
		int[] array = PyDifflib.CodePoints(s);
		foreach (int num2 in array)
		{
			num = (((num2 < 19968 || num2 > 40959) && (num2 < 12288 || num2 > 12351)) ? (num + 0.25) : (num + 1.0));
		}
		return (long)num + 1;
	}

	public static long EstimateMessages(IEnumerable<object> messages)
	{
		long num = 0L;
		if (messages == null)
		{
			return 0L;
		}
		foreach (object message in messages)
		{
			if (!(message is PyDict pyDict))
			{
				continue;
			}
			num += 4;
			num += EstimateText(Py.S(pyDict.Get("content")));
			foreach (object item in Py.ListOr(pyDict.Get("tool_calls")))
			{
				PyDict pyDict2 = Py.DictOr(Agent.DGet(item, "function"));
				num += EstimateText(Py.S(pyDict2.Get("arguments")));
				num += EstimateText(Py.S(pyDict2.Get("name")));
			}
		}
		return num;
	}

	internal static long UsageInt(PyDict usage, string a, string b)
	{
		return Py.Int(Py.Or(usage.Get(a), usage.Get(b), 0L));
	}

	public static void UpdateFromUsage(TokenState st, PyDict usage, long nMessages)
	{
		usage = usage ?? new PyDict();
		long num = UsageInt(usage, "prompt_tokens", "input_tokens");
		long last_output_tokens = UsageInt(usage, "completion_tokens", "output_tokens");
		long cache_read = Py.Int(Py.Or(Agent.DGet(Py.Or(Py.Or(usage.Get("prompt_tokens_details"), new PyDict()), new PyDict()), "cached_tokens"), 0L));
		if (num > 0)
		{
			st.base_tokens = num;
			st.base_msg_index = nMessages;
			st.cache_read = cache_read;
			st.source = "provider_usage";
		}
		st.last_output_tokens = last_output_tokens;
	}

	public static long CurrentInputTokens(List<object> messages, TokenState st)
	{
		messages = messages ?? new List<object>();
		if (st.source == "provider_usage" && st.base_msg_index <= messages.Count)
		{
			return st.base_tokens + EstimateMessages(messages.GetRange((int)st.base_msg_index, messages.Count - (int)st.base_msg_index));
		}
		return EstimateMessages(messages);
	}
}
